using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;
using Overseer.Web.Services;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #102: while an awaited model call holds up the turn, the session
/// says what it is waiting on, and clears it however the call ends. The
/// deterministic Pages runtime never claims LLM activity.
/// </summary>
public sealed class ProcessingStatusTests
{
    [Fact]
    public async Task ServerMindDecision_ShowsAwaitingLlmUntilTheModelAnswers()
    {
        var mind = new GatedDecisionService();
        var session = new GameSession(
            mind,
            new RuleBasedCrewGenerator(),
            new RuleBasedOverseerMessageInterpreter());
        await session.InitializeAsync();
        var changes = new List<string?>();
        session.ProcessingStatusChanged += () => changes.Add(session.ProcessingStatus);

        var advancing = session.AdvanceMinutesAsync(4);
        var deciding = await mind.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(
            $"AWAITING LLM RESPONSE — {deciding.Name.ToUpperInvariant()} IS DECIDING",
            session.ProcessingStatus);

        mind.Release();
        await advancing.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(session.ProcessingStatus);
        Assert.Contains(changes, change => change?.StartsWith("AWAITING LLM RESPONSE", StringComparison.Ordinal) == true);
        Assert.Null(changes[^1]);
    }

    [Fact]
    public async Task ServerMindDecision_ClearsTheStatusWhenTheCallFails()
    {
        var session = new GameSession(
            new ThrowingDecisionService(),
            new RuleBasedCrewGenerator(),
            new RuleBasedOverseerMessageInterpreter());
        await session.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.AdvanceMinutesAsync(4));

        Assert.Null(session.ProcessingStatus);
    }

    [Fact]
    public async Task ServerMessageInterpretation_ShowsAwaitingLlmWhileReading()
    {
        var interpreter = new GatedInterpreter();
        var session = new GameSession(
            new RuleBasedAiDecisionService(),
            new RuleBasedCrewGenerator(),
            interpreter);
        await session.InitializeAsync();

        var sending = session.SendMessageAsync(
            OverseerMessageScope.Broadcast,
            null,
            "Status report, please.");
        await interpreter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(
            "AWAITING LLM RESPONSE — READING OVERSEER MESSAGE",
            session.ProcessingStatus);

        interpreter.Release();
        Assert.True(await sending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(session.ProcessingStatus);
    }

    [Fact]
    public async Task BrowserMindRuntime_NeverClaimsLlmActivity()
    {
        var session = new BrowserMindSession(FacilitySeeder.CreateDefault(stationSeed: 4242));
        var changes = 0;
        session.ProcessingStatusChanged += () => changes++;

        await session.AdvanceMinutesAsync(10);
        await session.SendMessageAsync(
            OverseerMessageScope.Broadcast,
            null,
            "Status report, please.");

        Assert.Equal(0, changes);
        Assert.Null(session.ProcessingStatus);
    }

    private sealed class GatedDecisionService : IAiDecisionService
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<Npc> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public async Task<NpcIntent> DecideAsync(
            Npc npc,
            GameState state,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(npc);
            await _gate.Task;
            return new NpcIntent(
                ActionKind.Idle,
                null,
                "Observe the station.",
                "Gated test mind.",
                20,
                "Gated test mind",
                state.Elapsed);
        }
    }

    private sealed class ThrowingDecisionService : IAiDecisionService
    {
        public Task<NpcIntent> DecideAsync(
            Npc npc,
            GameState state,
            CancellationToken cancellationToken = default) =>
            Task.FromException<NpcIntent>(new InvalidOperationException("provider exploded"));
    }

    private sealed class GatedInterpreter : IOverseerMessageInterpreter
    {
        private readonly RuleBasedOverseerMessageInterpreter _inner = new();
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public async Task<OverseerMessageIntent> InterpretAsync(
            string text,
            OverseerMessageScope scope,
            string? targetNpcName,
            GameState state,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await _gate.Task;
            return await _inner.InterpretAsync(text, scope, targetNpcName, state, cancellationToken);
        }
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
