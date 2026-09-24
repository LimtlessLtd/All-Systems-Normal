using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #103: a won scenario keeps simulating after "Scenario Complete",
/// but its recorded result (status, outcome text, objectives, score, compliance
/// and the campaign's mission record) is frozen on the turn it resolved.
/// </summary>
public sealed class ScenarioContinuationTests
{
    [Fact]
    public async Task WonScenario_KeepsTickingWithTheResultFrozen()
    {
        var session = WinOnNextTurn(out var generation);

        Assert.True(await session.TryAdvanceRunningAsync(generation));
        Assert.Equal(ScenarioStatus.Won, session.State.ScenarioStatus);

        var wonAt = session.State.Elapsed;
        var outcome = session.State.ScenarioOutcome;
        var score = session.State.Telemetry.Score;
        var compliance = session.State.ComplianceScore;
        var objectives = session.State.ObjectiveProgress.ToDictionary(
            entry => entry.Key,
            entry => (entry.Value.Current, entry.Value.IsComplete, entry.Value.IsFailed, entry.Value.StatusText));
        var hunger = session.State.Crew.Select(npc => npc.Hunger).ToList();

        for (var minute = 0; minute < 30; minute++)
        {
            Assert.True(await session.TryAdvanceRunningAsync(generation));
        }

        Assert.True(session.IsRunning);
        Assert.Equal(wonAt + TimeSpan.FromMinutes(30), session.State.Elapsed);
        Assert.NotEqual(hunger, session.State.Crew.Select(npc => npc.Hunger).ToList());

        Assert.Equal(ScenarioStatus.Won, session.State.ScenarioStatus);
        Assert.Equal(outcome, session.State.ScenarioOutcome);
        Assert.Equal(score, session.State.Telemetry.Score);
        Assert.Equal(compliance, session.State.ComplianceScore);
        Assert.Equal(
            objectives,
            session.State.ObjectiveProgress.ToDictionary(
                entry => entry.Key,
                entry => (entry.Value.Current, entry.Value.IsComplete, entry.Value.IsFailed, entry.Value.StatusText)));
        Assert.Single(
            session.State.EventLog,
            line => line.Contains("SCENARIO COMPLETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WonScenario_RecordsTheMissionOnceAtTheWinningTurn()
    {
        var session = WinOnNextTurn(out var generation);
        var crewAboard = session.State.Crew.Count(npc => npc.IsAlive && npc.IsPresent);

        Assert.True(await session.TryAdvanceRunningAsync(generation));
        var recorded = Assert.Single(session.Campaign.MissionHistory);
        Assert.Equal(ScenarioStatus.Won, recorded.Outcome);
        Assert.Equal(crewAboard, recorded.CrewSurviving);

        // Losing someone after the win is part of the continuing story, not a
        // rewrite of the assignment already on record.
        session.State.Crew[0].Health = 0;
        for (var minute = 0; minute < 5; minute++)
        {
            Assert.True(await session.TryAdvanceRunningAsync(generation));
        }

        session.CaptureCampaignProgress();

        Assert.Equal(recorded, Assert.Single(session.Campaign.MissionHistory));
    }

    [Fact]
    public async Task WonScenario_StillAcceptsOverseerControls()
    {
        var session = WinOnNextTurn(out var generation);
        Assert.True(await session.TryAdvanceRunningAsync(generation));

        var alarm = OverseerCommsSystem.SoundFireAlarm(session.State, "engineering");

        Assert.NotNull(alarm);
        Assert.True(await session.SendMessageAsync(
            OverseerMessageScope.Broadcast,
            null,
            "Good work, everyone."));
    }

    [Fact]
    public async Task FailedScenario_StillHaltsTheStation()
    {
        var session = WinOnNextTurn(out var generation);
        session.State.ScenarioStatus = ScenarioStatus.Failed;
        var elapsed = session.State.Elapsed;

        Assert.False(await session.TryAdvanceRunningAsync(generation));
        Assert.False(session.IsRunning);
        Assert.Equal(elapsed, session.State.Elapsed);
        Assert.Null(OverseerCommsSystem.SoundFireAlarm(session.State, "engineering"));
    }

    [Fact]
    public void OnlyAFailedRunHaltsTheSimulation()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 4242);

        state.ScenarioStatus = ScenarioStatus.Running;
        Assert.True(state.IsSimulationLive);
        state.ScenarioStatus = ScenarioStatus.Won;
        Assert.True(state.IsSimulationLive);
        state.ScenarioStatus = ScenarioStatus.Failed;
        Assert.False(state.IsSimulationLive);
    }

    /// <summary>
    /// A quiet station one minute short of the observation window, with no
    /// directives to grade, resolves on its station objectives next turn.
    /// </summary>
    private static ContinuationSession WinOnNextTurn(out long generation)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 4242);
        state.Directives.Clear();
        state.DirectiveProgress.Clear();
        state.Elapsed = ScenarioCatalog.ObservationWindow - TimeSpan.FromMinutes(1);

        var session = new ContinuationSession(state);
        (_, generation) = session.StartClock();
        return session;
    }

    private sealed class ContinuationSession(GameState state)
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
