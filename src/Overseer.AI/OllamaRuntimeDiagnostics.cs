using Overseer.Simulation;

namespace Overseer.AI;

/// <summary>
/// Bounded, payload-free operational telemetry for the local Ollama provider.
/// It is safe to keep process-wide: only endpoint host/port, model name, counts
/// and the latest exception summary are retained. Exact prompts/responses remain
/// in each GameState's cognition telemetry.
/// </summary>
public sealed class OllamaRuntimeDiagnostics
{
    private readonly object _gate = new();
    private readonly string[] _sensitiveEndpointFragments;
    private long _requestsStarted;
    private long _responsesReceived;
    private long _failures;
    private long _npcDecisionRequestsStarted;
    private string? _lastOperation;
    private string? _lastError;
    private DateTimeOffset? _lastAttemptUtc;

    public OllamaRuntimeDiagnostics(Uri endpoint, string model)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint.IsDefaultPort
            ? $"{endpoint.Scheme}://{endpoint.Host}"
            : $"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}";
        Model = model;
        _sensitiveEndpointFragments =
        [
            endpoint.ToString(),
            endpoint.UserInfo,
            endpoint.Query.TrimStart('?'),
            endpoint.AbsolutePath.Trim('/')
        ];
    }

    public string Endpoint { get; }
    public string Model { get; }

    public void RecordStarted(string operation, bool npcDecision = false)
    {
        lock (_gate)
        {
            _requestsStarted++;
            if (npcDecision)
                _npcDecisionRequestsStarted++;

            _lastOperation = operation;
            _lastError = null;
            _lastAttemptUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RecordResponse(string operation)
    {
        lock (_gate)
        {
            _responsesReceived++;
            _lastOperation = operation;
            _lastError = null;
        }
    }

    public void RecordFailure(string operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        RecordFailure(operation, $"{exception.GetType().Name}: {exception.Message}");
    }

    public void RecordFailure(string operation, string error)
    {
        lock (_gate)
        {
            _failures++;
            _lastOperation = operation;
            _lastError = Limit(SanitizeError(error), 600);
        }
    }

    public AiRuntimeDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new AiRuntimeDiagnosticsSnapshot(
                "Ollama server",
                Endpoint,
                Model,
                _requestsStarted,
                _responsesReceived,
                _failures,
                _npcDecisionRequestsStarted,
                _lastOperation,
                _lastError,
                _lastAttemptUtc);
        }
    }

    private string SanitizeError(string value)
    {
        var sanitized = value;
        foreach (var fragment in _sensitiveEndpointFragments.Where(fragment =>
                     !string.IsNullOrWhiteSpace(fragment)))
        {
            sanitized = sanitized.Replace(
                fragment,
                "[redacted]",
                StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }

    private static string Limit(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters] + "…";
}
