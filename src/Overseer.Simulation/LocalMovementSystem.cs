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
    public static NpcMovement CreateOrder(
        Door door,
        Room fromRoom,
        Room toRoom)
    {
        var dx = toRoom.MapX - fromRoom.MapX;
        var dy = toRoom.MapY - fromRoom.MapY;

        double exitX;
        double exitY;
        double entryX;
        double entryY;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            var movingRight = dx >= 0;
            exitX = movingRight ? 94 : 6;
            exitY = 50;
            entryX = movingRight ? 6 : 94;
            entryY = 50;
        }
        else
        {
            var movingDown = dy >= 0;
            exitX = 50;
            exitY = movingDown ? 94 : 6;
            entryX = 50;
            entryY = movingDown ? 6 : 94;
        }

        return new NpcMovement(
            door.Id,
            fromRoom.Id,
            toRoom.Id,
            exitX,
            exitY,
            entryX,
            entryY);
    }
}
