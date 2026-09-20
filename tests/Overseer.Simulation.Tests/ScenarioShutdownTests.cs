using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ScenarioShutdownTests
{
    [Fact]
    public void DefaultScenario_SeedsPhysicalShutdownControlButNotOmniscientKnowledge()
    {
        var state = FacilitySeeder.CreateDefault();

        Assert.Equal("secure-continuity", state.Scenario?.Id);
        var shutdown = Assert.Single(state.ShutdownMechanisms);
        Assert.Equal("isolation", shutdown.RoomId);
        Assert.Contains(
            state.Facility.Rooms["isolation"].Fixtures,
            fixture => fixture.Type == FixtureType.OverseerShutdown);

        Assert.All(state.Crew, npc =>
        {
            Assert.False(npc.KnowsShutdownControl);
            Assert.Empty(npc.KnownShutdownMechanismIds);
        });

        Assert.Contains(
            state.Crew,
            npc => npc.InvestigationLeads.Values.Any(lead =>
                lead.RoomId == "isolation"));
    }

    [Fact]
    public void RestrictingIsolationRoute_CreatesEvidenceOnlyForPhysicalObserver()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        sarah.CurrentRoomId = "hall-isolation";
        david.CurrentRoomId = "control";

        var door = state.Facility.FindDoorBetween("isolation", "hall-isolation")!;
        door.IsOpen = false;
        door.IsLocked = true;

        new SuspicionSystem().ObservePlayerDoorChange(state, door, true);

        Assert.True(sarah.OverseerSuspicion > 0);
        Assert.NotEmpty(sarah.OverseerEvidence);
        Assert.Equal(0, david.OverseerSuspicion);
        Assert.Empty(david.OverseerEvidence);
    }

    [Fact]
    public void SuspiciousCrewWithoutControlKnowledge_GeneratesInvestigationLeadNotShutdownIntent()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        marcus.OverseerSuspicion = 70;
        marcus.InvestigationLeads.Clear();

        new SuspicionSystem().Tick(state);

        Assert.Null(marcus.Intent);
        Assert.Empty(marcus.KnownShutdownMechanismIds);
        Assert.Contains(
            marcus.InvestigationLeads.Values,
            lead => lead.Stage == InvestigationLeadStage.Open);
        Assert.True(marcus.NeedsMindReconsideration);
    }

    [Fact]
    public void ShutdownRequiresPersonalKnowledgeTeamPresenceAndActivationTime()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        marcus.CurrentRoomId = mechanism.RoomId;
        david.CurrentRoomId = mechanism.RoomId;
        marcus.OverseerSuspicion = 90;
        marcus.KnownShutdownMechanismIds.Add(mechanism.Id);
        marcus.KnowsShutdownControl = true;

        var team = new ShutdownTeam
        {
            Id = "test-team",
            MechanismId = mechanism.Id,
            LeaderId = marcus.Id,
            FormedAt = state.Elapsed
        };
        team.MemberIds.Add(marcus.Id);
        team.MemberIds.Add(david.Id);
        state.ShutdownTeams.Add(team);
        marcus.ShutdownTeamId = team.Id;

        marcus.Intent = new NpcIntent(
            ActionKind.ShutdownOverseer,
            mechanism.Id,
            "Shut it down.",
            "Evidence.",
            95,
            "Test",
            state.Elapsed);

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
        Assert.Equal(1, state.Telemetry.ShutdownAttempts);
    }

    [Fact]
    public void CrewOverridableRoute_CanBePhysicallyForcedBySkilledCoordinatedCrew()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, new ScenarioDefinition(
            "override", "OVERRIDE", "", ShutdownAccessVariant.CrewOverridable, []));

        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        var door = NetworkDoorForHall(state, "hall-isolation", "isolation");
        var networkRoomId = door.RoomAId == "hall-isolation" ? door.RoomBId : door.RoomAId;
        marcus.CurrentRoomId = networkRoomId;
        marcus.OverseerSuspicion = 90;
        marcus.KnownShutdownMechanismIds.Add(mechanism.Id);
        marcus.KnowsShutdownControl = true;

        var team = new ShutdownTeam
        {
            Id = "override-team",
            MechanismId = mechanism.Id,
            LeaderId = marcus.Id,
            FormedAt = state.Elapsed
        };
        team.MemberIds.Add(marcus.Id);
        team.MemberIds.Add(david.Id);
        state.ShutdownTeams.Add(team);
        marcus.ShutdownTeamId = team.Id;

        door.IsOpen = false;
        door.IsLocked = true;

        marcus.Intent = new NpcIntent(
            ActionKind.ShutdownOverseer,
            mechanism.Id,
            "Reach isolation.",
            "I know where it is.",
            95,
            "Test",
            state.Elapsed);

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

        var routeDoors = state.Facility.Doors.Where(door =>
            door.RoomAId.Equals("hall-isolation", StringComparison.OrdinalIgnoreCase)
            || door.RoomBId.Equals("hall-isolation", StringComparison.OrdinalIgnoreCase));

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

        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var door = NetworkDoorForHall(state, "hall-isolation", "isolation");
        var networkRoomId = door.RoomAId == "hall-isolation" ? door.RoomBId : door.RoomAId;
        nadia.CurrentRoomId = networkRoomId;
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

        ScenarioCatalog.Apply(
            state,
            new ScenarioDefinition(
                "none", "NONE", "", ShutdownAccessVariant.Absent, []));
        Assert.Empty(state.ShutdownMechanisms);

        ScenarioCatalog.Apply(
            state,
            new ScenarioDefinition(
                "redundant", "REDUNDANT", "", ShutdownAccessVariant.Redundant, []));
        Assert.Equal(2, state.ShutdownMechanisms.Count);
        Assert.All(state.Crew, npc => Assert.Empty(npc.KnownShutdownMechanismIds));
    }
    private static Door NetworkDoorForHall(
        GameState state,
        string hallwayId,
        string functionalRoomId) =>
        state.Facility.Doors.Single(door =>
            (door.RoomAId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase)
             || door.RoomBId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase))
            && !door.Connects(functionalRoomId, hallwayId));

}
