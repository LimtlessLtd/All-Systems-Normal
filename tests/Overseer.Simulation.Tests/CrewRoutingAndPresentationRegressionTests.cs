using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Regressions from the post-V0.10E review: route planning through ordinary
/// closed hatches, fallback-mind priorities and player-facing labels.
/// </summary>
public sealed class CrewRoutingAndPresentationRegressionTests
{
    [Fact]
    public void CrewRoutePlanning_PassesClosedUnlockedHatchesBeyondTheCurrentRoom()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var npc = state.Crew.First();
        npc.CurrentRoomId = "control";
        CloseEveryHatch(state);

        var navigation = new NavigationSystem();
        var topology = navigation.FindPathIgnoringDoorState(state.Facility, "control", "reactor");
        var crewPath = navigation.FindPathForCrew(state, npc, "control", "reactor");

        Assert.True(topology.Count > 3, "The regression needs a route through several closed hatches.");
        Assert.Equal(topology, crewPath);
    }

    [Fact]
    public void CrewRoutePlanning_StillStopsAtLockedOrWeldedHatches()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var npc = state.Crew.First();
        npc.CurrentRoomId = "control";
        CloseEveryHatch(state);

        var reactorDoor = state.Facility.FindDoorBetween("reactor", "hall-reactor")!;
        var navigation = new NavigationSystem();

        reactorDoor.IsLocked = true;
        Assert.Empty(navigation.FindPathForCrew(state, npc, "control", "reactor"));

        reactorDoor.IsLocked = false;
        reactorDoor.IsWelded = true;
        Assert.Empty(navigation.FindPathForCrew(state, npc, "control", "reactor"));
    }

    [Fact]
    public void CrewRoutePlanning_StillRespectsTheAirlockInnerHatchInterlock()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 101);
        var npc = state.Crew.First();
        npc.CurrentRoomId = "corridor";
        CloseEveryHatch(state);

        var navigation = new NavigationSystem();
        Assert.NotEmpty(navigation.FindPathForCrew(state, npc, "corridor", "airlock"));

        state.Facility.Rooms["airlock"].ExteriorHatchOpen = true;

        Assert.Empty(navigation.FindPathForCrew(state, npc, "corridor", "airlock"));
    }

    [Fact]
    public void RoomIntent_WalksTowardFoodThroughClosedOrdinaryHatches()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var npc = state.Crew.First();
        npc.CurrentRoomId = "control";
        CloseEveryHatch(state);
        npc.Intent = new NpcIntent(
            ActionKind.Eat,
            null,
            "Find something to eat.",
            "Hungry.",
            70,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.Move, npc.CurrentAction.Kind);
        Assert.NotNull(npc.Movement);
        Assert.DoesNotContain("sealed", npc.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UndisturbedShift_NeverReportsAnOrdinaryRouteAsSealed()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var shift = new BrowserShift();
        var sealedTicks = 0;

        for (var minute = 0; minute < 240 && state.ScenarioStatus == ScenarioStatus.Running; minute++)
        {
            shift.Advance(state);
            sealedTicks += state.Crew.Count(npc =>
                npc.CurrentAction.Reason.Contains("sealed", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(0, sealedTicks);
    }

    [Fact]
    public void Routine_LeavesCrewAloneWhileTheyWorkAJobOnSite()
    {
        // Once routine travel stopped failing at closed hatches, the routine
        // began pulling people off repairs and chores it knew nothing about.
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var device = state.Devices["lighting:medical"];
        var technician = state.Crew[0];
        var grower = state.Crew[1];

        technician.CurrentRoomId = device.RoomId;
        technician.ServicingDeviceId = device.Id;
        technician.CurrentAction = new NpcAction(ActionKind.Repair, device.RoomId, "Servicing.");

        grower.CurrentRoomId = "hydroponics";
        grower.ProvisioningJob = ActionKind.TendCrops;
        grower.ProvisioningRoomId = "hydroponics";
        grower.CurrentAction = new NpcAction(ActionKind.TendCrops, "hydroponics", "Tending.");

        var technicianAction = technician.CurrentAction;
        var growerAction = grower.CurrentAction;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new CrewRoutineSystem().Tick(state);

        Assert.Null(technician.Movement);
        Assert.Same(technicianAction, technician.CurrentAction);
        Assert.Null(grower.Movement);
        Assert.Same(growerAction, grower.CurrentAction);
    }

    [Fact]
    public async Task RuleBasedMind_EatsBeforeChasingAnInvestigationLead()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var npc = state.Crew.First();
        npc.Hunger = 80;
        npc.OverseerSuspicion = 50;
        npc.InvestigationLeads["lead-regression"] = new InvestigationLead
        {
            Id = "lead-regression",
            Description = "Something odd happened in Engineering.",
            RoomId = "engineering",
            CreatedAt = TimeSpan.Zero
        };

        var intent = await new RuleBasedAiDecisionService().DecideAsync(npc, state);

        Assert.Equal(ActionKind.Eat, intent.Action);
    }

    [Fact]
    public async Task RuleBasedMind_WalksOutOfDangerInsteadOfForcingAnOrdinaryHatch()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var engineer = state.Crew.Single(npc => npc.Role == CrewRole.Engineer);
        engineer.CurrentRoomId = "engineering";
        CloseEveryHatch(state);
        state.Facility.Rooms["engineering"].OxygenPercent = 15;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(engineer, state);

        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.NotEqual("engineering", intent.TargetId);
    }

    [Fact]
    public void BrowserMind_ChecksOnTheMostStressedColleague()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var carer = state.Crew
            .Where(npc => npc.Personality.Empathy >= 65)
            .OrderBy(npc => npc.Name)
            .First();
        var colleagues = state.Crew
            .Where(npc => npc.Id != carer.Id)
            .OrderBy(npc => npc.Name)
            .ToList();

        // The alphabetically first colleague is calm; the last one is struggling.
        foreach (var colleague in colleagues)
        {
            colleague.Stress = 60;
        }

        colleagues[0].Stress = 5;
        colleagues[^1].Stress = 90;
        carer.NeedsMindReconsideration = true;
        state.Elapsed = TimeSpan.FromMinutes(1);

        new BrowserMindSystem().Tick(state);

        Assert.NotNull(carer.Intent);
        Assert.Equal(ActionKind.CheckOnCrew, carer.Intent!.Action);
        Assert.Equal(colleagues[^1].Name, carer.Intent.TargetId);
    }

    [Fact]
    public void GeneratedFixtures_HavePlayerFacingDisplayNames()
    {
        var generated = new RoomFixture(FixtureType.UtilityPanel, "Generated UtilityPanel 3", 10, 10, 5, 5);
        var authored = new RoomFixture(FixtureType.Console, "Command Display", 10, 10, 5, 5);

        Assert.Equal("Utility panel 3", StationPresentationSystem.FixtureDisplayName(generated));
        Assert.Equal("Command Display", StationPresentationSystem.FixtureDisplayName(authored));
    }

    [Fact]
    public void DoorActuators_AreNamedAfterTheRoomsTheyJoin()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var actuators = state.Devices.Values
            .Where(device => device.Kind == StationSystemKind.Door)
            .ToList();

        Assert.NotEmpty(actuators);
        Assert.All(actuators, device =>
        {
            var door = state.Facility.Doors.Single(candidate => candidate.Id == device.DoorId);
            Assert.DoesNotContain(door.Id, device.Label, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(state.Facility.Rooms[door.RoomAId].Name, device.Label, StringComparison.Ordinal);
            Assert.Contains(state.Facility.Rooms[door.RoomBId].Name, device.Label, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SecureContinuityBriefing_MatchesItsSurvivalObjective()
    {
        var scenario = ScenarioCatalog.SecureContinuity;
        var survival = scenario.Objectives.Single(objective =>
            objective.Kind == ScenarioObjectiveKind.SurviveMinutes);

        Assert.Contains($"{survival.Target:0} simulated minutes", scenario.Briefing, StringComparison.Ordinal);
    }

    private static void CloseEveryHatch(GameState state)
    {
        foreach (var door in state.Facility.Doors)
        {
            door.IsOpen = false;
            door.IsLocked = false;
            door.IsPowered = true;
        }
    }

    /// <summary>The browser runtime's turn order, without the UI session.</summary>
    private sealed class BrowserShift
    {
        private readonly StationUpkeepSystem _upkeep = new();
        private readonly EnvironmentSystem _environment = new();
        private readonly AirlockSafetySystem _airlockSafety = new();
        private readonly VacuumConsequenceSystem _vacuum = new();
        private readonly SimulationEngine _simulation = new();
        private readonly MissingPersonSystem _missingPeople = new();
        private readonly BrowserMindSystem _browserMind = new();
        private readonly IntentExecutionSystem _intentExecution = new();
        private readonly InvestigationSystem _investigations = new();
        private readonly CrewCounterplaySystem _counterplay = new();
        private readonly RobotCountermeasureSystem _robotCountermeasures = new();
        private readonly TurretCountermeasureSystem _turretCountermeasures = new();
        private readonly ManualOverrideSystem _manualOverrides = new();
        private readonly ShutdownCoordinationSystem _shutdownCoordination = new();
        private readonly SocialSimulationSystem _social = new();
        private readonly SuspicionSystem _suspicion = new();
        private readonly ConversationPacingSystem _conversationPacing = new();
        private readonly CrewRoutineSystem _crewRoutines = new();
        private readonly RobotSystem _robots = new();
        private readonly TurretSystem _turrets = new();
        private readonly LocalMovementSystem _movement = new();
        private readonly CrewDoorInteractionSystem _crewDoors = new();
        private readonly ShutdownSystem _shutdown = new();
        private readonly CrewProvisioningSystem _provisioning = new();
        private readonly CrewMaintenanceSystem _maintenance = new();

        public void Advance(GameState state)
        {
            var turn = TimeSpan.FromMinutes(1);
            _upkeep.Tick(state, turn);
            _environment.Tick(state, turn);
            _airlockSafety.Tick(state, turn);
            _vacuum.Tick(state);
            _simulation.Tick(state, turn);
            _missingPeople.Tick(state);
            _browserMind.Tick(state);
            _intentExecution.Tick(state);
            _investigations.Tick(state);
            _counterplay.Tick(state);
            _robotCountermeasures.Tick(state);
            _turretCountermeasures.Tick(state);
            _manualOverrides.Tick(state);
            _shutdownCoordination.Tick(state);
            _social.Tick(state);
            _suspicion.Tick(state);
            _conversationPacing.Tick(state);
            _crewRoutines.Tick(state);
            _robots.Tick(state, turn);
            _turrets.Tick(state, turn);
            _movement.Tick(state, turn);
            _crewDoors.Tick(state);
            _shutdown.Tick(state);
            _provisioning.Tick(state, turn);
            _maintenance.Tick(state);
        }
    }
}
