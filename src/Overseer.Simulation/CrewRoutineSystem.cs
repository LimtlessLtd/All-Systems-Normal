using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class CrewRoutineSystem
{
    private readonly ActionResolver _actions = new();

    private static readonly IReadOnlyDictionary<CrewRole, string[]> Routes =
        new Dictionary<CrewRole, string[]>
        {
            [CrewRole.Commander] = ["control", "corridor", "kitchen", "quarters"],
            [CrewRole.Engineer] = ["engineering", "reactor", "generator", "control"],
            [CrewRole.Security] = ["corridor", "airlock", "storage", "control"],
            [CrewRole.Doctor] = ["medical", "quarters", "kitchen", "corridor"],
            [CrewRole.Technician] = ["generator", "engineering", "storage", "control"],
            [CrewRole.Scientist] = ["reactor", "engineering", "control", "medical"]
        };

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0)
        {
            return;
        }

        foreach (var npc in state.Crew)
        {
            // Spread movement across the clock instead of moving the whole crew at once.
            if ((minute + (int)npc.Role) % 3 != 0)
            {
                continue;
            }

            var targetRoomId = ChooseTargetRoom(npc, minute);

            if (npc.CurrentRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
            {
                HandleArrival(state, npc, targetRoomId);
                continue;
            }

            var path = FindPath(state.Facility, npc.CurrentRoomId, targetRoomId);

            if (path.Count < 2)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    targetRoomId,
                    $"Route to {state.Facility.Rooms[targetRoomId].Name} is blocked.");
                continue;
            }

            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    path[1],
                    $"Heading for {state.Facility.Rooms[targetRoomId].Name}."),
                out _);
        }
    }

    private static string ChooseTargetRoom(Npc npc, int minute)
    {
        if (npc.Hunger >= 55)
        {
            return "kitchen";
        }

        if (npc.Fatigue >= 70)
        {
            return "quarters";
        }

        var route = Routes[npc.Role];
        var phase = ((minute / 3) + (int)npc.Role) % route.Length;
        return route[phase];
    }

    private void HandleArrival(GameState state, Npc npc, string roomId)
    {
        if (roomId == "kitchen" && npc.Hunger >= 55)
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.Eat, roomId, "Taking a scheduled meal."),
                out _);
            return;
        }

        if (roomId == "quarters" && npc.Fatigue >= 70)
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.Rest, roomId, "Recovering in crew quarters."),
                out _);
            return;
        }

        npc.CurrentAction = new NpcAction(
            ActionKind.Investigate,
            roomId,
            $"Performing routine {npc.Role.ToString().ToLowerInvariant()} duties.");
    }

    private static IReadOnlyList<string> FindPath(
        Facility facility,
        string startRoomId,
        string targetRoomId)
    {
        if (startRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
        {
            return [startRoomId];
        }

        var queue = new Queue<string>();
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [startRoomId] = null
        };

        queue.Enqueue(startRoomId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var door in facility.Doors.Where(IsPassable))
            {
                string? neighbour = null;

                if (door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    neighbour = door.RoomBId;
                }
                else if (door.RoomBId.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    neighbour = door.RoomAId;
                }

                if (neighbour is null || previous.ContainsKey(neighbour))
                {
                    continue;
                }

                previous[neighbour] = current;

                if (neighbour.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
                {
                    return ReconstructPath(previous, targetRoomId);
                }

                queue.Enqueue(neighbour);
            }
        }

        return [];
    }

    private static bool IsPassable(Door door) =>
        door.IsPowered && door.IsOpen && !door.IsLocked;

    private static IReadOnlyList<string> ReconstructPath(
        IReadOnlyDictionary<string, string?> previous,
        string targetRoomId)
    {
        var path = new List<string>();
        string? current = targetRoomId;

        while (current is not null)
        {
            path.Add(current);
            current = previous[current];
        }

        path.Reverse();
        return path;
    }
}
