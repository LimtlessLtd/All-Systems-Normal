using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class LocalMovementSystem
{
    private const double SpeedPerMinute = 28;
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
            if (npc.Movement is { } movement)
            {
                AdvanceDoorMovement(state, npc, npc.Name, movement, maxDistance);
                continue;
            }

            var destination = GetLocalDestination(state, npc);
            MoveTowards(npc, destination.X, destination.Y, maxDistance);
        }

        foreach (var robot in state.Robots.Where(robot => !robot.IsDestroyed))
        {
            if (robot.Movement is { } movement)
            {
                AdvanceDoorMovement(state, robot, robot.Name, movement, maxDistance);
                continue;
            }

            if (!robot.IsOperational)
            {
                continue;
            }

            var destination = GetRobotDestination(state, robot);
            MoveTowards(robot, destination.X, destination.Y, maxDistance);
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

        var reachedDoor = MoveTowards(
            entity,
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

        var fromRoom = state.Facility.Rooms[movement.FromRoomId];
        var toRoom = state.Facility.Rooms[movement.ToRoomId];

        entity.CurrentRoomId = movement.ToRoomId;
        entity.PositionX = movement.EntryX;
        entity.PositionY = movement.EntryY;
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
            preferredFixture = FixtureForDevice(room, device);

            if (preferredFixture is null
                && device.Kind == StationSystemKind.Door
                && device.DoorId is { } servicedDoorId)
            {
                var servicedDoor = state.Facility.Doors.FirstOrDefault(candidate =>
                    candidate.Id.Equals(
                        servicedDoorId,
                        StringComparison.OrdinalIgnoreCase));

                if (servicedDoor is not null
                    && (servicedDoor.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                        || servicedDoor.RoomBId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    return DoorServicePoint(state, room, servicedDoor);
                }
            }
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
            return InteractionPoint(preferredFixture);
        }

        return PersonalIdlePoint(npc.Name);
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
                return InteractionPoint(fixture);
            }

            return (50, 50);
        }

        return PersonalIdlePoint(robot.Name);
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
                return FixtureForDevice(room, device);

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

    private static RoomFixture? FixtureForDevice(Room room, StationDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.FixtureLabel))
        {
            var exact = room.Fixtures.FirstOrDefault(fixture =>
                fixture.Label.Equals(
                    device.FixtureLabel,
                    StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
                return exact;
        }

        var bySystemId = room.Fixtures.FirstOrDefault(fixture =>
            fixture.SystemId?.Equals(
                device.Id,
                StringComparison.OrdinalIgnoreCase) == true);

        if (bySystemId is not null)
            return bySystemId;

        return device.Kind switch
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
            StationSystemKind.PowerDistribution =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.PowerPanel),
            StationSystemKind.CapacitorBank =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.CapacitorBank),
            StationSystemKind.CoolantPump =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.CoolantPump),
            StationSystemKind.OxygenGenerator =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.OxygenGenerator),
            StationSystemKind.CarbonScrubber =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.CarbonScrubber),
            StationSystemKind.ThermalLoop =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.ThermalLoop),
            StationSystemKind.GrowBeds =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.GrowBed),
            StationSystemKind.GalleyEquipment =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.KitchenCounter),
            StationSystemKind.AirlockMechanism =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.AirlockDoor)
                ?? room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.UtilityPanel),
            StationSystemKind.IsolationMechanism =>
                room.Fixtures.FirstOrDefault(fixture => fixture.Type == FixtureType.OverseerShutdown),
            StationSystemKind.Door =>
                room.Fixtures.FirstOrDefault(fixture =>
                    fixture.SystemId?.Equals(
                        $"door:{device.DoorId}",
                        StringComparison.OrdinalIgnoreCase) == true),
            _ =>
                room.Fixtures.FirstOrDefault(fixture =>
                    fixture.Type is FixtureType.UtilityPanel
                        or FixtureType.Console
                        or FixtureType.Workbench)
        };
    }

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

    private static (double X, double Y) DoorServicePoint(
        GameState state,
        Room room,
        Door door)
    {
        var otherRoomId = door.RoomAId.Equals(
            room.Id,
            StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId
                : door.RoomAId;
        var other = state.Facility.Rooms[otherRoomId];
        var portal = StationGeometry.FindSharedPortal(room, other);

        var localX = Math.Clamp(
            50 + (((portal.X - room.MapX) / Math.Max(room.MapWidth, 0.001)) * 100),
            8,
            92);
        var localY = Math.Clamp(
            50 + (((portal.Y - room.MapY) / Math.Max(room.MapHeight, 0.001)) * 100),
            8,
            92);

        return (localX, localY);
    }

    private static (double X, double Y) InteractionPoint(RoomFixture fixture) =>
        (
            Math.Clamp(fixture.InteractionX ?? fixture.X, 8, 92),
            Math.Clamp(fixture.InteractionY ?? fixture.Y, 8, 92));

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
        IStationMobileEntity npc,
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
