using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ScenarioShutdownTests
{
    [Fact]
    public void DefaultScenario_SeedsPhysicalShutdownControl()
    {
        var state = FacilitySeeder.CreateDefault();
        Assert.Equal("secure-continuity", state.Scenario?.Id);
        var shutdown = Assert.Single(state.ShutdownMechanisms);
        Assert.Equal("isolation", shutdown.RoomId);
        Assert.Contains(state.Facility.Rooms["isolation"].Fixtures, f => f.Type == FixtureType.OverseerShutdown);
    }

    [Fact]
    public void RestrictingShutdownRoute_CreatesIndividualEvidence()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew.First(n => n.KnowsShutdownControl);
        npc.CurrentRoomId = "hall-isolation";
        var door = state.Facility.FindDoorBetween("isolation", "hall-isolation")!;
        door.IsOpen = false;
        door.IsLocked = true;

        new SuspicionSystem().ObservePlayerDoorChange(state, door, true);

        Assert.True(npc.OverseerSuspicion > 0);
        Assert.NotEmpty(npc.OverseerEvidence);
    }

    [Fact]
    public void SuspiciousCrew_FormShutdownGoalButCannotBypassSealedRoute()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(n => n.Name == "Marcus Reed");
        marcus.OverseerSuspicion = 90;
        var door = state.Facility.FindDoorBetween("isolation", "hall-isolation")!;
        door.IsOpen = false;
        door.IsLocked = true;

        new SuspicionSystem().Tick(state);

        Assert.Null(marcus.Intent);
        Assert.NotEqual("isolation", marcus.CurrentRoomId);
    }

    [Fact]
    public void ShutdownRequiresPhysicalPresenceAndProducesFailureOnlyAfterActivation()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(n => n.Name == "Marcus Reed");
        var mechanism = Assert.Single(state.ShutdownMechanisms);
        marcus.CurrentRoomId = mechanism.RoomId;
        marcus.OverseerSuspicion = 90;
        marcus.Intent = new NpcIntent(ActionKind.ShutdownOverseer, mechanism.Id, "Shut it down.", "Evidence.", 95, "Test", state.Elapsed);

        new IntentExecutionSystem().Tick(state);
        Assert.Equal(ActionKind.ShutdownOverseer, marcus.CurrentAction.Kind);
        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);

        var shutdown = new ShutdownSystem();
        shutdown.Tick(state);
        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);

        state.Elapsed += TimeSpan.FromMinutes(mechanism.ActivationMinutes);
        shutdown.Tick(state);
        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.Contains("Marcus Reed", state.ScenarioOutcome);
    }

    [Fact]
    public void DoorRestriction_IsNotMagicallyObservedFromElsewhere()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(n => n.Name == "David Hale");
        david.CurrentRoomId = "control";
        var door = state.Facility.FindDoorBetween("isolation", "hall-isolation")!;
        door.IsOpen = false;
        door.IsLocked = true;

        new SuspicionSystem().ObservePlayerDoorChange(state, door, true);

        Assert.Equal(0, david.OverseerSuspicion);
        Assert.Empty(david.OverseerEvidence);
    }

    [Fact]
    public void CrewOverridableRoute_CanBePhysicallyForcedBySkilledCrew()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, new ScenarioDefinition(
            "override", "OVERRIDE", "", ShutdownAccessVariant.CrewOverridable, []));

        var marcus = state.Crew.Single(n => n.Name == "Marcus Reed");
        marcus.CurrentRoomId = "corridor";
        marcus.OverseerSuspicion = 90;
        var door = state.Facility.FindDoorBetween("hall-isolation", "corridor")!;
        door.IsOpen = false;
        door.IsLocked = true;

        new SuspicionSystem().Tick(state);
        Assert.NotNull(marcus.Intent);

        new IntentExecutionSystem().Tick(state);
        Assert.Equal(ActionKind.OverrideDoor, marcus.CurrentAction.Kind);
        Assert.Equal(door.Id, marcus.CurrentAction.TargetId);

        var overrides = new ManualOverrideSystem();
        overrides.Tick(state);
        Assert.True(marcus.RoutineUntil > state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(door.ManualOverrideMinutes);
        overrides.Tick(state);

        Assert.True(door.IsManuallyOverridden);
        Assert.True(door.IsPassable);
        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
    }

    [Fact]
    public void ImpossibleToSealRoute_RemovesOverseerDoorAuthority()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, new ScenarioDefinition(
            "analogue", "ANALOGUE", "", ShutdownAccessVariant.ImpossibleToSeal, []));

        var routeDoors = state.Facility.Doors.Where(d =>
            d.Connects("isolation", "hall-isolation")
            || d.Connects("hall-isolation", "corridor"));

        Assert.All(routeDoors, door =>
        {
            Assert.False(door.IsAiControllable);
            Assert.True(door.IsManuallyOverridden);
            Assert.True(door.IsPassable);
        });
    }

    [Fact]
    public void UnskilledCrew_CannotForceCrewOverridableDoor()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, new ScenarioDefinition(
            "override", "OVERRIDE", "", ShutdownAccessVariant.CrewOverridable, []));

        var nadia = state.Crew.Single(n => n.Name == "Nadia Okafor");
        nadia.CurrentRoomId = "corridor";
        var door = state.Facility.FindDoorBetween("hall-isolation", "corridor")!;
        door.IsOpen = false;
        door.IsLocked = true;

        var applied = new ActionResolver().TryApply(
            state,
            nadia.Id,
            new NpcAction(ActionKind.OverrideDoor, door.Id, "Force it."),
            out _);

        Assert.False(applied);
        Assert.False(door.IsManuallyOverridden);
    }

    [Fact]
    public void ScenarioCatalog_SupportsAbsentAndRedundantShutdowns()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, new ScenarioDefinition("none", "NONE", "", ShutdownAccessVariant.Absent, []));
        Assert.Empty(state.ShutdownMechanisms);

        ScenarioCatalog.Apply(state, new ScenarioDefinition("redundant", "REDUNDANT", "", ShutdownAccessVariant.Redundant, []));
        Assert.Equal(2, state.ShutdownMechanisms.Count);
    }
}
