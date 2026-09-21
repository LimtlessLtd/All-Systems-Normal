using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic physical perception. Humans have a forward 180-degree field
/// of view; maintenance/security hardware has omnidirectional sensors at twice
/// the range. Rays may cross only real room geometry and explicitly open doors.
/// </summary>
public sealed class PerceptionSystem
{
    public const double HumanRange = 26;
    public const double SensorRange = HumanRange * 2;
    private const double RayStep = .45;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var observer in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            foreach (var target in state.Crew.Where(npc => npc.Id != observer.Id && npc.IsPresent))
            {
                if (!CanSee(state, observer, target))
                    continue;

                if (target.IsAlive)
                {
                    observer.LastSeenCrew[target.Id] = new CrewSighting(
                        target.Id,
                        target.Name,
                        target.CurrentRoomId,
                        state.Elapsed);
                }
                else
                {
                    observer.DiscoveredBodies.Add(target.Id);
                }
            }
        }
    }

    public static bool CanSee(GameState state, Npc observer, Npc target) =>
        target.IsPresent
        && CanSeePoint(
            state,
            observer.CurrentRoomId,
            observer.PositionX,
            observer.PositionY,
            observer.FacingDegrees,
            HumanRange,
            forwardCone: true,
            target.CurrentRoomId,
            target.PositionX,
            target.PositionY);

    public static bool CanSee(GameState state, StationRobot observer, Npc target) =>
        observer.IsOperational
        && target.IsPresent
        && CanSeePoint(
            state,
            observer.CurrentRoomId,
            observer.PositionX,
            observer.PositionY,
            observer.FacingDegrees,
            SensorRange,
            forwardCone: false,
            target.CurrentRoomId,
            target.PositionX,
            target.PositionY);

    public static bool CanSee(GameState state, SecurityTurret observer, Npc target) =>
        !observer.IsDestroyed
        && target.IsPresent
        && CanSeePoint(
            state,
            observer.RoomId,
            observer.PositionX,
            observer.PositionY,
            0,
            SensorRange,
            forwardCone: false,
            target.CurrentRoomId,
            target.PositionX,
            target.PositionY);

    public static bool CanSeeBlood(GameState state, Npc observer, BloodEvidence evidence) =>
        CanSeePoint(
            state,
            observer.CurrentRoomId,
            observer.PositionX,
            observer.PositionY,
            observer.FacingDegrees,
            HumanRange,
            forwardCone: true,
            evidence.RoomId,
            evidence.X,
            evidence.Y);

    private static bool CanSeePoint(
        GameState state,
        string observerRoomId,
        double observerLocalX,
        double observerLocalY,
        double facingDegrees,
        double range,
        bool forwardCone,
        string targetRoomId,
        double targetLocalX,
        double targetLocalY)
    {
        if (!state.Facility.Rooms.TryGetValue(observerRoomId, out var observerRoom)
            || !state.Facility.Rooms.TryGetValue(targetRoomId, out var targetRoom))
            return false;

        var start = ToMap(observerRoom, observerLocalX, observerLocalY);
        var end = ToMap(targetRoom, targetLocalX, targetLocalY);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance > range)
            return false;

        if (forwardCone && distance > .05)
        {
            var targetAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
            var difference = Math.Abs(NormalizeDegrees(targetAngle - facingDegrees));
            if (difference > 90)
                return false;
        }

        return HasClearRay(state.Facility, observerRoomId, targetRoomId, start, end);
    }

    private static bool HasClearRay(
        Facility facility,
        string observerRoomId,
        string targetRoomId,
        (double X, double Y) start,
        (double X, double Y) end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var steps = Math.Max(1, (int)Math.Ceiling(length / RayStep));
        var currentRoomId = observerRoomId;

        for (var index = 1; index <= steps; index++)
        {
            var t = index / (double)steps;
            var x = start.X + (dx * t);
            var y = start.Y + (dy * t);
            var nextRoom = FindContainingRoom(facility, x, y, currentRoomId);

            if (nextRoom is null)
                return false;

            if (nextRoom.Id.Equals(currentRoomId, StringComparison.OrdinalIgnoreCase))
                continue;

            var door = facility.FindDoorBetween(currentRoomId, nextRoom.Id);
            if (door is null || !door.IsOpen || door.HasPhysicalSecuring)
                return false;

            currentRoomId = nextRoom.Id;
        }

        return currentRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase)
            || StationGeometry.Contains(facility.Rooms[targetRoomId], end.X, end.Y, .05);
    }

    private static Room? FindContainingRoom(
        Facility facility,
        double x,
        double y,
        string preferredRoomId)
    {
        if (facility.Rooms.TryGetValue(preferredRoomId, out var preferred)
            && StationGeometry.Contains(preferred, x, y, .05))
            return preferred;

        return facility.Rooms.Values
            .Where(room => StationGeometry.Contains(room, x, y, .05))
            .OrderBy(room => room.MapWidth * room.MapHeight)
            .ThenBy(room => room.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static (double X, double Y) ToMap(Room room, double localX, double localY) =>
        (
            room.MapX + (((localX - 50) / 100) * room.MapWidth),
            room.MapY + (((localY - 50) / 100) * room.MapHeight));

    private static double NormalizeDegrees(double value)
    {
        while (value > 180) value -= 360;
        while (value < -180) value += 360;
        return value;
    }
}
