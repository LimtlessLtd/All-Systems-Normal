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

                case ActionKind.SecureAirlock:
                    TickSecureAirlock(state, npc);
                    break;

                case ActionKind.RepairDoor:
                    TickDoorWork(state, npc, DoorWorkKind.Repair);
                    break;

                case ActionKind.WeldDoor:
                    TickDoorWork(state, npc, DoorWorkKind.Weld);
                    break;

                case ActionKind.BarricadeDoor:
                    TickDoorWork(state, npc, DoorWorkKind.Barricade);
                    break;
            }
        }
    }

    private static readonly string[] TechnicalSkillNames =
        { "Engineering", "Electrical", "Operations", "Reactor" };

    private const int RepairSkillGainPerTask = 1;

    public static int BestTechnicalSkill(Npc npc)
    {
        var baseSkill = TechnicalSkillNames
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

    /// <summary>
    /// Completing repair-type work nudges the crew member's strongest technical
    /// skill up slightly and deterministically — owner idea #7's "doing
    /// something improves skill slowly" half. Mentorship (a nearby more-skilled
    /// crewmate speeding this up) is a separate, not-yet-built slice.
    /// </summary>
    private static void GainTechnicalSkillFromRepairWork(Npc npc)
    {
        var skillName = TechnicalSkillNames
            .OrderByDescending(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .First();
        var current = npc.Skills.TryGetValue(skillName, out var value) ? value : 0;
        npc.Skills[skillName] = Math.Clamp(current + RepairSkillGainPerTask, 0, 100);
    }

    public static bool HasRestorableProblem(GameState state, string targetId)
    {
        if (targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase))
        {
            return HasSwitchedOffLifeSupport(state);
        }

        return state.Facility.Rooms.TryGetValue(targetId, out var room)
            && FindRoomProblem(state, room) is not null;
    }

    /// <summary>
    /// Life support is down for a reason the manual controls in Engineering can
    /// undo: the request was switched off, or a life-support unit was disabled
    /// (by Overseer or by hand) without failing. Any other cause is not a
    /// "restore" job. A failed unit needs servicing (<see cref="CrewMaintenanceSystem"/>),
    /// and an Engineering compartment shed by the grid needs generation back.
    /// Treating every outage as restorable used to send crew to flip a switch
    /// the next upkeep tick flipped straight back. That kept them at urgency
    /// 88-90, which blocked maintenance from ever servicing the worn reactor
    /// that caused the outage, and a whole crew suffocated.
    /// </summary>
    public static bool HasSwitchedOffLifeSupport(GameState state) =>
        !state.LifeSupport.IsOnline
        && (!state.LifeSupport.RequestedOnline
            || DisabledLifeSupportUnits(state).Any());

    private static IEnumerable<StationDevice> DisabledLifeSupportUnits(GameState state) =>
        state.Devices.Values.Where(device =>
            device.Kind is StationSystemKind.LifeSupport
                or StationSystemKind.OxygenGenerator
                or StationSystemKind.CarbonScrubber
            && !device.IsEnabled
            && !device.IsFailed);

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
                state,
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

            BeginTimedTask(
                state,
                npc,
                TimeSpan.FromMinutes(duration),
                useTechnical
                    ? $"bypassing {door.Id}"
                    : $"forcing {door.Id} open");
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
            EndAction(state, npc, $"Failed to open {door.Id}.");
            Log(
                state,
                $"{npc.Name} fails to force {door.Id}.");
            return;
        }

        door.IsLocked = false;
        door.LockedByOverseer = false;
        door.IsOpen = true;
        door.IsManuallyOverridden = true;
        door.IsAiControllable = false;
        door.IsTechnicallyBypassed = useTechnical;
        door.IsDamaged = !useTechnical;
        if (!useTechnical)
        {
            door.StructuralIntegrityPercent = Math.Min(door.StructuralIntegrityPercent, 45);
        }

        EndAction(
            state,
            npc,
            useTechnical
                ? $"Bypassed {door.Id}; it is now under local control."
                : $"Forced {door.Id} open; the hatch is damaged.",
            succeeded: true);

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

    private enum DoorWorkKind { Repair, Weld, Barricade }

    private static void TickDoorWork(GameState state, Npc npc, DoorWorkKind kind)
    {
        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(npc.CurrentAction.TargetId, StringComparison.OrdinalIgnoreCase));

        if (door is null || !IsAdjacent(npc, door))
        {
            EndAction(state, npc, "I need to be physically beside that hatch.");
            return;
        }

        var technical = BestRepairSkill(npc);
        var valid = kind switch
        {
            DoorWorkKind.Repair => door.IsDamaged || door.IsTechnicallyBypassed,
            DoorWorkKind.Weld => !door.IsPassable && !door.IsWelded && technical >= 65,
            DoorWorkKind.Barricade => !door.IsPassable && !door.IsBarricaded && BestForceSkill(npc) >= 55,
            _ => false
        };

        if (!valid)
        {
            EndAction(state, npc, "That hatch cannot be secured or repaired that way from here.");
            return;
        }

        if (npc.RoutineUntil == TimeSpan.Zero)
        {
            var minutes = kind switch
            {
                DoorWorkKind.Repair => 4,
                DoorWorkKind.Weld => 3,
                _ => 2
            };
            BeginTimedTask(
                state,
                npc,
                TimeSpan.FromMinutes(minutes),
                $"{kind.ToString().ToLowerInvariant()} work on {door.Id}");
            npc.Bubble = new NpcBubble(
                kind == DoorWorkKind.Repair ? "I'm repairing this hatch."
                    : kind == DoorWorkKind.Weld ? "I'm welding this hatch shut."
                    : "Help me barricade this hatch!",
                NpcBubbleKind.Alert, state.Elapsed, state.Elapsed + TimeSpan.FromMinutes(3));
            Log(state, $"{npc.Name} begins {kind.ToString().ToLowerInvariant()} work on {door.Id}.");
            return;
        }

        if (state.Elapsed < npc.RoutineUntil) return;

        switch (kind)
        {
            case DoorWorkKind.Repair:
                door.IsDamaged = false;
                door.IsTechnicallyBypassed = false;
                door.IsManuallyOverridden = false;
                door.StructuralIntegrityPercent = 100;
                door.IsAiControllable = true;
                GainTechnicalSkillFromRepairWork(npc);
                break;
            case DoorWorkKind.Weld:
                door.IsOpen = false;
                door.IsWelded = true;
                door.IsBarricaded = false;
                door.SecuredByNpcName = npc.Name;
                door.IsAiControllable = false;
                break;
            case DoorWorkKind.Barricade:
                door.IsOpen = false;
                door.IsBarricaded = true;
                door.IsWelded = false;
                door.SecuredByNpcName = npc.Name;
                door.IsAiControllable = false;
                break;
        }

        EndAction(
            state,
            npc,
            $"{door.Id} {kind.ToString().ToLowerInvariant()} work complete.",
            succeeded: true);
        Log(state, $"{npc.Name} completes {kind.ToString().ToLowerInvariant()} work on {door.Id}.");
    }

    private static void TickSecureAirlock(GameState state, Npc npc)
    {
        var targetId = npc.CurrentAction.TargetId;

        if (string.IsNullOrWhiteSpace(targetId)
            || !state.Facility.Rooms.TryGetValue(targetId, out var airlock)
            || airlock.Type != RoomType.Airlock
            || !airlock.HasExteriorHatch
            || !AirlockSafetySystem.NeedsCrewSecuring(state, airlock)
            || !AirlockSafetySystem.IsAtCrewControls(state, npc, airlock))
        {
            EndAction(state, npc, "The airlock emergency no longer needs action from here.");
            return;
        }

        if (npc.RoutineUntil == TimeSpan.Zero)
        {
            var technical = BestTechnicalSkill(npc);
            var duration = technical >= 70 ? 1 : 2;
            BeginTimedTask(
                state,
                npc,
                TimeSpan.FromMinutes(duration),
                $"securing {airlock.Name}");
            npc.Bubble = new NpcBubble(
                "I'm securing the airlock!",
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
                $"{npc.Name} begins emergency airlock securing.");
            return;
        }

        if (state.Elapsed < npc.RoutineUntil)
        {
            return;
        }

        if (!AirlockSafetySystem.TryCrewSecureNow(
                state,
                npc,
                airlock,
                out var message))
        {
            EndAction(state, npc, message);
            return;
        }

        EndAction(state, npc, message, succeeded: true);
        npc.Bubble = new NpcBubble(
            airlock.AirlockCycleMode == AirlockCycleMode.Pressurizing
                ? "Outer hatch sealed. Repressurizing."
                : "Outer hatch sealed.",
            NpcBubbleKind.Alert,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(4));

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Important,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        Log(state, message);
    }

    private static void TickRestoreSystem(GameState state, Npc npc)
    {
        var targetId = npc.CurrentAction.TargetId;

        if (string.IsNullOrWhiteSpace(targetId)
            || !HasRestorableProblem(state, targetId))
        {
            EndAction(state, npc, "There is nothing here that still needs restoring.");
            return;
        }

        var requiredRoom = RequiredRoomForRestore(state, targetId);

        if (requiredRoom is null
            || !npc.CurrentRoomId.Equals(
                requiredRoom,
                StringComparison.OrdinalIgnoreCase))
        {
            EndAction(state, npc, "I need to reach the affected controls before I can restore them.");
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
            BeginTimedTask(
                state,
                npc,
                TimeSpan.FromMinutes(duration),
                $"restoring {DescribeTarget(state, targetId)}");

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
            EndAction(state, npc, $"Failed to restore {DescribeTarget(state, targetId)}.");
            Log(
                state,
                $"{npc.Name} fails to restore {DescribeTarget(state, targetId)}.");
            return;
        }

        TryRestoreOneProblem(state, targetId);
        GainTechnicalSkillFromRepairWork(npc);
        EndAction(
            state,
            npc,
            $"Restored {DescribeTarget(state, targetId)}.",
            succeeded: true);

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

    /// <summary>
    /// What a person at the room's local controls could switch back on. A
    /// compartment the grid shed has nothing to restore by hand: the grid
    /// gives power back itself once generation allows. Forcing it on only got
    /// it shed again within minutes (a robot "restored" Engineering every
    /// three minutes in a soak while the worn reactor went unserviced).
    /// </summary>
    private static string? FindRoomProblem(GameState state, Room room)
    {
        if (state.Power.SheddedRoomIds.Contains(room.Id))
            return null;

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

    public static bool TryRestoreOneProblem(GameState state, string targetId)
    {
        if (targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase))
        {
            // Switch everything back on; StationUpkeepSystem then derives
            // whether it actually runs from power and unit health.
            state.LifeSupport.RequestedOnline = true;
            foreach (var unit in DisabledLifeSupportUnits(state).ToList())
                unit.IsEnabled = true;
            state.LifeSupport.IsOnline = true;
            return true;
        }

        if (!state.Facility.Rooms.TryGetValue(targetId, out var room))
        {
            return false;
        }

        if (!room.IsPowered)
        {
            room.IsPowered = true;
            return true;
        }

        if (room.HasVentilationControl && !room.VentilationEnabled)
        {
            room.VentilationEnabled = true;
            return true;
        }

        if (room.HasTemperatureControl && !room.TemperatureControlOnline)
        {
            room.TemperatureControlOnline = true;
            return true;
        }

        if (!room.CameraOnline)
        {
            room.CameraOnline = true;
            return true;
        }

        if (!room.LightsOn)
        {
            room.LightsOn = true;
            return true;
        }

        return false;
    }

    private static string DescribeTarget(GameState state, string targetId) =>
        targetId.Equals(LifeSupportTarget, StringComparison.OrdinalIgnoreCase)
            ? "primary life support"
            : state.Facility.Rooms.TryGetValue(targetId, out var room)
                ? $"{room.Name} {FindRoomProblem(state, room) ?? "systems"}"
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

    private static void BeginTimedTask(
        GameState state,
        Npc npc,
        TimeSpan duration,
        string description)
    {
        npc.RoutineUntil = state.Elapsed + duration;
        CrewTaskSystem.Start(
            state,
            npc,
            npc.CurrentAction.Kind,
            npc.CurrentAction.TargetId,
            description,
            duration);
    }

    private static void EndAction(
        GameState state,
        Npc npc,
        string reason,
        bool succeeded = false)
    {
        if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress })
        {
            if (succeeded)
                CrewTaskSystem.Succeed(state, npc, reason);
            else
                CrewTaskSystem.Fail(state, npc, reason);
        }

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
