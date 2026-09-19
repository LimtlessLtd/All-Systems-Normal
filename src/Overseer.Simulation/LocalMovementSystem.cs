using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class LocalMovementSystem
{
    private const double SpeedPerMinute = 28;

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
            if (npc.Movement is { } movement)
            {
                AdvanceDoorMovement(state, npc, movement, maxDistance);
                continue;
            }

            var destination = GetLocalDestination(state, npc);
            MoveTowards(npc, destination.X, destination.Y, maxDistance);
        }
    }

    private static void AdvanceDoorMovement(
        GameState state,
        Npc npc,
        NpcMovement movement,
        double maxDistance)
    {
        if (!npc.CurrentRoomId.Equals(
                movement.FromRoomId,
                StringComparison.OrdinalIgnoreCase))
        {
            npc.Movement = null;
            return;
        }

        var reachedDoor = MoveTowards(
            npc,
            movement.ExitX,
            movement.ExitY,
            maxDistance);

        if (!reachedDoor)
        {
            return;
        }

        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(movement.DoorId, StringComparison.OrdinalIgnoreCase));

        if (door is null
            || !door.Connects(movement.FromRoomId, movement.ToRoomId)
            || !door.IsPassable)
        {
            npc.Movement = null;
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                movement.ToRoomId,
                $"Reached {movement.DoorId}, but it is now sealed.");

            Log(
                state,
                $"{npc.Name} reaches {movement.DoorId} but cannot cross because it is sealed.");
            return;
        }

        var fromRoom = state.Facility.Rooms[movement.FromRoomId];
        var toRoom = state.Facility.Rooms[movement.ToRoomId];

        npc.CurrentRoomId = movement.ToRoomId;
        npc.PositionX = movement.EntryX;
        npc.PositionY = movement.EntryY;
        npc.Movement = null;

        Log(
            state,
            $"{npc.Name} crosses {door.Id} from {fromRoom.Name} to {toRoom.Name}.");
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

        var preferredFixture = npc.CurrentAction.Kind switch
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
            return (
                Math.Clamp(preferredFixture.X, 8, 92),
                Math.Clamp(preferredFixture.Y, 8, 92));
        }

        return PersonalIdlePoint(npc.Name);
    }

    private static (double X, double Y) PersonalIdlePoint(string name)
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

            return slots[hash % (uint)slots.Length];
        }
    }

    private static bool MoveTowards(
        Npc npc,
        double targetX,
        double targetY,
        double maxDistance)
    {
        var dx = targetX - npc.PositionX;
        var dy = targetY - npc.PositionY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance <= maxDistance || distance <= 0.001)
        {
            npc.PositionX = targetX;
            npc.PositionY = targetY;
            return true;
        }

        var scale = maxDistance / distance;
        npc.PositionX += dx * scale;
        npc.PositionY += dy * scale;
        return false;
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
        var portal = FindSharedPortal(fromRoom, toRoom);

        return new NpcMovement(
            door.Id,
            fromRoom.Id,
            toRoom.Id,
            ToLocalX(fromRoom, portal.X),
            ToLocalY(fromRoom, portal.Y),
            ToLocalX(toRoom, portal.X),
            ToLocalY(toRoom, portal.Y));
    }

    private static (double X, double Y) FindSharedPortal(
        Room fromRoom,
        Room toRoom)
    {
        var from = Bounds(fromRoom);
        var to = Bounds(toRoom);

        var horizontalOverlapStart = Math.Max(from.Left, to.Left);
        var horizontalOverlapEnd = Math.Min(from.Right, to.Right);
        var horizontalOverlap = horizontalOverlapEnd - horizontalOverlapStart;

        var verticalOverlapStart = Math.Max(from.Top, to.Top);
        var verticalOverlapEnd = Math.Min(from.Bottom, to.Bottom);
        var verticalOverlap = verticalOverlapEnd - verticalOverlapStart;

        var movingRight = toRoom.MapX >= fromRoom.MapX;
        var horizontalEdgeDistance = movingRight
            ? Math.Abs(from.Right - to.Left)
            : Math.Abs(from.Left - to.Right);

        var movingDown = toRoom.MapY >= fromRoom.MapY;
        var verticalEdgeDistance = movingDown
            ? Math.Abs(from.Bottom - to.Top)
            : Math.Abs(from.Top - to.Bottom);

        var canMeetOnVerticalWall = verticalOverlap > PortalTolerance;
        var canMeetOnHorizontalWall = horizontalOverlap > PortalTolerance;

        if (canMeetOnVerticalWall
            && (!canMeetOnHorizontalWall
                || horizontalEdgeDistance <= verticalEdgeDistance))
        {
            var x = movingRight
                ? (from.Right + to.Left) / 2
                : (from.Left + to.Right) / 2;
            var y = (verticalOverlapStart + verticalOverlapEnd) / 2;
            return (x, y);
        }

        if (canMeetOnHorizontalWall)
        {
            var x = (horizontalOverlapStart + horizontalOverlapEnd) / 2;
            var y = movingDown
                ? (from.Bottom + to.Top) / 2
                : (from.Top + to.Bottom) / 2;
            return (x, y);
        }

        throw new InvalidOperationException(
            $"Rooms '{fromRoom.Id}' and '{toRoom.Id}' do not share a physical corridor edge.");
    }

    private static (double Left, double Right, double Top, double Bottom) Bounds(Room room) =>
        (
            room.MapX - (room.MapWidth / 2),
            room.MapX + (room.MapWidth / 2),
            room.MapY - (room.MapHeight / 2),
            room.MapY + (room.MapHeight / 2)
        );

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
