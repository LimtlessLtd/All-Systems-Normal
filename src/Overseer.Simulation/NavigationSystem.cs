using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class NavigationSystem
{
    public IReadOnlyList<string> FindPath(Facility facility, string startRoomId, string targetRoomId) =>
        FindPathCore(facility, startRoomId, targetRoomId, door => door.IsPassable);

    public IReadOnlyList<string> FindPathIgnoringDoorState(Facility facility, string startRoomId, string targetRoomId) =>
        FindPathCore(facility, startRoomId, targetRoomId, _ => true);

    public IReadOnlyList<string> FindPathForCrew(
        GameState state,
        Npc npc,
        string startRoomId,
        string targetRoomId) =>
        FindPathCore(
            state.Facility,
            startRoomId,
            targetRoomId,
            door => door.IsPassable
                || CrewDoorInteractionSystem.CanOpenForTraversal(state, npc, door));

    private static IReadOnlyList<string> FindPathCore(
        Facility facility,
        string startRoomId,
        string targetRoomId,
        Func<Door, bool> canTraverse)
    {
        ArgumentNullException.ThrowIfNull(facility);

        if (!facility.Rooms.ContainsKey(startRoomId)
            || !facility.Rooms.ContainsKey(targetRoomId))
        {
            return [];
        }

        if (startRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
        {
            return [startRoomId];
        }

        var frontier = new PriorityQueue<string, double>();
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [startRoomId] = null
        };
        var costSoFar = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [startRoomId] = 0
        };

        frontier.Enqueue(startRoomId, 0);

        while (frontier.TryDequeue(out var current, out _))
        {
            if (current.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
            {
                return Reconstruct(previous, targetRoomId);
            }

            foreach (var door in facility.Doors.Where(canTraverse))
            {
                var neighbour = OtherSide(door, current);

                if (neighbour is null || !facility.Rooms.ContainsKey(neighbour))
                {
                    continue;
                }

                var stepCost = Distance(
                    facility.Rooms[current],
                    facility.Rooms[neighbour]);
                var newCost = costSoFar[current] + stepCost;

                if (costSoFar.TryGetValue(neighbour, out var knownCost)
                    && newCost >= knownCost)
                {
                    continue;
                }

                costSoFar[neighbour] = newCost;
                previous[neighbour] = current;

                var priority = newCost + Distance(
                    facility.Rooms[neighbour],
                    facility.Rooms[targetRoomId]);

                frontier.Enqueue(neighbour, priority);
            }
        }

        return [];
    }

    private static string? OtherSide(Door door, string roomId)
    {
        if (door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase))
        {
            return door.RoomBId;
        }

        if (door.RoomBId.Equals(roomId, StringComparison.OrdinalIgnoreCase))
        {
            return door.RoomAId;
        }

        return null;
    }

    private static double Distance(Room first, Room second)
    {
        var dx = first.MapX - second.MapX;
        var dy = first.MapY - second.MapY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static IReadOnlyList<string> Reconstruct(
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
