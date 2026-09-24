using System.Net;
using System.Net.Sockets;
using System.Text;
using OllamaSharp;
using Overseer.AI;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class OllamaProviderBoundaryTests
{
    [Fact]
    public async Task StructuredDecision_ReachesTheConfiguredOllamaHttpEndpoint()
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
                var request = Encoding.UTF8.GetString(buffer, 0, read);
                requestObserved.TrySetResult(request);

                // A failure response is enough for this boundary regression:
                // OllamaAiDecisionService must still have crossed the real
                // OllamaSharp HTTP boundary before falling back.
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

        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var npc = state.Crew[0];
        var client = new OllamaApiClient(
            new Uri($"http://127.0.0.1:{port}/"),
            "qwen3:4b");
        var diagnostics = new OllamaRuntimeDiagnostics(
            new Uri($"http://127.0.0.1:{port}/"),
            "qwen3:4b");
        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService(),
            diagnostics);

        _ = await service.DecideAsync(npc, state, timeout.Token);

        var rawRequest = await requestObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            timeout.Token);
        await server;

        Assert.Contains("POST ", rawRequest);
        Assert.Contains("/api/chat", rawRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            state.CognitionTelemetry,
            trace => trace.Source.Equals("Ollama", StringComparison.OrdinalIgnoreCase));

        var runtime = diagnostics.Snapshot();
        Assert.Equal(1, runtime.NpcDecisionRequestsStarted);
        Assert.Equal(1, runtime.ProviderRequestsStarted);
        Assert.Equal(0, runtime.ProviderResponsesReceived);
        Assert.Equal(1, runtime.ProviderFailures);
        Assert.Equal("NPC decision", runtime.LastOperation);
        Assert.False(string.IsNullOrWhiteSpace(runtime.LastError));
    }

    [Fact]
    public async Task StructuredDecision_AsksOllamaForEnoughOutputToFinishTheJson()
    {
        // Owner report (2026-09-24): with a 300-token cap, qwen3 decisions
        // were cut short. The cap must reach the wire as Ollama's num_predict.
        var handler = new CapturingHandler();
        var client = new OllamaApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") },
            "qwen3:4b");
        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);

        _ = await service.DecideAsync(state.Crew[0], state);

        Assert.Equal(750, OllamaAiDecisionService.DecisionMaxOutputTokens);
        Assert.NotEmpty(handler.Bodies);
        Assert.All(handler.Bodies, body =>
        {
            var compact = body.Replace(" ", string.Empty);
            Assert.Contains("\"num_predict\":750", compact);
            Assert.Contains(
                $"\"num_ctx\":{OllamaAiDecisionService.ContextWindowTokens}",
                compact);
        });
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}
