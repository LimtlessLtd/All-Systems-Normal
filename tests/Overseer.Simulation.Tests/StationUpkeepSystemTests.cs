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
}
