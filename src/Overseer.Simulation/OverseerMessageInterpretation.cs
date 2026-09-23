using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Structured reading of a free-text Overseer message, as returned by the model
/// before validation.
/// </summary>
public sealed class OverseerMessageReading
{
    public required string Claim { get; init; }
    public string? SubjectPerson { get; init; }
    public string? SubjectRoomId { get; init; }
}

/// <summary>
/// A validated interpretation the simulation is willing to act on. Anything the
/// model invented has already been stripped.
/// </summary>
public sealed record OverseerMessageIntent(
    OverseerClaimKind Claim,
    string? SubjectNpcName,
    string? SubjectRoomId,
    string Source);

public interface IOverseerMessageInterpreter
{
    Task<OverseerMessageIntent> InterpretAsync(
        string text,
        OverseerMessageScope scope,
        string? targetNpcName,
        GameState state,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared validation. The model proposes a reading; this decides what the
/// simulation will accept, exactly as NPC cognition does for intents. A claim
/// about a room or person that does not exist collapses to
/// <see cref="OverseerClaimKind.None"/> — the message is still delivered as
/// social text, it simply asserts nothing the world can falsify.
/// </summary>
public static class OverseerMessageValidator
{
    public static OverseerMessageIntent Validate(
        OverseerMessageReading reading,
        GameState state,
        string source)
    {
        if (!Enum.TryParse<OverseerClaimKind>(reading.Claim, true, out var claim))
        {
            claim = OverseerClaimKind.None;
        }

        var person = state.Crew.FirstOrDefault(npc =>
            npc.Name.Equals(reading.SubjectPerson?.Trim(), StringComparison.OrdinalIgnoreCase));

        var roomId = reading.SubjectRoomId?.Trim();
        var room = roomId is not null && state.Facility.Rooms.TryGetValue(roomId, out var found)
            ? found
            : null;

        // The FIRE ALARM is its own console control, not something free text
        // can trip; a typed "there's a fire in X" is an ordinary warning.
        if (claim == OverseerClaimKind.FireAlarm)
        {
            claim = OverseerClaimKind.Warning;
        }

        // Blaming nobody is not blame; a hazard warning about nowhere is noise.
        if (claim == OverseerClaimKind.BlameCrew && person is null)
        {
            claim = OverseerClaimKind.None;
        }

        if (claim is OverseerClaimKind.Warning or OverseerClaimKind.SystemStatus
            && room is null)
        {
            claim = OverseerClaimKind.None;
        }

        return new OverseerMessageIntent(
            claim,
            person?.Name,
            room?.Id,
            source);
    }
}

/// <summary>
/// Deterministic keyword reading, used when no model is available (the browser
/// build) and as the fallback when Ollama is offline or returns nonsense.
/// Deliberately conservative: it would rather classify a message as social
/// noise than invent an accusation the player did not make.
/// </summary>
public sealed class RuleBasedOverseerMessageInterpreter : IOverseerMessageInterpreter
{
    public Task<OverseerMessageIntent> InterpretAsync(
        string text,
        OverseerMessageScope scope,
        string? targetNpcName,
        GameState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var lower = (text ?? string.Empty).ToLowerInvariant();

        var person = state.Crew.FirstOrDefault(npc =>
            MentionsPerson(lower, npc.Name));

        var room = state.Facility.Rooms.Values.FirstOrDefault(candidate =>
            lower.Contains(candidate.Name.ToLowerInvariant(), StringComparison.Ordinal)
            || lower.Contains(candidate.Id.ToLowerInvariant(), StringComparison.Ordinal));

        var claim = OverseerClaimKind.None;

        if (person is not null && ContainsAny(lower,
                "fault", "caused", "blame", "responsible", "sabotage", "broke",
                "disabled", "tampered", "did this", "their doing"))
        {
            claim = OverseerClaimKind.BlameCrew;
        }
        else if (ContainsAny(lower,
                     "danger", "hazard", "evacuate", "warning", "unsafe",
                     "leak", "breach", "get out", "do not enter"))
        {
            claim = room is null ? OverseerClaimKind.None : OverseerClaimKind.Warning;
        }
        else if (ContainsAny(lower,
                     "safe", "nominal", "no cause for concern", "under control",
                     "all systems normal", "nothing is wrong", "you are fine"))
        {
            claim = OverseerClaimKind.Reassurance;
        }
        else if (room is not null && ContainsAny(lower,
                     "power", "lights", "ventilation", "air", "temperature",
                     "heater", "climate", "restored", "offline", "online"))
        {
            claim = OverseerClaimKind.SystemStatus;
        }
        else if (ContainsAny(lower,
                     "go to", "report to", "head to", "proceed to", "check",
                     "inspect", "meet"))
        {
            claim = OverseerClaimKind.Instruction;
        }

        var reading = new OverseerMessageReading
        {
            Claim = claim.ToString(),
            SubjectPerson = person?.Name,
            SubjectRoomId = room?.Id
        };

        return Task.FromResult(
            OverseerMessageValidator.Validate(reading, state, "Rule-based"));
    }

    private static bool MentionsPerson(string lower, string name)
    {
        if (lower.Contains(name.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return true;
        }

        var first = name.Split(' ')[0].ToLowerInvariant();
        return first.Length >= 3 && lower.Contains(first, StringComparison.Ordinal);
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));
}
