using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #26, slice 1: every hatch's access controller remembers who
/// locked, unlocked, forced or bypassed it, when, and with which credential.
/// The log records what the controller could actually know: trained access
/// identifies the person, a keycard only identifies whose card it is, and a
/// forced or bypassed hatch identifies nobody. An unpowered controller
/// records nothing. Reading, wiping and falsifying the log are later slices.
/// </summary>
public static class DoorAccessLogSystem
{
    public const int MaxEntries = 24;

    /// <summary>
    /// The credential a crew member's lock or unlock presents. Trained access
    /// comes first (a person with their own PIN/biometric uses it); otherwise
    /// the keycard they hold, which the controller attributes to its owner.
    /// </summary>
    public static (DoorAccessCredential Credential, Guid? RecordedId, string? RecordedName)
        CrewCredential(GameState state, Npc npc)
    {
        if (CrewDoorInteractionSystem.HasLockAuthority(npc))
            return (DoorAccessCredential.TrainedAccess, npc.Id, npc.Name);

        if (CrewDoorInteractionSystem.HeldLockCredential(state, npc) is { } card)
        {
            var owner = state.Crew.FirstOrDefault(crew => crew.Id == card.OwnerId);
            return (DoorAccessCredential.Keycard, card.OwnerId, owner?.Name);
        }

        return (DoorAccessCredential.None, null, null);
    }

    public static void RecordCrew(
        GameState state,
        Door door,
        Npc npc,
        DoorAccessKind kind)
    {
        var (credential, recordedId, recordedName) =
            kind is DoorAccessKind.Lock or DoorAccessKind.Unlock
                ? CrewCredential(state, npc)
                : (DoorAccessCredential.None, null, null);

        Record(state, door, new DoorAccessRecord
        {
            At = state.Elapsed,
            Kind = kind,
            Credential = credential,
            RecordedId = recordedId,
            RecordedName = recordedName,
            ActorId = npc.Id
        });
    }

    public static void RecordOverseer(GameState state, Door door, DoorAccessKind kind) =>
        Record(state, door, new DoorAccessRecord
        {
            At = state.Elapsed,
            Kind = kind,
            Credential = DoorAccessCredential.OverseerNetwork,
            RecordedName = "Overseer"
        });

    private static void Record(GameState state, Door door, DoorAccessRecord record)
    {
        if (!door.IsPowered)
            return;

        door.AccessLog.Insert(0, record);
        if (door.AccessLog.Count > MaxEntries)
            door.AccessLog.RemoveRange(MaxEntries, door.AccessLog.Count - MaxEntries);
    }

    /// <summary>One log entry as the door Inspector shows it.</summary>
    public static string Describe(DoorAccessRecord record)
    {
        var what = record.Kind switch
        {
            DoorAccessKind.Lock => "LOCKED",
            DoorAccessKind.Unlock => "UNLOCKED",
            DoorAccessKind.ForcedOpen => "FORCED OPEN",
            DoorAccessKind.Bypassed => "LOCK BYPASSED",
            _ => "ACCESSED"
        };

        var who = record.Credential switch
        {
            DoorAccessCredential.TrainedAccess => $"trained access: {record.RecordedName ?? "unknown"}",
            DoorAccessCredential.Keycard => $"keycard issued to {record.RecordedName ?? "unknown"}",
            DoorAccessCredential.OverseerNetwork => "Overseer network command",
            _ => "no credential, unidentified"
        };

        return $"T+{record.At:hh\\:mm} {what} ({who})";
    }
}
