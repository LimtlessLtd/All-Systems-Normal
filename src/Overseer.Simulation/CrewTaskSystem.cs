using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Shared deterministic bookkeeping for physical timed work. This is deliberately
/// not a planner: cognition may choose an intention, while simulation systems
/// start/interrupt/complete the task and therefore own its outcome.
/// </summary>
public static class CrewTaskSystem
{
    public const int CommitmentUrgency = 80;

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
        task.Status = CrewTaskStatus.Succeeded;
        task.Outcome = outcome;
        Log(state, $"{npc.Name} completes {task.Description}: {outcome}");
    }

    public static void Interrupt(GameState state, Npc npc, string reason)
    {
        if (npc.ActiveTask is not { Status: CrewTaskStatus.InProgress } task) return;
        task.Status = CrewTaskStatus.Interrupted;
        task.Outcome = reason;
        Log(state, $"{npc.Name}'s task is interrupted ({task.Description}): {reason}");
    }

    public static void Fail(GameState state, Npc npc, string reason)
    {
        if (npc.ActiveTask is not { } task) return;
        task.Status = CrewTaskStatus.Failed;
        task.Outcome = reason;
        Log(state, $"{npc.Name}'s task fails ({task.Description}): {reason}");
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
