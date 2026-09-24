using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Crew close the open hatch toward a breach (BACKLOG → Open issues: one
/// fire-driven hull breach drained a whole station because no fallback mind
/// ever shut a hatch against it).
/// </summary>
public sealed class DecompressionContainmentTests
{
    [Fact]
    public void HatchTowardBreach_IsTheOpenHatchOnTheBreachSide()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var hallCrew = state.Crew[0];
        var deeperCrew = state.Crew[1];
        hallCrew.CurrentRoomId = vent.Hallway.Id;
        deeperCrew.CurrentRoomId = vent.NetworkRoom.Id;

        Assert.Same(
            vent.InnerDoor,
            DecompressionContainmentRules.FindHatchTowardBreach(state, hallCrew));
        Assert.Same(
            vent.NetworkDoor,
            DecompressionContainmentRules.FindHatchTowardBreach(state, deeperCrew));
    }

    [Fact]
    public void NoHatchTowardBreach_InTheBreachedRoomOrAwayFromAnyBreach()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.CurrentRoomId = "hall-airlock";

        Assert.Null(DecompressionContainmentRules.FindHatchTowardBreach(state, npc));

        VentAirlockIntoStation(state);
        npc.CurrentRoomId = "airlock";

        // Shutting a hatch cannot save someone inside the breached room.
        Assert.Null(DecompressionContainmentRules.FindHatchTowardBreach(state, npc));
    }

    [Fact]
    public void NobodyShutsAHatchOnSomeoneCrossingIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var npc = state.Crew[0];
        var crossing = state.Crew[1];
        npc.CurrentRoomId = vent.Hallway.Id;
        crossing.CurrentRoomId = "airlock";
        crossing.Movement = new NpcMovement(
            vent.InnerDoor.Id,
            "airlock",
            vent.Hallway.Id,
            0,
            0,
            0,
            0);

        Assert.Null(DecompressionContainmentRules.FindHatchTowardBreach(state, npc));
    }

    [Fact]
    public void TwoPeopleDoNotBothGoForTheSameHatch()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var first = state.Crew[0];
        var second = state.Crew[1];
        first.CurrentRoomId = vent.NetworkRoom.Id;
        second.CurrentRoomId = vent.NetworkRoom.Id;
        first.Intent = new NpcIntent(ActionKind.CloseDoor, vent.NetworkDoor.Id, "Shut it", "Breach", 100, "test", state.Elapsed);

        Assert.Same(vent.NetworkDoor, DecompressionContainmentRules.FindHatchTowardBreach(state, first));
        Assert.Null(DecompressionContainmentRules.FindHatchTowardBreach(state, second));
    }

    [Fact]
    public void BrowserMind_ShutsTheHatchTowardBreachBeforeTheRoomIsEvenDangerous()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var npc = state.Crew[0];
        npc.CurrentRoomId = vent.NetworkRoom.Id;
        Assert.False(CrewEnvironmentSafety.IsDangerous(vent.NetworkRoom));

        new BrowserMindSystem().Tick(state);

        Assert.Equal(ActionKind.CloseDoor, npc.Intent?.Action);
        Assert.Equal(vent.NetworkDoor.Id, npc.Intent?.TargetId);
    }

    [Fact]
    public void BrowserMind_SealsInsteadOfFleeingWhenTheRoomIsAlreadyDangerous()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var npc = state.Crew[0];
        npc.CurrentRoomId = vent.Hallway.Id;
        vent.Hallway.PressureKpa = 60;
        Assert.True(CrewEnvironmentSafety.IsDangerous(vent.Hallway));

        var mind = new BrowserMindSystem();
        mind.Tick(state);
        mind.Tick(state);

        Assert.Equal(ActionKind.CloseDoor, npc.Intent?.Action);
        Assert.Equal(vent.InnerDoor.Id, npc.Intent?.TargetId);
    }

    [Fact]
    public async Task RuleBasedMind_ChoosesTheSameHatchAsTheBrowserMind()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var npc = state.Crew[0];
        npc.CurrentRoomId = vent.NetworkRoom.Id;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(npc, state);

        Assert.Equal(ActionKind.CloseDoor, intent.Action);
        Assert.Equal(vent.NetworkDoor.Id, intent.TargetId);
        Assert.Equal(DecompressionContainmentRules.Goal(vent.NetworkDoor), intent.Goal);
    }

    [Fact]
    public void ClosingTheHatchTowardBreach_CanInterruptCommittedWork()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var npc = state.Crew[0];
        npc.CurrentRoomId = vent.NetworkRoom.Id;
        CrewTaskSystem.Start(state, npc, ActionKind.Work, vent.NetworkRoom.Id, "routine work", TimeSpan.FromMinutes(30));
        var close = new NpcIntent(ActionKind.CloseDoor, vent.NetworkDoor.Id, "Shut it", "Breach", 100, "test", state.Elapsed);

        Assert.True(CrewTaskSystem.CanInterruptForLifeThreat(state, npc, close));

        // Without a drain, closing an ordinary hatch is not a life threat.
        vent.Airlock.ExteriorHatchOpen = false;
        Assert.False(CrewTaskSystem.CanInterruptForLifeThreat(state, npc, close));
    }

    [Fact]
    public void OllamaPrompt_ShowsTheOpenHatchTowardBreachWithoutPrescribingIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        var npc = state.Crew[0];
        npc.CurrentRoomId = vent.NetworkRoom.Id;

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains($"DECOMPRESSION: this compartment is losing air to space through the open hatch {vent.NetworkDoor.Id}", prompt);
        Assert.Contains("What you do is up to you.", prompt);

        npc.CurrentRoomId = "airlock";
        Assert.DoesNotContain("DECOMPRESSION:", NpcPromptBuilder.Build(npc, state));
    }

    [Fact]
    public async Task BrowserMindCrew_ConfineABreachInAnAllOpenStation()
    {
        // The 2026-09-24 soak: with nearly every hatch open, one breach used
        // to drain all 29 rooms in minutes and kill the whole crew.
        var state = FacilitySeeder.CreateDefault();
        var vent = VentAirlockIntoStation(state);
        foreach (var door in state.Facility.Doors)
        {
            door.IsOpen = true;
        }

        var deeperCrew = state.Crew[0];
        deeperCrew.CurrentRoomId = vent.NetworkRoom.Id;
        foreach (var npc in state.Crew)
        {
            if (npc.CurrentRoomId is "airlock" or "hall-airlock")
                npc.CurrentRoomId = vent.NetworkRoom.Id;
            npc.Intent = null;
            npc.Movement = null;
            npc.ActiveTask = null;
        }

        var roomsBefore = EnvironmentSystem.FindVacuumDepths(state).Count;
        Assert.True(roomsBefore > 5);

        var session = new BrowserMindSession(state);
        await session.AdvanceMinutesAsync(3);

        // The crew beside the breach shut the hatch to the airlock hallway.
        Assert.False(vent.NetworkDoor.IsOpen);
        Assert.Equal(
            ["airlock", "hall-airlock"],
            EnvironmentSystem.FindVacuumDepths(state).Keys.Order());

        await session.AdvanceMinutesAsync(30);

        Assert.All(state.Crew, npc => Assert.True(npc.IsAlive, $"{npc.Name}: {npc.CauseOfDeath}"));
        Assert.True(vent.NetworkRoom.PressureKpa >= 90);
    }

    [Fact]
    public async Task SoakSeed_FireDrivenHullBreachNoLongerKillsTheWholeCrew()
    {
        // BACKLOG soak finding (2026-09-24): an unfought fire breaches the
        // hull at T+06:03 with nearly every hatch open. Before crew sealed
        // hatches, all 29 rooms drained and all 12 crew died by T+06:20.
        var crew = PrisonerRosterSystem.Compose(
            SeededCrewRosterGenerator.Generate(15838, 12),
            ScenarioCatalog.SecureContinuity);
        var state = FacilitySeeder.CreateDefault(
            crew,
            stationSeed: 202,
            stationConstraints: RosterCompositionRules.ConstraintsFor(
                ScenarioCatalog.SecureContinuity,
                crew.Count),
            robotCount: 1);
        ScenarioCatalog.Apply(state, ScenarioCatalog.SecureContinuity);
        var session = new BrowserMindSession(state);

        var breached = false;
        for (var minute = 0; minute < 8 * 60; minute++)
        {
            await session.AdvanceMinutesAsync(1);
            breached |= state.Facility.Rooms.Values.Any(room => room.HasHullBreach);
        }

        Assert.True(breached, "The soak seed no longer breaches; pick a new seed that does.");
        Assert.True(EnvironmentSystem.FindVacuumDepths(state).Count <= 2);
        Assert.All(state.Crew, npc => Assert.True(npc.IsAlive, $"{npc.Name}: {npc.CauseOfDeath}"));
    }

    private sealed record Vent(Room Airlock, Room Hallway, Room NetworkRoom, Door InnerDoor, Door NetworkDoor);

    private static Vent VentAirlockIntoStation(GameState state)
    {
        var airlock = state.Facility.Rooms["airlock"];
        var hallway = state.Facility.Rooms["hall-airlock"];
        var innerDoor = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;
        var networkDoor = state.Facility.Doors.Single(door =>
            (door.RoomAId == "hall-airlock" || door.RoomBId == "hall-airlock")
            && !door.Connects("airlock", "hall-airlock"));
        var networkRoomId = networkDoor.RoomAId == "hall-airlock"
            ? networkDoor.RoomBId
            : networkDoor.RoomAId;

        innerDoor.IsOpen = true;
        networkDoor.IsOpen = true;
        airlock.ExteriorHatchOpen = true;
        airlock.PressureKpa = 0;
        return new Vent(airlock, hallway, state.Facility.Rooms[networkRoomId], innerDoor, networkDoor);
    }

    private sealed class BrowserMindSession(GameState state)
        : StationSession(new RuleBasedOverseerMessageInterpreter(), state)
    {
        private readonly BrowserMindSystem _mind = new();

        public override Task ResetAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task RegenerateStationAsync(int? seed = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task RestoreCampaignAsync(CampaignState campaign, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task LoadScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task LoadStandaloneScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        protected override Task ThinkAsync(CancellationToken cancellationToken)
        {
            _mind.Tick(State);
            return Task.CompletedTask;
        }
    }
}
