using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class NavigationSystem
{
    public IReadOnlyList<string> FindPath(
        Facility facility,
        string startRoomId,
        string targetRoomId)
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

        var queue = new Queue<string>();
        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [startRoomId] = null
        };

        queue.Enqueue(startRoomId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var door in facility.Doors.Where(door => door.IsPassable))
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
                    return Reconstruct(previous, targetRoomId);
                }

                queue.Enqueue(neighbour);
            }
        }

        return [];
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
