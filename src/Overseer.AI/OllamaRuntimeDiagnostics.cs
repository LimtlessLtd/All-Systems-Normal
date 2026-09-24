using Overseer.Simulation;

namespace Overseer.AI;

/// <summary>
/// Bounded operational telemetry for the local Ollama provider. Counters and
/// redacted errors are always payload-free. Exact request/response payloads are
/// retained only when Development-only capture is explicitly enabled.
/// </summary>
public sealed class OllamaRuntimeDiagnostics
{
    private const int MaxRecentProviderTraces = 100;

    private readonly object _gate = new();
    private readonly string[] _sensitiveEndpointFragments;
    private readonly bool _capturePayloads;
    private readonly List<AiProviderDebugTrace> _recentProviderTraces = [];
    private long _requestsStarted;
    private long _responsesReceived;
    private long _failures;
    private long _npcDecisionRequestsStarted;
    private long _nextSequence;
    private string? _lastOperation;
    private string? _lastError;
    private DateTimeOffset? _lastAttemptUtc;

    public OllamaRuntimeDiagnostics(
        Uri endpoint,
        string model,
        bool capturePayloads = false)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint.IsDefaultPort
            ? $"{endpoint.Scheme}://{endpoint.Host}"
            : $"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}";
        Model = model;
        _capturePayloads = capturePayloads;
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

    public long RecordStarted(
        string operation,
        bool npcDecision = false,
        string? prompt = null)
    {
        lock (_gate)
        {
            _requestsStarted++;
            if (npcDecision)
                _npcDecisionRequestsStarted++;

            _lastOperation = operation;
            _lastError = null;
            _lastAttemptUtc = DateTimeOffset.UtcNow;
            var sequence = ++_nextSequence;

            if (_capturePayloads)
            {
                _recentProviderTraces.Insert(
                    0,
                    new AiProviderDebugTrace(
                        sequence,
                        _lastAttemptUtc.Value,
                        operation,
                        npcDecision,
                        prompt,
                        null,
                        null));

                if (_recentProviderTraces.Count > MaxRecentProviderTraces)
                {
                    _recentProviderTraces.RemoveRange(
                        MaxRecentProviderTraces,
                        _recentProviderTraces.Count - MaxRecentProviderTraces);
                }
            }

            return sequence;
        }
    }

    public void RecordResponse(
        long sequence,
        string operation,
        string? rawResponse = null)
    {
        lock (_gate)
        {
            _responsesReceived++;
            _lastOperation = operation;
            _lastError = null;
            UpdateTrace(sequence, rawResponse: rawResponse, error: null);
        }
    }

    public void RecordFailure(
        long sequence,
        string operation,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        RecordFailure(
            sequence,
            operation,
            $"{exception.GetType().Name}: {exception.Message}");
    }

    public void RecordFailure(
        long sequence,
        string operation,
        string error)
    {
        lock (_gate)
        {
            _failures++;
            _lastOperation = operation;
            _lastError = Limit(SanitizeError(error), 600);
            UpdateTrace(sequence, rawResponse: null, error: _lastError);
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
                _lastAttemptUtc,
                _recentProviderTraces.ToArray());
        }
    }

    private void UpdateTrace(
        long sequence,
        string? rawResponse,
        string? error)
    {
        if (!_capturePayloads)
            return;

        var index = _recentProviderTraces.FindIndex(trace =>
            trace.Sequence == sequence);
        if (index < 0)
            return;

        var trace = _recentProviderTraces[index];
        _recentProviderTraces[index] = trace with
        {
            RawResponse = rawResponse ?? trace.RawResponse,
            Error = error
        };
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
