using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;
using Overseer.Web.Services;

namespace Overseer.Simulation.Tests;

public sealed class ServerGameSessionCognitionTests
{
    [Fact]
    public async Task OrdinaryServerCadence_CallsInjectedMindByMinuteFour()
    {
        var mind = new CountingDecisionService();
        var session = new GameSession(
            mind,
            new RuleBasedCrewGenerator(),
            new RuleBasedOverseerMessageInterpreter());

        await session.InitializeAsync();
        await session.AdvanceMinutesAsync(4);

        Assert.True(
            mind.Calls > 0,
            $"Expected server cognition by T+00:04, but the injected mind was called {mind.Calls} times.");
    }

    [Fact]
    public async Task OrdinaryServerCadence_ContinuesCallingMindAcrossFirstHour()
    {
        var mind = new CountingDecisionService();
        var session = new GameSession(
            mind,
            new RuleBasedCrewGenerator(),
            new RuleBasedOverseerMessageInterpreter());

        await session.InitializeAsync();
        await session.AdvanceMinutesAsync(60);

        Assert.True(
            mind.Calls >= 6,
            $"Expected repeated server cognition through T+01:00, but the injected mind was called only {mind.Calls} times.");
    }

    private sealed class CountingDecisionService : IAiDecisionService
    {
        public int Calls { get; private set; }

        public Task<NpcIntent> DecideAsync(
            Npc npc,
            GameState state,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new NpcIntent(
                ActionKind.Idle,
                null,
                "Observe the station.",
                "I should reconsider what matters.",
                20,
                "Counting test mind",
                state.Elapsed));
        }
    }
}
