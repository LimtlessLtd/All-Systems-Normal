using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Grounded opportunity for containing a decompression: an open hatch in the
/// crew member's own compartment that leads toward the breach. Closing it
/// stops this compartment (and everything behind it) draining to space.
///
/// This only finds the opportunity. Both deterministic fallback minds take it
/// (owner principle: without an LLM the crew act as model citizens), Ollama
/// cognition is shown it and chooses for itself, and the ordinary CloseDoor
/// validation decides whether the hatch actually shuts. Closing is never a
/// trap: ordinary hatches stay openable from either side, so anyone still
/// behind it can come through, after which the next person on the safe side
/// closes it again.
///
/// Before this, the Pages build's fallback minds never closed a hatch against
/// a breach, so one fire-driven hull breach drained every room of a station
/// whose hatches stood open and killed the whole crew.
/// </summary>
public static class DecompressionContainmentRules
{
    /// <summary>
    /// The open hatch beside <paramref name="npc"/> whose far side is closer
    /// to the breach than their own compartment, or null when there is none
    /// they can close. Someone standing in the breached compartment itself
    /// (depth 0) gets nothing here: shutting a hatch cannot help them, and
    /// they should leave instead.
    /// </summary>
    public static Door? FindHatchTowardBreach(
        GameState state,
        Npc npc,
        IReadOnlyDictionary<string, int>? vacuumDepths = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);

        if (!npc.IsAlive || !npc.IsPresent)
            return null;

        vacuumDepths ??= EnvironmentSystem.FindVacuumDepths(state);

        if (!vacuumDepths.TryGetValue(npc.CurrentRoomId, out var ownDepth)
            || ownDepth <= 0)
            return null;

        return state.Facility.Doors
            .Where(door =>
                CrewDoorInteractionSystem.CanClose(npc, door)
                && !HasTraffic(state, door)
                && !SomeoneElseIsClosing(state, npc, door))
            .Select(door => (
                Door: door,
                FarDepth: vacuumDepths.TryGetValue(
                    FarSide(npc, door),
                    out var depth)
                    ? depth
                    : int.MaxValue))
            .Where(candidate => candidate.FarDepth < ownDepth)
            .OrderBy(candidate => candidate.FarDepth)
            .ThenBy(candidate => candidate.Door.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Door)
            .FirstOrDefault();
    }

    /// <summary>
    /// True while this person already intends, or is already working, to
    /// close <paramref name="door"/>, so emergency cognition does not keep
    /// restarting the same one-minute hatch operation.
    /// </summary>
    public static bool IsAlreadyClosing(Npc npc, Door door) =>
        npc.Intent is { Action: ActionKind.CloseDoor, TargetId: { } intentTarget }
            && intentTarget.Equals(door.Id, StringComparison.OrdinalIgnoreCase)
        || npc.ActiveTask is
            {
                Status: CrewTaskStatus.InProgress,
                Action: ActionKind.CloseDoor,
                TargetId: { } taskTarget
            }
            && taskTarget.Equals(door.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Goal wording shared by both fallback minds.</summary>
    public static string Goal(Door hatch) =>
        $"Shut {hatch.Id} against the decompression.";

    /// <summary>Reason wording shared by both fallback minds.</summary>
    public static string Reason(GameState state, Npc npc, Door hatch) =>
        $"Air is rushing out toward {state.Facility.Rooms[FarSide(npc, hatch)].Name}; closing {hatch.Id} seals this side off from the breach.";

    public static string FarSide(Npc npc, Door door) =>
        door.RoomAId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
            ? door.RoomBId
            : door.RoomAId;

    private static bool SomeoneElseIsClosing(GameState state, Npc npc, Door door) =>
        state.Crew.Any(other =>
            other.Id != npc.Id
            && other.IsAlive
            && other.IsPresent
            && IsAlreadyClosing(other, door));

    /// <summary>
    /// Nobody shuts a hatch on someone who is physically crossing it.
    /// </summary>
    private static bool HasTraffic(GameState state, Door door) =>
        state.Crew.Any(other =>
            other.IsAlive
            && other.IsPresent
            && other.Movement?.DoorId.Equals(
                door.Id,
                StringComparison.OrdinalIgnoreCase) == true)
        || state.Robots.Any(robot =>
            !robot.IsDestroyed
            && robot.Movement?.DoorId.Equals(
                door.Id,
                StringComparison.OrdinalIgnoreCase) == true);
}
