using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationUpkeepSystemTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void EveryRoomDoorAndPlantSystemIsRegisteredAsMaintainable()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);

        Assert.NotEmpty(state.Devices);

        foreach (var room in state.Facility.Rooms.Values)
        {
            Assert.Contains(state.Devices.Values, d =>
                d.Kind == StationSystemKind.Lighting && d.RoomId == room.Id);
            Assert.Contains(state.Devices.Values, d =>
                d.Kind == StationSystemKind.Camera && d.RoomId == room.Id);
        }

        foreach (var door in state.Facility.Doors)
        {
            Assert.Contains(state.Devices.Values, d => d.DoorId == door.Id);
        }

        Assert.Contains(state.Devices.Values, d => d.Kind == StationSystemKind.Reactor);
        Assert.Contains(state.Devices.Values, d => d.Kind == StationSystemKind.PowerGenerator);
        Assert.Contains(state.Devices.Values, d => d.Kind == StationSystemKind.GrowBeds);
        Assert.Contains(state.Devices.Values, d => d.Kind == StationSystemKind.GalleyEquipment);
        Assert.Contains(state.Devices.Values, d => d.Kind == StationSystemKind.LifeSupport);
        Assert.Contains(state.Devices.Values, d => d.Kind == StationSystemKind.IsolationMechanism);
    }

    [Fact]
    public void AStationIsNotDeliveredNewAndSomeSeedsHandOverABacklog()
    {
        // Across a spread of seeds, some stations should start with equipment
        // already in poor shape. A game that always begins pristine loses the
        // "you inherited this mess" texture entirely.
        var seedsWithWornKit = 0;

        for (var seed = 0; seed < 25; seed++)
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: seed);

            if (state.Devices.Values.Any(device => device.Condition < 35))
            {
                seedsWithWornKit++;
            }
        }

        Assert.True(
            seedsWithWornKit > 10,
            $"Only {seedsWithWornKit}/25 seeds started with worn equipment.");
    }

    [Fact]
    public void TheSameSeedAlwaysProducesTheSameStation()
    {
        var first = FacilitySeeder.CreateDefault(upkeepSeed: 4242);
        var second = FacilitySeeder.CreateDefault(upkeepSeed: 4242);

        Assert.Equal(first.Devices.Count, second.Devices.Count);

        foreach (var (id, device) in first.Devices)
        {
            Assert.Equal(device.Condition, second.Devices[id].Condition, 3);
            Assert.Equal(device.WearPerHour, second.Devices[id].WearPerHour, 6);
        }
    }

    [Fact]
    public void EquipmentWearsOutWhenNobodyServicesIt()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        var before = state.Devices.Values.Average(d => d.Condition);

        for (var hour = 0; hour < 24; hour++)
        {
            system.Tick(state, Hour);
        }

        Assert.True(
            state.Devices.Values.Average(d => d.Condition) < before,
            "A station nobody maintains should run itself down.");
    }

    [Fact]
    public void EquipmentInAnUnpoweredCompartmentBarelyWears()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        var powered = state.Devices["lighting:medical"];
        var unpowered = state.Devices["lighting:storage"];

        // Match them so the comparison is about power, not their wear rates.
        powered.Condition = 90;
        unpowered.Condition = 90;
        state.Facility.Rooms["storage"].IsPowered = false;

        for (var hour = 0; hour < 20; hour++)
        {
            system.Tick(state, Hour);
        }

        Assert.True(
            unpowered.Condition > powered.Condition,
            "Equipment that is not running should not be wearing out.");
    }

    [Fact]
    public void AFailedLightTakesTheCompartmentDarkAndOverseerCannotSwitchItBack()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        state.Devices["lighting:medical"].Condition = 0;
        system.Tick(state, Minute);

        Assert.False(state.Facility.Rooms["medical"].LightsOn);
    }

    [Fact]
    public void AFailedDoorActuatorTakesTheHatchOutOfOverseersHands()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        var door = state.Facility.Doors[0];
        state.Devices[$"door:{door.Id}"].Condition = 0;

        system.Tick(state, Minute);

        Assert.False(door.IsAiControllable);
        Assert.True(door.IsDamaged);
    }

    [Fact]
    public void AFailedIsolationSwitchIsOneTheCrewCannotUse()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        var mechanism = state.ShutdownMechanisms[0];
        Assert.True(mechanism.IsOnline);

        state.Devices[$"isolation:{mechanism.Id}"].Condition = 0;
        system.Tick(state, Minute);

        Assert.False(mechanism.IsOnline);
    }

    [Fact]
    public void AHealthyStationRunsComfortablyInsideItsGeneration()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        new StationUpkeepSystem().Tick(state, Minute);

        Assert.False(state.Power.IsBrownedOut);
        Assert.Empty(state.Power.SheddedRoomIds);

        // Shedding by default used to drop the kitchen, which quietly starved
        // the crew before any food system existed.
        Assert.True(state.Facility.Rooms["kitchen"].IsPowered);
    }

    [Fact]
    public void LosingGenerationShedsLoadAndRestoringItGivesThePowerBack()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        var reactor = state.Devices.Values.Single(d => d.Kind == StationSystemKind.Reactor);
        var generator = state.Devices.Values.Single(d => d.Kind == StationSystemKind.PowerGenerator);

        reactor.Condition = 0;
        generator.Condition = 0;
        system.Tick(state, Minute);

        Assert.NotEmpty(state.Power.SheddedRoomIds);

        // The grid is machinery, not an Overseer decision: fix generation and
        // the compartments come back on their own.
        reactor.Condition = 100;
        generator.Condition = 100;

        for (var i = 0; i < 40; i++)
        {
            system.Tick(state, Minute);
        }

        Assert.Empty(state.Power.SheddedRoomIds);
    }

    [Fact]
    public void TheReactorAndCorridorAreNeverShed()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        foreach (var device in state.Devices.Values.Where(d =>
                     d.Kind is StationSystemKind.Reactor or StationSystemKind.PowerGenerator))
        {
            device.Condition = 0;
        }

        for (var i = 0; i < 10; i++)
        {
            system.Tick(state, Minute);
        }

        Assert.DoesNotContain("corridor", state.Power.SheddedRoomIds);
        Assert.DoesNotContain("reactor", state.Power.SheddedRoomIds);
    }

    [Fact]
    public void EngineeringIsTheLastCompartmentTheGridLetsGo()
    {
        // Engineering houses the O2 generator, scrubbers and life-support core.
        // It used to be shed before Medical, the Control Room, the Airlock and
        // Containment, so a worn reactor suffocated crew in lit rooms.
        for (var reactorCondition = 0; reactorCondition <= 60; reactorCondition += 2)
        {
            var state = BrownedOutStation(reactorCondition);
            var system = new StationUpkeepSystem();

            for (var minute = 0; minute < 30; minute++)
            {
                system.Tick(state, Minute);

                if (!state.Power.SheddedRoomIds.Contains("engineering"))
                {
                    continue;
                }

                var stillLit = state.Facility.Rooms.Values
                    .Where(room => room.Id != "engineering"
                        && room.Type is not (RoomType.Reactor or RoomType.Generator or RoomType.Corridor)
                        && room.IsPowered)
                    .Select(room => room.Id)
                    .ToList();
                Assert.True(
                    stillLit.Count == 0,
                    $"reactor {reactorCondition}%, minute {minute}: Engineering shed while {string.Join(", ", stillLit)} stayed lit.");
            }
        }
    }

    [Fact]
    public void SheddingAndRestoringCountTheMachinesInTheRoom()
    {
        // The grid used to count only a room's own load when it shed or
        // restored it, not the machines that go dark or come back with it. So
        // it shed more rooms than the deficit needed, and a restored room could
        // overload the grid on the next tick and be shed again. In a soak,
        // Engineering blinked on and off every three minutes. The demand the
        // grid books when it switches rooms must match what it measures next.
        for (var reactorCondition = 0.0; reactorCondition <= 60; reactorCondition += 0.5)
        {
            var state = BrownedOutStation(reactorCondition);
            var system = new StationUpkeepSystem();
            var reactor = state.Devices.Values.Single(d => d.Kind == StationSystemKind.Reactor);

            for (var minute = 0; minute < 20; minute++)
            {
                reactor.Condition = reactorCondition;
                var shedBefore = state.Power.SheddedRoomIds.ToHashSet();
                system.Tick(state, Minute);
                if (state.Power.SheddedRoomIds.SetEquals(shedBefore))
                {
                    continue;
                }

                var booked = state.Power.DemandKilowatts;
                var shedAfter = state.Power.SheddedRoomIds.ToHashSet();
                reactor.Condition = reactorCondition;
                system.Tick(state, Minute);
                if (!state.Power.SheddedRoomIds.SetEquals(shedAfter))
                {
                    continue;
                }

                Assert.True(
                    Math.Abs(booked - state.Power.DemandKilowatts) < 0.01,
                    $"reactor {reactorCondition}%, minute {minute}: booked {booked:F1} kW but measured {state.Power.DemandKilowatts:F1} kW.");
            }
        }
    }

    [Fact]
    public void TheMostImportantCompartmentGetsPowerBackFirst()
    {
        var state = BrownedOutStation(0);
        var system = new StationUpkeepSystem();
        system.Tick(state, Minute);
        Assert.Contains("engineering", state.Power.SheddedRoomIds);
        Assert.Contains("lounge", state.Power.SheddedRoomIds);

        // Just enough generation back for one more compartment: it must be
        // Engineering, whatever order the shed set happens to enumerate in.
        var reactor = state.Devices.Values.Single(d => d.Kind == StationSystemKind.Reactor);
        state.Power.StoredKilowattHours = 0;
        for (var condition = 0; condition <= 100 && state.Power.SheddedRoomIds.Contains("engineering"); condition++)
        {
            reactor.Condition = condition;
            var shedBefore = state.Power.SheddedRoomIds.ToHashSet();
            system.Tick(state, Minute);

            var restored = shedBefore.Except(state.Power.SheddedRoomIds).ToList();
            if (restored.Count > 0)
            {
                Assert.Contains("engineering", restored);
            }
        }

        Assert.DoesNotContain("engineering", state.Power.SheddedRoomIds);
    }

    private static GameState BrownedOutStation(double reactorCondition)
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Devices.Values.Single(d => d.Kind == StationSystemKind.PowerGenerator).Condition = 0;
        state.Devices.Values.Single(d => d.Kind == StationSystemKind.Reactor).Condition = reactorCondition;
        return state;
    }
}
