using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// RestoreSystem only targets outages the local controls can actually undo.
/// A soak seed lost 8 of 11 crew to suffocation: a worn reactor made the grid
/// shed Engineering, and the crew spent seven hours "restoring" life support.
/// The next upkeep tick switched it straight back off, and their urgency-90
/// intents kept maintenance from ever servicing the reactor.
/// </summary>
public sealed class RestorableOutageTests
{
    [Fact]
    public void LifeSupportDownBecauseEngineeringWasShed_IsNotARestoreJob()
    {
        var state = FacilitySeeder.CreateDefault();
        ShedEngineering(state);

        Assert.False(CrewCounterplaySystem.HasSwitchedOffLifeSupport(state));
        Assert.False(CrewCounterplaySystem.HasRestorableProblem(state, CrewCounterplaySystem.LifeSupportTarget));
        Assert.False(CrewCounterplaySystem.HasRestorableProblem(state, "engineering"));
    }

    [Fact]
    public void LifeSupportSwitchedOff_IsRestoredByTheManualControls()
    {
        var state = FacilitySeeder.CreateDefault();
        state.LifeSupport.RequestedOnline = false;
        state.LifeSupport.IsOnline = false;

        Assert.True(CrewCounterplaySystem.HasRestorableProblem(state, CrewCounterplaySystem.LifeSupportTarget));
        Assert.True(CrewCounterplaySystem.TryRestoreOneProblem(state, CrewCounterplaySystem.LifeSupportTarget));

        Assert.True(state.LifeSupport.RequestedOnline);
        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));
        Assert.True(state.LifeSupport.IsOnline);
    }

    [Fact]
    public void DisabledOxygenGenerator_IsRestorable_ButAFailedOneNeedsServicing()
    {
        var state = FacilitySeeder.CreateDefault();
        var oxygen = state.Devices.Values.Single(device => device.Kind == StationSystemKind.OxygenGenerator);
        oxygen.IsEnabled = false;
        state.LifeSupport.IsOnline = false;

        Assert.True(CrewCounterplaySystem.HasSwitchedOffLifeSupport(state));
        CrewCounterplaySystem.TryRestoreOneProblem(state, CrewCounterplaySystem.LifeSupportTarget);
        Assert.True(oxygen.IsEnabled);

        oxygen.IsEnabled = true;
        oxygen.Condition = 0;
        state.LifeSupport.IsOnline = false;
        Assert.False(CrewCounterplaySystem.HasSwitchedOffLifeSupport(state));
    }

    [Fact]
    public void OverseerCutRoomPower_IsStillRestorable_ButAGridShedRoomIsNot()
    {
        var state = FacilitySeeder.CreateDefault();
        var galley = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Kitchen);
        galley.IsPowered = false;

        Assert.True(CrewCounterplaySystem.HasRestorableProblem(state, galley.Id));

        state.Power.SheddedRoomIds.Add(galley.Id);
        Assert.False(CrewCounterplaySystem.HasRestorableProblem(state, galley.Id));
    }

    [Fact]
    public async Task FallbackMind_DoesNotChaseAShedLifeSupportOutage()
    {
        var state = FacilitySeeder.CreateDefault();
        ShedEngineering(state);
        var engineer = state.Crew.First(npc => CrewCounterplaySystem.BestRepairSkill(npc) >= 55);
        engineer.CurrentRoomId = "corridor";

        var intent = await new RuleBasedAiDecisionService().DecideAsync(engineer, state);

        Assert.NotEqual(ActionKind.RestoreSystem, intent.Action);
    }

    [Fact]
    public void OllamaPrompt_ExplainsTheGridShedInsteadOfListingItAsRestorable()
    {
        var state = FacilitySeeder.CreateDefault();
        ShedEngineering(state);
        var npc = state.Crew[0];

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains("STATION GRID: generation cannot carry the load, so the grid has cut power to engineering.", prompt);
        var disabled = prompt[(prompt.IndexOf("DISABLED SYSTEM TARGET IDS:", StringComparison.Ordinal) + 28)..];
        disabled = disabled[..disabled.IndexOf('\n')];
        Assert.DoesNotContain("life-support", disabled);
        Assert.DoesNotContain("engineering", disabled);
    }

    [Fact]
    public async Task SoakSeed_WornReactorNoLongerSuffocatesTheCrew()
    {
        // Seed 10 of a 60-seed browser-mind soak (2026-09-24): the station
        // starts with its reactor at 22% condition. Before the fix, 8 of 11
        // crew died of oxygen deprivation by T+08:00.
        const int rosterSeed = 10 * 7919;
        var composition = RosterCompositionRules.Sample(rosterSeed);
        var crew = PrisonerRosterSystem.Compose(
            SeededCrewRosterGenerator.Generate(rosterSeed, composition.CrewCount),
            ScenarioCatalog.SecureContinuity);
        var state = FacilitySeeder.CreateDefault(
            crew,
            stationSeed: 10 * 104729,
            stationConstraints: RosterCompositionRules.ConstraintsFor(
                ScenarioCatalog.SecureContinuity,
                crew.Count),
            robotCount: composition.RobotCount);
        ScenarioCatalog.Apply(state, ScenarioCatalog.SecureContinuity);
        var reactor = state.Devices.Values.Single(device => device.Kind == StationSystemKind.Reactor);
        Assert.True(reactor.Condition < reactor.DegradedAt, "The soak seed no longer starts with a worn reactor; pick a new seed that does.");

        await new BrowserMindSession(state).AdvanceMinutesAsync(8 * 60);

        Assert.All(state.Crew, npc => Assert.True(npc.IsAlive, $"{npc.Name}: {npc.CauseOfDeath}"));
        Assert.True(reactor.Condition >= reactor.DegradedAt);
    }

    private static void ShedEngineering(GameState state)
    {
        state.Facility.Rooms["engineering"].IsPowered = false;
        state.Facility.Rooms["engineering"].LightsOn = false;
        state.Facility.Rooms["engineering"].CameraOnline = false;
        state.Power.SheddedRoomIds.Add("engineering");
        state.LifeSupport.OxygenGeneratorOnline = false;
        state.LifeSupport.IsOnline = false;
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
