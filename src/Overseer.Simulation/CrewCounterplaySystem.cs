using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Resolves physical human attempts to defeat blocked doors or restore station
/// systems. An NPC must first choose one of these actions; this system only
/// resolves time, skill, traits and deterministic pseudo-random outcome.
/// </summary>
public sealed class CrewCounterplaySystem
{
    public const string LifeSupportTarget = "life-support";

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            switch (npc.CurrentAction.Kind)
            {
                case ActionKind.ForceDoor:
                    TickForceDoor(state, npc);
                    break;

                case ActionKind.RestoreSystem:
                    TickRestoreSystem(state, npc);
                    break;
            }
        }
    }

    public static int BestTechnicalSkill(Npc npc)
    {
        var relevant = new[] { "Engineering", "Electrical", "Operations", "Reactor" };
        var baseSkill = relevant
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp(
            baseSkill
            + CrewTraitMath.Modifier(npc, TraitEffectKind.Technical),
            0,
            120);
    }

    public static int BestForceSkill(Npc npc)
    {
        var relevant = new[] { "Athletics", "Security", "Operations" };
        var baseSkill = relevant
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp(
            baseSkill
            + CrewTraitMath.Modifier(npc, TraitEffectKind.Force)
            + (CrewTraitMath.Modifier(npc, TraitEffectKind.Courage) / 3),
            0,
            120);
    }

    public static int BestRepairSkill(Npc npc) =>
        Math.Clamp(
            BestTechnicalSkill(npc)
            + CrewTraitMath.Modifier(npc, TraitEffectKind.Repair),
            0,
            130);

    public static bool HasRestorableProblem(GameState state, string targetId)
    {
        if (targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase))
        {
            return !state.LifeSupport.IsOnline;
        }

        return state.Facility.Rooms.TryGetValue(targetId, out var room)
            && FindRoomProblem(room) is not null;
    }

    public static string? RequiredRoomForRestore(GameState state, string targetId)
    {
        if (targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase))
        {
            return "engineering";
        }

        return state.Facility.Rooms.ContainsKey(targetId)
            ? targetId
            : null;
    }

    private static void TickForceDoor(GameState state, Npc npc)
    {
        var door = state.Facility.Doors.FirstOrDefault(door =>
            door.Id.Equals(
                npc.CurrentAction.TargetId,
                StringComparison.OrdinalIgnoreCase));

        if (door is null
            || !door.CanBeForced
            || door.IsPassable
            || !IsAdjacent(npc, door))
        {
            EndAction(
                npc,
                door is { IsPassable: true }
                    ? $"{door.Id} is already open."
                    : "I cannot force that hatch from here.");
            return;
        }

        var technical = BestTechnicalSkill(npc);
        var force = BestForceSkill(npc);
        var useTechnical = technical >= force;
        var score = useTechnical ? technical : force;
        var difficulty = useTechnical
            ? door.TechnicalDifficulty
            : door.ForceDifficulty;

        if (npc.RoutineUntil == TimeSpan.Zero)
        {
            var duration = Math.Clamp(
                5 - ((score - difficulty) / 20),
                2,
                6);

            npc.RoutineUntil =
                state.Elapsed + TimeSpan.FromMinutes(duration);
            npc.Bubble = new NpcBubble(
                useTechnical
                    ? "I'm bypassing this hatch."
                    : "Help me get this hatch open!",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(3));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Warning,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            Log(
                state,
                $"{npc.Name} begins {(useTechnical ? "a technical bypass" : "forcing")} {door.Id}.");
            return;
        }

        if (state.Elapsed < npc.RoutineUntil)
        {
            return;
        }

        var success = ResolveCheck(
            state,
            npc,
            door.Id,
            score,
            difficulty,
            salt: useTechnical ? 31 : 17);

        if (!success)
        {
            npc.Stress = Math.Clamp(npc.Stress + 6, 0, 100);
            EndAction(npc, $"Failed to open {door.Id}.");
            Log(
                state,
                $"{npc.Name} fails to force {door.Id}.");
            return;
        }

        door.IsLocked = false;
        door.IsOpen = true;
        door.IsManuallyOverridden = true;
        door.IsAiControllable = false;
        door.IsDamaged = !useTechnical;

        EndAction(
            npc,
            useTechnical
                ? $"Bypassed {door.Id}; it is now under local control."
                : $"Forced {door.Id} open; the hatch is damaged.");

        npc.Bubble = new NpcBubble(
            useTechnical ? "Bypass worked." : "It's open!",
            NpcBubbleKind.Alert,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(3));

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Important,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        Log(
            state,
            $"{npc.Name} {(useTechnical ? "bypasses" : "physically forces")} {door.Id} open.");
    }

    private static void TickRestoreSystem(GameState state, Npc npc)
    {
        var targetId = npc.CurrentAction.TargetId;

        if (string.IsNullOrWhiteSpace(targetId)
            || !HasRestorableProblem(state, targetId))
        {
            EndAction(npc, "There is nothing here that still needs restoring.");
            return;
        }

        var requiredRoom = RequiredRoomForRestore(state, targetId);

        if (requiredRoom is null
            || !npc.CurrentRoomId.Equals(
                requiredRoom,
                StringComparison.OrdinalIgnoreCase))
        {
            EndAction(npc, "I need to reach the affected controls before I can restore them.");
            return;
        }

        var score = BestRepairSkill(npc);
        var difficulty = targetId.Equals(
            LifeSupportTarget,
            StringComparison.OrdinalIgnoreCase)
                ? 68
                : 58;

        if (npc.RoutineUntil == TimeSpan.Zero)
        {
            var duration = Math.Clamp(
                5 - ((score - difficulty) / 22),
                2,
                6);
            npc.RoutineUntil =
                state.Elapsed + TimeSpan.FromMinutes(duration);

            npc.Bubble = new NpcBubble(
                "I'm trying to bring it back online.",
                NpcBubbleKind.Speech,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(3));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Warning,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            Log(
                state,
                $"{npc.Name} begins restoring {DescribeTarget(state, targetId)}.");
            return;
        }

        if (state.Elapsed < npc.RoutineUntil)
        {
            return;
        }

        var success = ResolveCheck(
            state,
            npc,
            targetId,
            score,
            difficulty,
            salt: 73);

        if (!success)
        {
            npc.Stress = Math.Clamp(npc.Stress + 4, 0, 100);
            EndAction(npc, $"Failed to restore {DescribeTarget(state, targetId)}.");
            Log(
                state,
                $"{npc.Name} fails to restore {DescribeTarget(state, targetId)}.");
            return;
        }

        RestoreOneProblem(state, targetId);
        EndAction(npc, $"Restored {DescribeTarget(state, targetId)}.");

        npc.Bubble = new NpcBubble(
            "System restored.",
            NpcBubbleKind.Alert,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(3));

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Important,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        Log(
            state,
            $"{npc.Name} restores {DescribeTarget(state, targetId)}.");
    }

    private static string? FindRoomProblem(Room room)
    {
        if (!room.IsPowered)
            return "power";

        if (room.HasVentilationControl && !room.VentilationEnabled)
            return "ventilation";

        if (room.HasTemperatureControl && !room.TemperatureControlOnline)
            return "climate control";

        if (!room.CameraOnline)
            return "camera";

        if (!room.LightsOn)
            return "lighting";

        return null;
    }

    private static void RestoreOneProblem(GameState state, string targetId)
    {
        if (targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase))
        {
            state.LifeSupport.IsOnline = true;
            return;
        }

        var room = state.Facility.Rooms[targetId];

        if (!room.IsPowered)
        {
            room.IsPowered = true;
            return;
        }

        if (room.HasVentilationControl && !room.VentilationEnabled)
        {
            room.VentilationEnabled = true;
            return;
        }

        if (room.HasTemperatureControl && !room.TemperatureControlOnline)
        {
            room.TemperatureControlOnline = true;
            return;
        }

        if (!room.CameraOnline)
        {
            room.CameraOnline = true;
            return;
        }

        if (!room.LightsOn)
        {
            room.LightsOn = true;
        }
    }

    private static string DescribeTarget(GameState state, string targetId) =>
        targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase)
            ? "primary life support"
            : state.Facility.Rooms.TryGetValue(targetId, out var room)
                ? $"{room.Name} {FindRoomProblem(room) ?? "systems"}"
                : targetId;

    private static bool ResolveCheck(
        GameState state,
        Npc npc,
        string targetId,
        int score,
        int difficulty,
        int salt)
    {
        if (score >= difficulty + 28)
        {
            return true;
        }

        var chance = Math.Clamp(
            0.38 + ((score - difficulty) * 0.018),
            0.06,
            0.92);
        var roll = StableRoll(
            (int)Math.Floor(state.Elapsed.TotalMinutes),
            npc.Name,
            targetId,
            salt);

        return roll <= chance;
    }

    private static bool IsAdjacent(Npc npc, Door door) =>
        npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
        || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

    private static void EndAction(Npc npc, string reason)
    {
        npc.RoutineUntil = TimeSpan.Zero;
        npc.Intent = null;
        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            null,
            reason);
    }

    private static double StableRoll(
        int minute,
        string first,
        string second,
        int salt)
    {
        unchecked
        {
            uint hash = 2166136261;

            foreach (var ch in $"{minute}|{first}|{second}|{salt}")
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (hash % 10_000) / 10_000d;
        }
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
