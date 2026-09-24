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

    /// <summary>
    /// Human sight in the dark: when either end of the sightline is unlit,
    /// people only make out what is close. Machine sensors do not need light.
    /// </summary>
    public const double DarkRangeFactor = 0.35;
    private const double RayStep = .45;

    public static bool IsLit(GameState state, string roomId) =>
        state.Facility.Rooms.TryGetValue(roomId, out var room)
        && room.IsPowered
        && room.LightsOn;

    private static double HumanRangeBetween(GameState state, string observerRoomId, string targetRoomId) =>
        IsLit(state, observerRoomId) && IsLit(state, targetRoomId)
            ? HumanRange
            : HumanRange * DarkRangeFactor;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var observer in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            ObserveCurrentRoomFire(state, observer);

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

    private static void ObserveCurrentRoomFire(GameState state, Npc observer)
    {
        // A fire observation is an episode, like the other entries in ObservedFaults:
        // once noticed it does not wake cognition every minute, but extinguishing it
        // clears the marker so a later re-ignition can be noticed again.
        foreach (var extinguished in state.Facility.Rooms.Values.Where(room => room.FireIntensity <= 0))
        {
            observer.ObservedFaults.Remove($"{extinguished.Id}:fire");
        }

        if (!state.Facility.Rooms.TryGetValue(observer.CurrentRoomId, out var room)
            || room.FireIntensity <= 0)
        {
            return;
        }

        var fireX = room.FireOriginX ?? 50;
        var fireY = room.FireOriginY ?? 50;

        // Fire is a salient visual/thermal source, so facing is not used as a
        // gate once it is in the same compartment; walls and human sight range
        // still apply through the ordinary ray test.
        if (!CanSeePoint(
                state,
                observer.CurrentRoomId,
                observer.PositionX,
                observer.PositionY,
                observer.FacingDegrees,
                HumanRange,
                forwardCone: false,
                room.Id,
                fireX,
                fireY))
        {
            return;
        }

        var key = $"{room.Id}:fire";
        if (!observer.ObservedFaults.Add(key))
        {
            return;
        }

        observer.Memories.Add(new Memory(
            $"I can see an active fire in {room.Name} [{room.Id}] at about {room.FireIntensity:0}% intensity.",
            state.Elapsed,
            0.9,
            ObservedFireRoomId: room.Id));
    }

    public static bool CanSee(GameState state, Npc observer, Npc target) =>
        target.IsPresent
        && CanSeePoint(
            state,
            observer.CurrentRoomId,
            observer.PositionX,
            observer.PositionY,
            observer.FacingDegrees,
            HumanRangeBetween(state, observer.CurrentRoomId, target.CurrentRoomId),
            forwardCone: true,
            target.CurrentRoomId,
            target.PositionX,
            target.PositionY);

    /// <summary>
    /// Whether someone can tell who is involved in a commotion. In the same
    /// compartment people turn toward the noise, so facing does not matter but
    /// distance and light do; across compartments it is ordinary sight.
    /// </summary>
    public static bool CanMakeOut(GameState state, Npc observer, Npc target)
    {
        if (!target.IsPresent)
            return false;

        if (!observer.CurrentRoomId.Equals(target.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
            || !state.Facility.Rooms.TryGetValue(observer.CurrentRoomId, out var room))
            return CanSee(state, observer, target);

        var from = ToMap(room, observer.PositionX, observer.PositionY);
        var to = ToMap(room, target.PositionX, target.PositionY);
        var distance = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));

        return distance <= HumanRangeBetween(state, room.Id, room.Id);
    }

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
            HumanRangeBetween(state, observer.CurrentRoomId, evidence.RoomId),
            forwardCone: true,
            evidence.RoomId,
            evidence.X,
            evidence.Y);

    /// <summary>
    /// Presentation helper (owner idea #87): the outline, in map coordinates,
    /// of where this person can currently see — their forward 180-degree cone
    /// out to lit/dark human range, clipped by the same walls and closed or
    /// secured doors <see cref="CanSee(GameState, Npc, Npc)"/> respects. Range
    /// uses the observer's own compartment lighting. The first point is the
    /// observer. Empty when they cannot see at all.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> VisionOutline(GameState state, Npc observer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observer);

        if (!observer.IsAlive || !observer.IsPresent)
            return [];

        var range = IsLit(state, observer.CurrentRoomId)
            ? HumanRange
            : HumanRange * DarkRangeFactor;

        return Outline(
            state.Facility,
            observer.CurrentRoomId,
            observer.PositionX,
            observer.PositionY,
            observer.FacingDegrees - 90,
            180,
            range);
    }

    /// <summary>
    /// Presentation helper (owner idea #87): the omnidirectional sensor
    /// outline of an operational robot, clipped like its real sightlines.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> VisionOutline(GameState state, StationRobot observer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observer);

        if (!observer.IsOperational)
            return [];

        return Outline(
            state.Facility,
            observer.CurrentRoomId,
            observer.PositionX,
            observer.PositionY,
            0,
            360,
            SensorRange);
    }

    private static IReadOnlyList<(double X, double Y)> Outline(
        Facility facility,
        string roomId,
        double localX,
        double localY,
        double startDegrees,
        double sweepDegrees,
        double range)
    {
        if (!facility.Rooms.TryGetValue(roomId, out var room))
            return [];

        const double StepDegrees = 4;
        var origin = ToMap(room, localX, localY);
        var fullCircle = sweepDegrees >= 360;
        var rays = (int)Math.Ceiling(sweepDegrees / StepDegrees);
        var points = new List<(double X, double Y)>(rays + 2);

        if (!fullCircle)
            points.Add(origin);

        for (var index = 0; index <= rays; index++)
        {
            if (fullCircle && index == rays)
                break;

            var radians = (startDegrees + (sweepDegrees * index / rays)) * Math.PI / 180;
            points.Add(RayReach(facility, roomId, origin, Math.Cos(radians), Math.Sin(radians), range));
        }

        return points;
    }

    /// <summary>
    /// Marches a sightline outward with the same step and door rules as
    /// <see cref="HasClearRay"/>, returning the farthest point still visible.
    /// </summary>
    private static (double X, double Y) RayReach(
        Facility facility,
        string roomId,
        (double X, double Y) origin,
        double directionX,
        double directionY,
        double range)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(range / RayStep));
        var currentRoomId = roomId;
        var reach = origin;

        for (var index = 1; index <= steps; index++)
        {
            var distance = range * index / steps;
            var x = origin.X + (directionX * distance);
            var y = origin.Y + (directionY * distance);
            var nextRoom = FindContainingRoom(facility, x, y, currentRoomId);

            if (nextRoom is null)
                break;

            if (!nextRoom.Id.Equals(currentRoomId, StringComparison.OrdinalIgnoreCase))
            {
                var door = facility.FindDoorBetween(currentRoomId, nextRoom.Id);
                if (door is null || !door.IsOpen || door.HasPhysicalSecuring)
                    break;

                currentRoomId = nextRoom.Id;
            }

            reach = (x, y);
        }

        return reach;
    }

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
