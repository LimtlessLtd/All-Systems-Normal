using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #87: the selected-unit vision arc is drawn from the same
/// perception contract the simulation uses — forward 180-degree human cone,
/// omnidirectional robot sensors, clipped by walls and closed doors.
/// </summary>
public sealed class VisionOutlineTests
{
    [Fact]
    public void HumanOutline_StaysInsideTheForwardConeAndRange()
    {
        var (state, observer, _, _) = ObserverFacingADoor(doorOpen: true);

        var outline = PerceptionSystem.VisionOutline(state, observer);

        Assert.True(outline.Count > 10);
        var origin = outline[0];
        var facing = observer.FacingDegrees * Math.PI / 180;
        foreach (var point in outline.Skip(1))
        {
            var dx = point.X - origin.X;
            var dy = point.Y - origin.Y;
            Assert.True(Math.Sqrt((dx * dx) + (dy * dy)) <= PerceptionSystem.HumanRange + .01);
            Assert.True((dx * Math.Cos(facing)) + (dy * Math.Sin(facing)) >= -.01);
        }
    }

    [Fact]
    public void HumanOutline_IsClippedByAClosedDoorAndPassesAnOpenOne()
    {
        var (openState, openObserver, room, neighbour) = ObserverFacingADoor(doorOpen: true);
        var (closedState, closedObserver, _, _) = ObserverFacingADoor(doorOpen: false);

        var open = PerceptionSystem.VisionOutline(openState, openObserver);
        var closed = PerceptionSystem.VisionOutline(closedState, closedObserver);

        Assert.Contains(open, point =>
            StationGeometry.Contains(neighbour, point.X, point.Y, 0)
            && !StationGeometry.Contains(room, point.X, point.Y, 0));
        Assert.DoesNotContain(closed, point =>
            StationGeometry.Contains(neighbour, point.X, point.Y, 0)
            && !StationGeometry.Contains(room, point.X, point.Y, .05));
    }

    [Fact]
    public void DeadOrAbsentCrew_HaveNoOutline()
    {
        var (state, observer, _, _) = ObserverFacingADoor(doorOpen: true);
        observer.Health = 0;

        Assert.Empty(PerceptionSystem.VisionOutline(state, observer));
    }

    [Fact]
    public void RobotOutline_IsOmnidirectionalWithinSensorRange()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var robot = state.Robots.First(candidate => candidate.IsOperational);

        var outline = PerceptionSystem.VisionOutline(state, robot);

        Assert.Equal(90, outline.Count);
        var room = state.Facility.Rooms[robot.CurrentRoomId];
        var originX = room.MapX + (((robot.PositionX - 50) / 100) * room.MapWidth);
        var originY = room.MapY + (((robot.PositionY - 50) / 100) * room.MapHeight);
        Assert.All(outline, point =>
            Assert.True(Math.Sqrt(Math.Pow(point.X - originX, 2) + Math.Pow(point.Y - originY, 2))
                <= PerceptionSystem.SensorRange + .01));
    }

    private static (GameState State, Npc Observer, Room Room, Room Neighbour) ObserverFacingADoor(bool doorOpen)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var observer = state.Crew.First(npc => npc.IsAlive && npc.IsPresent);
        var door = state.Facility.Doors
            .Where(candidate => !candidate.HasPhysicalSecuring)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .First();
        var room = state.Facility.Rooms[door.RoomAId];
        var neighbour = state.Facility.Rooms[door.RoomBId];
        var portal = StationGeometry.FindSharedPortal(room, neighbour);
        door.IsOpen = doorOpen;
        room.IsPowered = true;
        room.LightsOn = true;

        // Stand a little inside the room, facing straight at the doorway.
        var standX = room.MapX + ((portal.X - room.MapX) * .7);
        var standY = room.MapY + ((portal.Y - room.MapY) * .7);
        observer.CurrentRoomId = room.Id;
        observer.PositionX = 50 + ((standX - room.MapX) / room.MapWidth * 100);
        observer.PositionY = 50 + ((standY - room.MapY) / room.MapHeight * 100);
        observer.FacingDegrees = Math.Atan2(portal.Y - standY, portal.X - standX) * 180 / Math.PI;

        return (state, observer, room, neighbour);
    }
}
