using Overseer.Domain;

namespace Overseer.Simulation;

public static class CognitionTelemetrySystem
{
    private const int MaxEntries = 240;
    private const int MaxPromptCharacters = 64_000;
    private const int MaxResponseCharacters = 32_000;

    public static void Record(
        GameState state,
        Npc npc,
        string source,
        NpcIntent? intent,
        string? prompt = null,
        string? rawResponse = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);

        state.CognitionTelemetry.Insert(
            0,
            new CognitionTelemetryEntry(
                state.NextCognitionTelemetrySequence++,
                state.Elapsed,
                npc.Id,
                npc.Name,
                source,
                Limit(prompt, MaxPromptCharacters),
                Limit(rawResponse, MaxResponseCharacters),
                intent?.Action,
                intent?.TargetId,
                intent?.Goal,
                intent?.Reason,
                intent?.Urgency,
                note));

        if (state.CognitionTelemetry.Count > MaxEntries)
        {
            state.CognitionTelemetry.RemoveRange(
                MaxEntries,
                state.CognitionTelemetry.Count - MaxEntries);
        }
    }

    private static string? Limit(string? value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.Length <= maxCharacters
            ? value
            : value[..maxCharacters] + "\n… [truncated]";
    }
}
