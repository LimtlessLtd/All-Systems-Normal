using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class LocalMovementSystem
{
    private const double SpeedPerMinute = 32;
    private const double DoorApproachMultiplier = 1.35;
    private const double FixtureClearance = 1.8;
    private const double WaypointMargin = 1.1;
    private readonly CrewDoorInteractionSystem _crewDoors = new();

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (delta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "Movement time must move forward.");
        }

        var maxDistance = SpeedPerMinute * delta.TotalMinutes;

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive))
        {
            npc.IsLocallyMoving = false;
            if (npc.Movement is { } movement)
            {
                AdvanceDoorMovement(state, npc, npc.Name, movement, maxDistance);
                continue;
            }

            var room = state.Facility.Rooms[npc.CurrentRoomId];
            var destination = GetLocalDestination(state, npc);
            MoveTowards(room, npc, destination.X, destination.Y, maxDistance);
        }

        foreach (var robot in state.Robots.Where(robot => !robot.IsDestroyed))
        {
            robot.IsLocallyMoving = false;
            if (robot.Movement is { } movement)
            {
                AdvanceDoorMovement(state, robot, robot.Name, movement, maxDistance);
                continue;
            }

            if (!robot.IsOperational)
            {
                continue;
            }

            var room = state.Facility.Rooms[robot.CurrentRoomId];
            var destination = GetRobotDestination(state, robot);
            MoveTowards(room, robot, destination.X, destination.Y, maxDistance);
        }
    }

    private void AdvanceDoorMovement(
        GameState state,
        IStationMobileEntity entity,
        string displayName,
        NpcMovement movement,
        double maxDistance)
    {
        if (!entity.CurrentRoomId.Equals(
                movement.FromRoomId,
                StringComparison.OrdinalIgnoreCase))
        {
            entity.Movement = null;
            return;
        }

        var fromRoom = state.Facility.Rooms[movement.FromRoomId];
        var reachedDoor = MoveTowards(
            fromRoom,
            entity,
            movement.ExitX,
            movement.ExitY,
            maxDistance * DoorApproachMultiplier);

        if (!reachedDoor)
        {
            return;
        }

        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(movement.DoorId, StringComparison.OrdinalIgnoreCase));

        if (door is null
            || !door.Connects(movement.FromRoomId, movement.ToRoomId))
        {
            BlockAtDoor(state, entity, displayName, movement);
            return;
        }

        if (!door.IsPassable
            && entity is Npc crew
            && CrewDoorInteractionSystem.CanOpenForTraversal(state, crew, door))
        {
            _crewDoors.TryOpenForTraversal(state, crew, door, out _);
        }

        // Revalidate the live authoritative door state at the actual crossing.
        // A lock, weld, barricade, power loss or airlock interlock can still
        // stop somebody even if route planning originally allowed the trip.
        if (!door.IsPassable)
        {
            BlockAtDoor(state, entity, displayName, movement);
            return;
        }

        var toRoom = state.Facility.Rooms[movement.ToRoomId];

        entity.CurrentRoomId = movement.ToRoomId;
        entity.PositionX = Math.Clamp(
            movement.EntryX + (Math.Sign(50 - movement.EntryX) * 4),
            2,
            98);
        entity.PositionY = Math.Clamp(
            movement.EntryY + (Math.Sign(50 - movement.EntryY) * 4),
            2,
            98);
        entity.Movement = null;

        Log(
            state,
            $"{displayName} crosses {door.Id} from {fromRoom.Name} to {toRoom.Name}.");
    }


    private static void BlockAtDoor(
        GameState state,
        IStationMobileEntity entity,
        string displayName,
        NpcMovement movement)
    {
        entity.Movement = null;

        if (entity is Npc npc)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                movement.ToRoomId,
                $"Reached {movement.DoorId}, but it is now sealed.");
            npc.NeedsMindReconsideration = true;
        }
        else if (entity is StationRobot robot)
        {
            robot.CurrentTask = $"Route blocked at {movement.DoorId}.";
        }

        Log(
            state,
            $"{displayName} reaches {movement.DoorId} but cannot cross because it is sealed.");
    }

    private static (double X, double Y) GetLocalDestination(
        GameState state,
        Npc npc)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        if (npc.CurrentAction.Kind is ActionKind.Talk
            or ActionKind.Socialize
            or ActionKind.Argue
            or ActionKind.Attack
            or ActionKind.RequestHelp
            or ActionKind.Intimacy)
        {
            var target = state.Crew.FirstOrDefault(other =>
                other.IsAlive
                && other.CurrentRoomId.Equals(
                    npc.CurrentRoomId,
                    StringComparison.OrdinalIgnoreCase)
                && other.Name.Equals(
                    npc.CurrentAction.TargetId,
                    StringComparison.OrdinalIgnoreCase));

            if (target is not null)
            {
                return (
                    Math.Clamp(target.PositionX + 8, 8, 92),
                    Math.Clamp(target.PositionY + 6, 8, 92));
            }
        }

        RoomFixture? preferredFixture = null;

        if (npc.ServicingDeviceId is { } deviceId
            && state.Devices.TryGetValue(deviceId, out var device)
            && device.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            preferredFixture = FixtureForDevice(room, device.Kind);
        }

        if (preferredFixture is null && npc.ProvisioningJob is { } provisioning)
        {
            preferredFixture = provisioning switch
            {
                ActionKind.Cook => room.Fixtures.FirstOrDefault(fixture =>
                    fixture.Type is FixtureType.KitchenCounter or FixtureType.Sink),
                ActionKind.TendCrops or ActionKind.Harvest => FixtureForCropBed(room, npc.TendingBedId),
                _ => null
            };
        }

        preferredFixture ??= npc.CurrentAction.Kind switch
        {
            ActionKind.Rest or ActionKind.Sleep or ActionKind.Intimacy =>
                room.Fixtures.FirstOrDefault(fixture =>
                    fixture.Type is FixtureType.Bed or FixtureType.MedicalBed),

            ActionKind.Eat => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.Table or FixtureType.KitchenCounter),

            ActionKind.Recreate => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.Sofa
                    or FixtureType.RecreationConsole
                    or FixtureType.Table),

            ActionKind.Groom => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.Mirror or FixtureType.Sink),

            ActionKind.Shower => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type == FixtureType.Shower),

            ActionKind.UseToilet => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type == FixtureType.Toilet),

            ActionKind.Work or ActionKind.Repair => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.Workbench
                    or FixtureType.Console
                    or FixtureType.Generator
                    or FixtureType.ReactorCore
                    or FixtureType.MedicalBed
                    or FixtureType.StorageRack),

            ActionKind.ShutdownOverseer => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type == FixtureType.OverseerShutdown),

            ActionKind.Investigate => room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.Console
                    or FixtureType.Workbench
                    or FixtureType.StorageRack),

            _ => null
        };

        if (preferredFixture is not null)
        {
            return InteractionPoint(room, preferredFixture);
        }

        return PersonalIdlePoint(room, npc.Name);
    }

    private static (double X, double Y) GetRobotDestination(
        GameState state,
        StationRobot robot)
    {
        var room = state.Facility.Rooms[robot.CurrentRoomId];

        if (robot.TargetNpcId is { } targetId)
        {
            var target = state.Crew.FirstOrDefault(npc =>
                npc.Id == targetId
                && npc.IsAlive
                && npc.IsPresent
                && npc.CurrentRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

            if (target is not null)
            {
                return (
                    Math.Clamp(target.PositionX + 7, 8, 92),
                    Math.Clamp(target.PositionY + 5, 8, 92));
            }
        }

        if (robot.TargetRoomId is not null
            && robot.TargetRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            var fixture = RobotInteractionFixture(state, room, robot);
            if (fixture is not null)
            {
                return InteractionPoint(room, fixture);
            }

            return (50, 50);
        }

        return PersonalIdlePoint(room, robot.Name);
    }

    private static RoomFixture? RobotInteractionFixture(
        GameState state,
        Room room,
        StationRobot robot)
    {
        if (robot.CurrentTask.Contains("Charging", StringComparison.OrdinalIgnoreCase))
        {
            return room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.UtilityPanel
                    or FixtureType.Console
                    or FixtureType.Workbench);
        }

        if (robot.ActionCompletesAt is not null
            || robot.CurrentTask.Contains("repair", StringComparison.OrdinalIgnoreCase)
            || robot.CurrentTask.Contains("restor", StringComparison.OrdinalIgnoreCase))
        {
            if (!room.CameraOnline)
                return room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.Camera);

            if (!room.VentilationEnabled)
                return room.Fixtures.FirstOrDefault(fixture =>
                    fixture.Type is FixtureType.Vent or FixtureType.UtilityPanel);

            var device = state.Devices.Values
                .Where(candidate =>
                    candidate.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Condition)
                .FirstOrDefault();

            if (device is not null)
                return FixtureForDevice(room, device.Kind);

            return room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.UtilityPanel
                    or FixtureType.Console
                    or FixtureType.Workbench);
        }

        if (robot.CurrentTask.Contains("inspection", StringComparison.OrdinalIgnoreCase)
            || robot.CurrentTask.Contains("patrol", StringComparison.OrdinalIgnoreCase))
        {
            return room.Fixtures.FirstOrDefault(fixture =>
                fixture.Type is FixtureType.Camera
                    or FixtureType.UtilityPanel
                    or FixtureType.Console
                    or FixtureType.StorageRack);
        }

        return null;
    }

    private static RoomFixture? FixtureForDevice(Room room, StationSystemKind kind) =>
        kind switch
        {
            StationSystemKind.Camera =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.Camera),
            StationSystemKind.Ventilation =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.Vent)
                ?? room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.UtilityPanel),
            StationSystemKind.PowerGenerator =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.Generator),
            StationSystemKind.Reactor =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.ReactorCore),
            StationSystemKind.GrowBeds =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.GrowBed),
            StationSystemKind.GalleyEquipment =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.KitchenCounter),
            StationSystemKind.AirlockMechanism =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.AirlockDoor)
                ?? room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.UtilityPanel),
            StationSystemKind.IsolationMechanism =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.OverseerShutdown),
            _ =>
                room.Fixtures.FirstOrDefault(fixture =>
                    fixture.Type is FixtureType.UtilityPanel
                        or FixtureType.Console
                        or FixtureType.Workbench)
        };

    private static RoomFixture? FixtureForCropBed(Room room, string? bedId)
    {
        var beds = room.Fixtures
            .Where(fixture => fixture.Type == FixtureType.GrowBed)
            .ToList();

        if (beds.Count == 0)
            return null;

        var suffix = bedId?.Split(':').LastOrDefault();
        if (int.TryParse(suffix, out var index)
            && index >= 1
            && index <= beds.Count)
        {
            return beds[index - 1];
        }

        return beds[0];
    }

    private static (double X, double Y) InteractionPoint(Room room, RoomFixture fixture)
    {
        if (fixture.InteractionX is { } interactionX
            && fixture.InteractionY is { } interactionY)
        {
            return FindWalkablePoint(room, interactionX, interactionY);
        }

        var candidates = new[]
        {
            (X: fixture.X, Y: fixture.Y + (fixture.Height / 2) + 5),
            (X: fixture.X, Y: fixture.Y - (fixture.Height / 2) - 5),
            (X: fixture.X + (fixture.Width / 2) + 5, Y: fixture.Y),
            (X: fixture.X - (fixture.Width / 2) - 5, Y: fixture.Y)
        };

        return candidates
            .Select(point => FindWalkablePoint(room, point.X, point.Y))
            .OrderBy(point => Distance(point.X, point.Y, fixture.X, fixture.Y))
            .First();
    }

    private static (double X, double Y) PersonalIdlePoint(Room room, string name)
    {
        var slots = new (double X, double Y)[]
        {
            (32, 34),
            (50, 32),
            (68, 34),
            (34, 66),
            (50, 68),
            (66, 66)
        };

        unchecked
        {
            uint hash = 2166136261;

            foreach (var ch in name)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            var point = slots[hash % (uint)slots.Length];
            return FindWalkablePoint(room, point.X, point.Y);
        }
    }

    private static bool MoveTowards(
        Room room,
        IStationMobileEntity entity,
        double targetX,
        double targetY,
        double maxDistance)
    {
        var destination = FindWalkablePoint(room, targetX, targetY);
        var dx = destination.X - entity.PositionX;
        var dy = destination.Y - entity.PositionY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        SetFacing(entity, dx, dy);

        if (distance <= maxDistance || distance <= 0.001)
        {
            if (distance > .05)
                MarkLocallyMoving(entity);
            entity.PositionX = destination.X;
            entity.PositionY = destination.Y;
            return true;
        }

        var scale = maxDistance / distance;
        var nextX = entity.PositionX + (dx * scale);
        var nextY = entity.PositionY + (dy * scale);

        if (SegmentHitsFixture(room, entity.PositionX, entity.PositionY, nextX, nextY))
        {
            var detour = DetourPoint(room, entity.PositionX, entity.PositionY, destination.X, destination.Y);
            var detourDx = detour.X - entity.PositionX;
            var detourDy = detour.Y - entity.PositionY;
            var detourDistance = Math.Sqrt((detourDx * detourDx) + (detourDy * detourDy));

            if (detourDistance <= 0.001)
                return false;

            var detourScale = Math.Min(1, maxDistance / detourDistance);
            nextX = entity.PositionX + (detourDx * detourScale);
            nextY = entity.PositionY + (detourDy * detourScale);
            SetFacing(entity, detourDx, detourDy);

            if (SegmentHitsFixture(room, entity.PositionX, entity.PositionY, nextX, nextY))
                return false;
        }

        MarkLocallyMoving(entity);
        entity.PositionX = Math.Clamp(nextX, 2, 98);
        entity.PositionY = Math.Clamp(nextY, 2, 98);
        return false;
    }

    private static void MarkLocallyMoving(IStationMobileEntity entity)
    {
        if (entity is Npc npc)
            npc.IsLocallyMoving = true;
        else if (entity is StationRobot robot)
            robot.IsLocallyMoving = true;
    }

    internal static bool IsWalkable(Room room, double x, double y) =>
        x >= 2 && x <= 98 && y >= 2 && y <= 98
        && !room.Fixtures.Any(fixture =>
            IsCollisionFixture(fixture)
            && PointInside(fixture, x, y, FixtureClearance));

    private static (double X, double Y) FindWalkablePoint(Room room, double x, double y)
    {
        x = Math.Clamp(x, 4, 96);
        y = Math.Clamp(y, 4, 96);

        if (IsWalkable(room, x, y))
            return (x, y);

        for (var radius = 4d; radius <= 28; radius += 4)
        {
            var candidates = new[]
            {
                (x + radius, y), (x - radius, y), (x, y + radius), (x, y - radius),
                (x + radius, y + radius), (x - radius, y + radius),
                (x + radius, y - radius), (x - radius, y - radius)
            };

            foreach (var candidate in candidates
                         .Select(point => (
                             X: Math.Clamp(point.Item1, 4, 96),
                             Y: Math.Clamp(point.Item2, 4, 96)))
                         .OrderBy(point => Distance(point.X, point.Y, x, y)))
            {
                if (IsWalkable(room, candidate.X, candidate.Y))
                    return candidate;
            }
        }

        return (Math.Clamp(x, 4, 96), Math.Clamp(y, 4, 96));
    }

    private static (double X, double Y) DetourPoint(
        Room room,
        double startX,
        double startY,
        double targetX,
        double targetY)
    {
        if (!SegmentHitsFixture(room, startX, startY, targetX, targetY))
            return (targetX, targetY);

        // Build a tiny deterministic visibility graph from the corners of the
        // physical fixtures. This is internal navigation only: rendering stays
        // completely freeform while actors can reliably walk around machinery
        // instead of oscillating at the first blocked straight-line segment.
        var nodes = new List<(double X, double Y)> { (targetX, targetY) };

        foreach (var fixture in room.Fixtures
                     .Where(IsCollisionFixture)
                     .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
        {
            var offsetX = (fixture.Width / 2) + FixtureClearance + WaypointMargin;
            var offsetY = (fixture.Height / 2) + FixtureClearance + WaypointMargin;

            foreach (var point in new[]
                     {
                         (X: fixture.X - offsetX, Y: fixture.Y - offsetY),
                         (X: fixture.X + offsetX, Y: fixture.Y - offsetY),
                         (X: fixture.X - offsetX, Y: fixture.Y + offsetY),
                         (X: fixture.X + offsetX, Y: fixture.Y + offsetY)
                     })
            {
                var candidate = (
                    X: Math.Clamp(point.X, 3, 97),
                    Y: Math.Clamp(point.Y, 3, 97));

                if (IsWalkable(room, candidate.X, candidate.Y)
                    && !nodes.Any(existing =>
                        Distance(existing.X, existing.Y, candidate.X, candidate.Y) < .25))
                {
                    nodes.Add(candidate);
                }
            }
        }

        if (nodes.Count == 1)
            return (targetX, targetY);

        var distance = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var previous = Enumerable.Repeat(-2, nodes.Count).ToArray();
        var visited = new bool[nodes.Count];

        // The moving actor is an implicit source node. Seed every waypoint
        // directly visible from its current position.
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (SegmentHitsFixture(room, startX, startY, node.X, node.Y))
                continue;

            distance[index] = Distance(startX, startY, node.X, node.Y);
            previous[index] = -1;
        }

        while (true)
        {
            var current = -1;
            var best = double.PositiveInfinity;

            for (var index = 0; index < nodes.Count; index++)
            {
                if (!visited[index] && distance[index] < best)
                {
                    best = distance[index];
                    current = index;
                }
            }

            if (current < 0 || current == 0)
                break;

            visited[current] = true;

            for (var next = 0; next < nodes.Count; next++)
            {
                if (next == current || visited[next])
                    continue;

                var from = nodes[current];
                var to = nodes[next];

                if (SegmentHitsFixture(room, from.X, from.Y, to.X, to.Y))
                    continue;

                var proposed = distance[current]
                    + Distance(from.X, from.Y, to.X, to.Y);

                if (proposed + .001 >= distance[next])
                    continue;

                distance[next] = proposed;
                previous[next] = current;
            }
        }

        if (double.IsPositiveInfinity(distance[0]))
        {
            return nodes
                .Skip(1)
                .Where(node => !SegmentHitsFixture(room, startX, startY, node.X, node.Y))
                .OrderBy(node =>
                    Distance(startX, startY, node.X, node.Y)
                    + Distance(node.X, node.Y, targetX, targetY))
                .FirstOrDefault((targetX, targetY));
        }

        var waypoint = 0;
        while (previous[waypoint] >= 0)
            waypoint = previous[waypoint];

        return nodes[waypoint];
    }

    private static bool SegmentHitsFixture(
        Room room,
        double startX,
        double startY,
        double endX,
        double endY) =>
        room.Fixtures.Any(fixture =>
            IsCollisionFixture(fixture)
            && SegmentIntersectsInflatedFixture(
                fixture, startX, startY, endX, endY, FixtureClearance));

    private static bool SegmentIntersectsInflatedFixture(
        RoomFixture fixture,
        double startX,
        double startY,
        double endX,
        double endY,
        double clearance)
    {
        var distance = Distance(startX, startY, endX, endY);
        var steps = Math.Max(1, (int)Math.Ceiling(distance / 1.5));

        for (var index = 1; index <= steps; index++)
        {
            var t = index / (double)steps;
            var x = startX + ((endX - startX) * t);
            var y = startY + ((endY - startY) * t);
            if (PointInside(fixture, x, y, clearance))
                return true;
        }

        return false;
    }

    private static bool PointInside(RoomFixture fixture, double x, double y, double clearance) =>
        x >= fixture.X - (fixture.Width / 2) - clearance
        && x <= fixture.X + (fixture.Width / 2) + clearance
        && y >= fixture.Y - (fixture.Height / 2) - clearance
        && y <= fixture.Y + (fixture.Height / 2) + clearance;

    private static bool IsCollisionFixture(RoomFixture fixture) =>
        fixture.Type is not FixtureType.Camera
            and not FixtureType.Window
            and not FixtureType.Screen
            and not FixtureType.Mirror
            and not FixtureType.Pipe
            and not FixtureType.AirlockDoor;

    private static double Distance(double firstX, double firstY, double secondX, double secondY)
    {
        var dx = firstX - secondX;
        var dy = firstY - secondY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void SetFacing(IStationMobileEntity entity, double dx, double dy)
    {
        if (Math.Abs(dx) < .001 && Math.Abs(dy) < .001)
            return;

        var facing = Math.Atan2(dy, dx) * 180 / Math.PI;

        if (entity is Npc npc)
            npc.FacingDegrees = facing;
        else if (entity is StationRobot robot)
            robot.FacingDegrees = facing;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}

internal static class MovementGeometry
{
    private const double PortalTolerance = 0.01;

    public static NpcMovement CreateOrder(
        Door door,
        Room fromRoom,
        Room toRoom)
    {
        var portal = StationGeometry.FindSharedPortal(fromRoom, toRoom);

        return new NpcMovement(
            door.Id,
            fromRoom.Id,
            toRoom.Id,
            ToLocalX(fromRoom, portal.X),
            ToLocalY(fromRoom, portal.Y),
            ToLocalX(toRoom, portal.X),
            ToLocalY(toRoom, portal.Y));
    }

    private static double ToLocalX(Room room, double globalX) =>
        Math.Clamp(
            50 + (((globalX - room.MapX) / room.MapWidth) * 100),
            0,
            100);

    private static double ToLocalY(Room room, double globalY) =>
        Math.Clamp(
            50 + (((globalY - room.MapY) / room.MapHeight) * 100),
            0,
            100);
}
