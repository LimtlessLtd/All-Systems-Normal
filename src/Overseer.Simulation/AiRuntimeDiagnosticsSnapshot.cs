namespace Overseer.Simulation;

/// <summary>
/// One provider request retained for developer diagnostics. Payload fields are
/// populated only by the local Development server; production snapshots keep
/// this collection empty.
/// </summary>
public sealed record AiProviderDebugTrace(
    long Sequence,
    DateTimeOffset StartedAtUtc,
    string Operation,
    bool IsNpcDecision,
    string? Prompt,
    string? RawResponse,
    string? Error);

/// <summary>
/// Process-level diagnostics for the optional external model runtime. Counters
/// and redacted errors contain no prompts/model responses. RecentProviderTraces
/// contains payloads only when the server explicitly enables Development-only
/// capture, allowing /debug to survive a Blazor circuit/page reload without
/// exposing one player's prompts to another in production.
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
    DateTimeOffset? LastAttemptUtc,
    IReadOnlyList<AiProviderDebugTrace> RecentProviderTraces)
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
            null,
            []);
}
