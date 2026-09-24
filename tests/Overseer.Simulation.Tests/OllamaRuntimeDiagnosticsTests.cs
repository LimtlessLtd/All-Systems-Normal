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

        diagnostics.RecordStarted("NPC decision", npcDecision: true);
        diagnostics.RecordFailure(
            "NPC decision",
            new InvalidOperationException("connection refused"));

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
    }

    [Fact]
    public void SuccessfulResponse_ClearsPreviousProviderError()
    {
        var diagnostics = new OllamaRuntimeDiagnostics(
            new Uri("http://localhost:11434"),
            "qwen3:4b");

        diagnostics.RecordStarted("startup health probe");
        diagnostics.RecordFailure("startup health probe", "not reachable");
        diagnostics.RecordStarted("crew generation");
        diagnostics.RecordResponse("crew generation");

        var snapshot = diagnostics.Snapshot();

        Assert.Equal(2, snapshot.ProviderRequestsStarted);
        Assert.Equal(1, snapshot.ProviderResponsesReceived);
        Assert.Equal(1, snapshot.ProviderFailures);
        Assert.Equal("crew generation", snapshot.LastOperation);
        Assert.Null(snapshot.LastError);
    }
}
