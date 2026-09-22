using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Shared deterministic bookkeeping for physical timed work. This is deliberately
/// not a planner: cognition may choose an intention, while simulation systems
/// start/interrupt/complete the task and therefore own its outcome.
/// </summary>
public static class CrewTaskSystem
{
    /// <summary>
    /// A committed physical task is not pre-empted by an arbitrary high urgency
    /// number. The world must contain an immediate survival threat and the new
    /// intention must actually be a response to it.
    /// </summary>
    public static bool CanInterruptForLifeThreat(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(intent);

        if (!state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
            return false;

        var hazardousAtmosphere =
            room.FireIntensity > 0
            || room.OxygenPercent < 19
            || room.PressureKpa < 90
            || room.SmokePercent >= 35
            || room.CarbonDioxidePercent > 1.25;

        var hostileMachineHere =
            state.Robots.Any(robot =>
                !robot.IsDestroyed
                && robot.IsOperational
                && robot.Policy == RobotPolicy.Hostile
                && robot.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            || state.Turrets.Any(turret =>
                !turret.IsDestroyed
                && turret.IsArmed
                && turret.Policy == TurretPolicy.Hostile
                && turret.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase));

        var acuteInjury =
            npc.Health <= 40
            || npc.LastHealthSnapshot - npc.Health >= 8;

        var genuineThreat = hazardousAtmosphere || hostileMachineHere || acuteInjury;
        if (!genuineThreat)
            return false;

        return intent.Action is
            ActionKind.SeekSafety
            or ActionKind.EvacuateHazard
            or ActionKind.FightFire
            or ActionKind.SealHazardRoom
            or ActionKind.VentHazardRoom
            or ActionKind.RequestHelp;
    }

    public static CrewTaskState Start(
        GameState state,
        Npc npc,
        ActionKind action,
        string? targetId,
        string description,
        TimeSpan duration)
    {
        var task = new CrewTaskState
        {
            Action = action,
            TargetId = targetId,
            Description = description,
            StartedAt = state.Elapsed,
            CompletesAt = state.Elapsed + duration,
            Status = CrewTaskStatus.InProgress,
            Outcome = "In progress."
        };
        npc.ActiveTask = task;
        return task;
    }

    public static bool IsWorking(Npc npc, ActionKind? action = null) =>
        npc.ActiveTask is { Status: CrewTaskStatus.InProgress } task
        && (action is null || task.Action == action);

    public static double Progress(GameState state, Npc npc) =>
        npc.ActiveTask?.ProgressPercent(state.Elapsed) ?? 0;

    public static bool IsComplete(GameState state, Npc npc) =>
        npc.ActiveTask is { Status: CrewTaskStatus.InProgress } task
        && state.Elapsed >= task.CompletesAt;

    public static void Succeed(GameState state, Npc npc, string outcome)
    {
        if (npc.ActiveTask is not { } task) return;
        task.FinalProgressPercent = 100;
        task.Status = CrewTaskStatus.Succeeded;
        task.Outcome = outcome;
        Log(state, $"{npc.Name} completes {task.Description}: {outcome}");
    }

    public static void Interrupt(GameState state, Npc npc, string reason)
    {
        if (npc.ActiveTask is not { Status: CrewTaskStatus.InProgress } task) return;
        task.FinalProgressPercent = task.ProgressPercent(state.Elapsed);
        task.Status = CrewTaskStatus.Interrupted;
        task.Outcome = reason;
        Log(state, $"{npc.Name}'s task is interrupted ({task.Description}): {reason}");
    }

    public static void Fail(GameState state, Npc npc, string reason)
    {
        if (npc.ActiveTask is not { } task) return;
        task.FinalProgressPercent = task.ProgressPercent(state.Elapsed);
        task.Status = CrewTaskStatus.Failed;
        task.Outcome = reason;
        Log(state, $"{npc.Name}'s task fails ({task.Description}): {reason}");
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
