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

    public const int EntriesRead = 5;

    /// <summary>
    /// Owner idea #26, slice 2: a crew member at a powered hatch reads its
    /// latest entries into their own memory. They learn only what the
    /// controller recorded (a keycard entry names the card's owner), never
    /// who really did it; what that evidence means is the mind's to judge.
    /// </summary>
    public static Memory RememberReading(GameState state, Npc reader, Door door)
    {
        var entries = door.AccessLog.Count == 0
            ? "no lock activity recorded"
            : string.Join("; ", door.AccessLog.Take(EntriesRead).Select(Describe));
        var memory = new Memory(
            $"I read {door.Id}'s access log: {entries}.",
            state.Elapsed,
            .55);
        reader.Memories.Add(memory);
        return memory;
    }

    public const int WipeSkillRequired = 65;

    public static bool CanWipe(Npc npc, Door door) =>
        CrewDoorInteractionSystem.IsAdjacent(npc, door)
        && door.IsPowered
        && CrewCounterplaySystem.BestTechnicalSkill(npc) >= WipeSkillRequired;

    /// <summary>
    /// Owner idea #26, slice 3: erase a hatch's access log at its panel. The
    /// controller cannot record its own erasure's author, but it does record
    /// that the log was wiped, so a later reader finds the gap. The wiper and
    /// anyone who sees them do it remember it as sensitive (#14). Returns the
    /// event-log message; the caller has checked <see cref="CanWipe"/>.
    /// </summary>
    public static string Wipe(GameState state, Npc npc, Door door)
    {
        door.AccessLog.Clear();
        Record(state, door, new DoorAccessRecord
        {
            At = state.Elapsed,
            Kind = DoorAccessKind.LogWiped,
            Credential = DoorAccessCredential.None,
            ActorId = npc.Id
        });

        npc.Memories.Add(new Memory(
            $"I wiped {door.Id}'s access log.",
            state.Elapsed,
            .45,
            IsSensitive: true));
        ActionResolver.NotifyPhysicalInteractionWitnesses(
            state,
            npc,
            $"erase {door.Id}'s access log at its panel",
            isSensitive: true);

        var message = $"{npc.Name} wipes {door.Id}'s access log.";
        if (state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            var line = OverseerSightSystem.Witnessed(room, message);
            Log(state, line);
            if (OverseerSightSystem.IsUnseen(line))
                Log(state, $"ACCESS LOG: {door.Id}'s log was wiped at its panel.");
        }
        else
        {
            Log(state, message);
        }

        return message;
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");

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

        if (record.Kind == DoorAccessKind.LogWiped)
            return $"T+{record.At:hh\\:mm} LOG WIPED (earlier entries erased at the panel)";

        return $"T+{record.At:hh\\:mm} {what} ({who})";
    }
}
