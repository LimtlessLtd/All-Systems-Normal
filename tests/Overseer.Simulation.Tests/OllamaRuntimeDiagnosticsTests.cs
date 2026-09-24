using Overseer.AI;

namespace Overseer.Simulation.Tests;

public sealed class OllamaRuntimeDiagnosticsTests
{
    [Fact]
    public void Snapshot_IsPayloadFreeAndSanitizesEndpointCredentials()
    {
        var diagnostics = new OllamaRuntimeDiagnostics(
            new Uri("http://user:super-secret@localhost:11434/private?token=also-secret"),
            "qwen3:4b");

        var sequence = diagnostics.RecordStarted("NPC decision", npcDecision: true);
        diagnostics.RecordFailure(
            sequence,
            "NPC decision",
            new InvalidOperationException(
                "connection refused at http://user:super-secret@localhost:11434/private?token=also-secret"));

        var snapshot = diagnostics.Snapshot();

        Assert.Equal("Ollama server", snapshot.Mode);
        Assert.Equal("http://localhost:11434", snapshot.Endpoint);
        Assert.Equal("qwen3:4b", snapshot.Model);
        Assert.Equal(1, snapshot.ProviderRequestsStarted);
        Assert.Equal(1, snapshot.NpcDecisionRequestsStarted);
        Assert.Equal(1, snapshot.ProviderFailures);
        Assert.Contains("InvalidOperationException", snapshot.LastError);
        Assert.DoesNotContain("super-secret", snapshot.Endpoint);
        Assert.DoesNotContain("also-secret", snapshot.Endpoint);
        Assert.DoesNotContain("super-secret", snapshot.LastError);
        Assert.DoesNotContain("also-secret", snapshot.LastError);
    }

    [Fact]
    public void SuccessfulResponse_ClearsPreviousProviderError()
    {
        var diagnostics = new OllamaRuntimeDiagnostics(
            new Uri("http://localhost:11434"),
            "qwen3:4b");

        var probe = diagnostics.RecordStarted("startup health probe");
        diagnostics.RecordFailure(probe, "startup health probe", "not reachable");
        var generation = diagnostics.RecordStarted("crew generation");
        diagnostics.RecordResponse(generation, "crew generation");

        var snapshot = diagnostics.Snapshot();

        Assert.Equal(2, snapshot.ProviderRequestsStarted);
        Assert.Equal(1, snapshot.ProviderResponsesReceived);
        Assert.Equal(1, snapshot.ProviderFailures);
        Assert.Equal("crew generation", snapshot.LastOperation);
        Assert.Null(snapshot.LastError);
    }
    [Fact]
    public void DevelopmentCapture_RetainsBoundedNpcRequestAndResponse()
    {
        var diagnostics = new OllamaRuntimeDiagnostics(
            new Uri("http://localhost:11434"),
            "qwen3:4b",
            capturePayloads: true);

        var sequence = diagnostics.RecordStarted(
            "NPC decision",
            npcDecision: true,
            prompt: "EXACT PROMPT");
        diagnostics.RecordResponse(
            sequence,
            "NPC decision",
            rawResponse: "RAW RESPONSE");

        var trace = Assert.Single(diagnostics.Snapshot().RecentProviderTraces);
        Assert.True(trace.IsNpcDecision);
        Assert.Equal("EXACT PROMPT", trace.Prompt);
        Assert.Equal("RAW RESPONSE", trace.RawResponse);
        Assert.Null(trace.Error);
    }

    [Fact]
    public void ProductionStyleCapture_NeverRetainsProviderPayloads()
    {
        var diagnostics = new OllamaRuntimeDiagnostics(
            new Uri("http://localhost:11434"),
            "qwen3:4b",
            capturePayloads: false);

        var sequence = diagnostics.RecordStarted(
            "NPC decision",
            npcDecision: true,
            prompt: "MUST NOT SURVIVE");
        diagnostics.RecordResponse(
            sequence,
            "NPC decision",
            rawResponse: "MUST NOT SURVIVE EITHER");

        Assert.Empty(diagnostics.Snapshot().RecentProviderTraces);
    }

}
