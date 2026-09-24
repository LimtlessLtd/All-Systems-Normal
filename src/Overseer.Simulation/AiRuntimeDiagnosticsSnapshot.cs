namespace Overseer.Simulation;

/// <summary>
/// Process-level diagnostics for the optional external model runtime. This is
/// deliberately operational telemetry only: it never contains prompts, model
/// responses or player data, so a fresh /debug circuit can still report whether
/// the local Ollama provider is actually being reached without leaking another
/// station session's cognition.
/// </summary>
public sealed record AiRuntimeDiagnosticsSnapshot(
    string Mode,
    string? Endpoint,
    string? Model,
    long ProviderRequestsStarted,
    long ProviderResponsesReceived,
    long ProviderFailures,
    long NpcDecisionRequestsStarted,
    string? LastOperation,
    string? LastError,
    DateTimeOffset? LastAttemptUtc)
{
    public static AiRuntimeDiagnosticsSnapshot None { get; } =
        new(
            "Deterministic runtime",
            null,
            null,
            0,
            0,
            0,
            0,
            null,
            null,
            null);
}
