using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewMaintenanceSystemTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>Everything a station needs to run itself for a while.</summary>
    private sealed class Station
    {
        private readonly SimulationEngine _simulation = new();
        private readonly EnvironmentSystem _environment = new();
        private readonly StationUpkeepSystem _upkeep = new();
        private readonly CrewMaintenanceSystem _maintenance = new();
        private readonly CrewProvisioningSystem _provisioning = new();
        private readonly CrewRoutineSystem _routines = new();
        private readonly IntentExecutionSystem _intents = new();
        private readonly LocalMovementSystem _movement = new();
        private readonly BrowserMindSystem _mind = new();
        private readonly ConversationPacingSystem _pacing = new();

        public void Tick(GameState state)
        {
            _upkeep.Tick(state, Minute);
            _environment.Tick(state, Minute);
            _simulation.Tick(state, Minute);
            _mind.Tick(state);
            _intents.Tick(state);
            _provisioning.Tick(state, Minute);
            _maintenance.Tick(state);
            _pacing.Tick(state);
            _routines.Tick(state);
            _movement.Tick(state, Minute);
        }

        public void Run(GameState state, int minutes)
        {
            for (var i = 0; i < minutes; i++)
            {
                Tick(state);
            }
        }
    }

    [Fact]
    public void SomebodyQualifiedIsSentToFixAFailedUnit()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var device = state.Devices["lighting:medical"];
        device.Condition = 0;

        new CrewMaintenanceSystem().Tick(state);

        var assigned = state.Crew.SingleOrDefault(npc => npc.ServicingDeviceId == device.Id);

        Assert.NotNull(assigned);
        Assert.Equal(ActionKind.Repair, assigned!.Intent!.Action);
        Assert.Equal(device.RoomId, assigned.Intent.TargetId);
    }

    [Fact]
    public void TwoPeopleAreNeverSentToTheSameUnit()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);

        foreach (var device in state.Devices.Values)
        {
            device.Condition = 100;
        }

        state.Devices["lighting:medical"].Condition = 0;

        var system = new CrewMaintenanceSystem();
        system.Tick(state);
        system.Tick(state);

        Assert.Single(state.Crew, npc => npc.ServicingDeviceId == "lighting:medical");
    }

    [Fact]
    public void ServicingRestoresConditionAndHandsTheFunctionBack()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var device = state.Devices["lighting:medical"];
        device.Condition = 0;

        new StationUpkeepSystem().Tick(state, Minute);
        Assert.False(state.Facility.Rooms["medical"].LightsOn);

        new Station().Run(state, 90);

        Assert.True(device.Condition > 0);
        Assert.True(state.Facility.Rooms["medical"].LightsOn);
    }

    [Fact]
    public void WorkCannotProceedInAnUnpoweredCompartment()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var device = state.Devices["climate:medical"];
        device.Condition = 0;

        var worker = state.Crew.First(npc => StationUpkeepRules.CanService(npc, device));
        worker.CurrentRoomId = "medical";
        worker.ServicingDeviceId = device.Id;
        worker.Intent = new NpcIntent(
            ActionKind.Repair, "medical", "Fix it.", "It is broken.", 60, "Test", state.Elapsed);

        state.Facility.Rooms["medical"].IsPowered = false;

        var maintenance = new CrewMaintenanceSystem();

        for (var i = 0; i < 60; i++)
        {
            state.Elapsed += Minute;
            maintenance.Tick(state);
        }

        // Overseer cutting the power is exactly how you stop a repair.
        Assert.Equal(0, device.Condition);
    }

    [Fact]
    public void AbandoningTheJobReleasesItForSomebodyElse()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var device = state.Devices["lighting:medical"];
        device.Condition = 0;

        var maintenance = new CrewMaintenanceSystem();
        maintenance.Tick(state);

        var assigned = state.Crew.Single(npc => npc.ServicingDeviceId == device.Id);

        // The model decides they would rather do something else.
        assigned.Intent = new NpcIntent(
            ActionKind.Eat, null, "Get food.", "I am hungry.", 90, "Ollama", state.Elapsed);

        maintenance.Tick(state);

        Assert.Null(assigned.ServicingDeviceId);
    }

    [Fact]
    public void TheCrewRoughlyKeepUpWithOrdinaryWearOverAFullDay()
    {
        foreach (var seed in new[] { 1, 7, 99 })
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: seed);
            var before = state.Devices.Values.Average(d => d.Condition);

            new Station().Run(state, 1440);

            var after = state.Devices.Values.Average(d => d.Condition);

            // They should hold the line without gaining ground — close enough
            // that anything Overseer does tips the balance.
            Assert.True(
                after > before - 12,
                $"seed {seed}: condition collapsed from {before:0.0} to {after:0.0}.");

            Assert.Equal(0, state.Devices.Values.Count(d => d.IsFailed));
            Assert.True(
                state.Crew.Count(npc => npc.IsAlive) == 6,
                $"seed {seed}: deaths = {string.Join("; ", state.Crew.Where(npc => !npc.IsAlive).Select(npc => $"{npc.Name}: {npc.CauseOfDeath}, room={npc.CurrentRoomId}, hunger={npc.Hunger:0.0}, health={npc.Health:0.0}"))}");
        }
    }

    [Fact]
    public void SealingTheCrewAwayFromTheirWorkLetsTheStationRot()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 7);

        // Overseer locks the station down: nobody can get to anything.
        foreach (var door in state.Facility.Doors)
        {
            door.IsOpen = false;
            door.IsLocked = true;
        }

        var before = state.Devices.Values.Average(d => d.Condition);
        new Station().Run(state, 1440);
        var after = state.Devices.Values.Average(d => d.Condition);

        Assert.True(
            after < before,
            "A crew that cannot reach the equipment cannot maintain it.");
    }
}
