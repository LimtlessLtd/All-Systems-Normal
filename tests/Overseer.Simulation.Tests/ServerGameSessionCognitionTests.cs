using System.Net;
using System.Net.Sockets;
using System.Text;
using OllamaSharp;
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


    [Fact]
    public async Task RealServerSession_ReachesOllamaHttpBoundaryByMinuteFour()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestObserved = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var server = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                var buffer = new byte[16_384];
                var read = await stream.ReadAsync(buffer, timeout.Token);
                requestObserved.TrySetResult(Encoding.UTF8.GetString(buffer, 0, read));

                // The response may fail: this test is about proving the live
                // server reaches Ollama. The production fallback then keeps the
                // station moving exactly as it would for a provider error.
                var response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 500 Internal Server Error\r\n" +
                    "Content-Length: 0\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(response, timeout.Token);
            }
            finally
            {
                listener.Stop();
            }
        }, timeout.Token);

        var endpoint = new Uri($"http://127.0.0.1:{port}/");
        var diagnostics = new OllamaRuntimeDiagnostics(endpoint, "qwen3:4b");
        var mind = new OllamaAiDecisionService(
            new OllamaApiClient(endpoint, "qwen3:4b"),
            new RuleBasedAiDecisionService(),
            diagnostics);
        var session = new GameSession(
            mind,
            new RuleBasedCrewGenerator(),
            new RuleBasedOverseerMessageInterpreter(),
            diagnostics);

        await session.InitializeAsync(timeout.Token);
        await session.AdvanceMinutesAsync(4, timeout.Token);

        var rawRequest = await requestObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            timeout.Token);
        await server;

        Assert.Contains("POST ", rawRequest);
        Assert.Contains("/api/chat", rawRequest, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.AiRuntimeDiagnostics.NpcDecisionRequestsStarted >= 1);
        Assert.True(session.AiRuntimeDiagnostics.ProviderFailures >= 1);
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
