using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #65: running and worn machinery is audible, and noise makes
/// sleep less restorative. C# owns only loudness and its cost.
/// </summary>
public sealed class MachineryNoiseTests
{
    [Fact]
    public void HealthyRunningMachineryMakesItsBaseNoiseAndQuietEquipmentMakesNone()
    {
        var state = HealthyStation();
        var vent = state.Devices["ventilation:quarters"];
        var lighting = state.Devices["lighting:quarters"];

        Assert.Equal(StationNoiseSystem.BaseLevel(StationSystemKind.Ventilation), StationNoiseSystem.DeviceLevel(state, vent));
        Assert.Equal(0, StationNoiseSystem.DeviceLevel(state, lighting));
        Assert.True(
            StationNoiseSystem.BaseLevel(StationSystemKind.Reactor)
            > StationNoiseSystem.BaseLevel(StationSystemKind.Ventilation));
    }

    [Fact]
    public void WornMachineryGetsLouderAsItsConditionFalls()
    {
        var state = HealthyStation();
        var vent = state.Devices["ventilation:quarters"];
        var healthy = StationNoiseSystem.DeviceLevel(state, vent);

        vent.Condition = vent.DegradedAt + 1;
        Assert.Equal(healthy, StationNoiseSystem.DeviceLevel(state, vent));

        vent.Condition = vent.DegradedAt / 2;
        var halfWorn = StationNoiseSystem.DeviceLevel(state, vent);
        vent.Condition = 1;
        var nearlyDead = StationNoiseSystem.DeviceLevel(state, vent);

        Assert.True(halfWorn > healthy);
        Assert.True(nearlyDead > halfWorn);
        Assert.True(nearlyDead <= healthy * StationNoiseSystem.MaxWearAmplification);
    }

    [Fact]
    public void StoppedFailedOrUnpoweredMachineryIsSilentButPowerSourcesStillRun()
    {
        var state = HealthyStation();
        var vent = state.Devices["ventilation:quarters"];
        vent.Condition = 5;

        vent.IsEnabled = false;
        Assert.Equal(0, StationNoiseSystem.DeviceLevel(state, vent));

        vent.IsEnabled = true;
        vent.Condition = 0;
        Assert.Equal(0, StationNoiseSystem.DeviceLevel(state, vent));

        vent.Condition = 5;
        state.Facility.Rooms["quarters"].IsPowered = false;
        Assert.Equal(0, StationNoiseSystem.DeviceLevel(state, vent));

        var reactor = state.Devices.Values.First(device => device.Kind == StationSystemKind.Reactor);
        state.Facility.Rooms[reactor.RoomId].IsPowered = false;
        Assert.True(StationNoiseSystem.DeviceLevel(state, reactor) > 0);
    }

    [Fact]
    public void SoundCarriesAtHalfStrengthThroughAnOpenHatchAndAClosedHatchBlocksIt()
    {
        var state = HealthyStation();
        var door = state.Facility.Doors.First(candidate =>
            candidate.RoomAId == "quarters" || candidate.RoomBId == "quarters");
        var neighbour = door.RoomAId == "quarters" ? door.RoomBId : door.RoomAId;
        var loud = AddLoudMachine(state, neighbour);
        var ownLevel = StationNoiseSystem.DeviceLevel(state, loud);
        Assert.True(ownLevel > 0);

        door.IsOpen = true;
        var carried = StationNoiseSystem.Sources(state, "quarters")
            .Single(source => source.Device.Id == loud.Id);
        Assert.Equal(ownLevel * StationNoiseSystem.OpenHatchTransmission, carried.Level, 6);
        Assert.Equal(neighbour, carried.RoomId);

        door.IsOpen = false;
        Assert.DoesNotContain(
            StationNoiseSystem.Sources(state, "quarters"),
            source => source.Device.Id == loud.Id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(29.9, 1)]
    [InlineData(50, 0.65)]
    [InlineData(70, 0.3)]
    [InlineData(500, 0.3)]
    public void RestRecoveryFallsLinearlyAboveTheDisturbanceThreshold(double noise, double expected)
    {
        Assert.Equal(expected, StationNoiseSystem.RestRecoveryFactor(noise), 6);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(1_234)]
    [InlineData(98_765)]
    public void AHealthyCrewQuartersIsRestful(int seed)
    {
        var state = HealthyStation(seed);

        Assert.False(StationNoiseSystem.IsDisturbing(StationNoiseSystem.RoomLevel(state, "quarters")));
    }

    [Fact]
    public void SleepingBesideARattlingMachineRestoresLessThanSleepingInQuiet()
    {
        var quiet = SleepOneHour(state => { });
        var noisy = SleepOneHour(state => state.Devices["ventilation:quarters"].Condition = 3);

        Assert.True(noisy.FatigueRecovered > 0);
        Assert.True(noisy.FatigueRecovered < quiet.FatigueRecovered * 0.85);
        Assert.True(noisy.SleepDebtRecovered < quiet.SleepDebtRecovered * 0.85);
    }

    [Fact]
    public void DisconnectingTheNoisyMachineRestoresRestfulSleep()
    {
        var quiet = SleepOneHour(state => { });
        var disconnected = SleepOneHour(state =>
        {
            var vent = state.Devices["ventilation:quarters"];
            vent.Condition = 3;
            vent.IsEnabled = false;
        });

        Assert.Equal(quiet.FatigueRecovered, disconnected.FatigueRecovered, 6);
    }

    [Fact]
    public void CognitionHearsALoudRoomAndWhichMachineIsMakingIt()
    {
        var state = HealthyStation();
        var npc = state.Crew.First(candidate => !candidate.IsPrisoner);
        npc.CurrentRoomId = "quarters";

        Assert.DoesNotContain("NOISE:", NpcPromptBuilder.Build(npc, state));

        state.Devices["ventilation:quarters"].Condition = 3;
        var prompt = NpcPromptBuilder.Build(npc, state);
        var noiseLine = prompt.Split('\n').Single(line => line.StartsWith("NOISE:", StringComparison.Ordinal));

        Assert.Contains("[ventilation:quarters]", noiseLine);
        Assert.Contains("worn and rattling at 3% condition", noiseLine);
        Assert.Contains("up to you", noiseLine);
    }

    private static (double FatigueRecovered, double SleepDebtRecovered) SleepOneHour(Action<GameState> arrange)
    {
        var state = HealthyStation();
        var sleeper = state.Crew.First(npc => !npc.IsPrisoner);
        foreach (var other in state.Crew.Where(npc => npc.Id != sleeper.Id))
            other.IsPresent = false;

        var quarters = state.Facility.Rooms["quarters"];
        var bed = quarters.Fixtures.First(fixture => fixture.Type == FixtureType.Bed);
        sleeper.CurrentRoomId = quarters.Id;
        sleeper.PositionX = bed.X;
        sleeper.PositionY = bed.Y;
        sleeper.CurrentAction = new NpcAction(ActionKind.Sleep, null, "Sleeping.");
        sleeper.Fatigue = 90;
        sleeper.SleepDebtMinutes = 600;
        arrange(state);
        Assert.True(SimulationEngine.IsPhysicallyAsleep(state, sleeper));

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(20));

        return (90 - sleeper.Fatigue, 600 - sleeper.SleepDebtMinutes);
    }

    private static GameState HealthyStation(int seed = 4_242)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: seed);
        foreach (var device in state.Devices.Values)
        {
            device.Condition = 100;
            device.IsEnabled = true;
        }

        return state;
    }

    private static StationDevice AddLoudMachine(GameState state, string roomId)
    {
        var pump = new StationDevice
        {
            Id = $"test-pump:{roomId}",
            Kind = StationSystemKind.CoolantPump,
            RoomId = roomId,
            Label = "Test coolant pump",
            Discipline = MaintenanceDiscipline.Mechanical,
            WearPerHour = 0
        };
        state.Devices[pump.Id] = pump;
        state.Facility.Rooms[roomId].IsPowered = true;
        return pump;
    }
}
