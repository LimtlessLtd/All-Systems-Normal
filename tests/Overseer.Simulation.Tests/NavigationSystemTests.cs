using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class NavigationSystemTests
{
    [Fact]
    public void FindPath_CannotEnterRoomWhenItsHallwayDoorIsLocked()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 101);
        var airlockDoor = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;

        airlockDoor.IsOpen = false;
        airlockDoor.IsLocked = true;

        var path = new NavigationSystem().FindPath(
            state.Facility,
            "corridor",
            "airlock");

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_UsesRealRoomHallwayAndGeneratedCorridorGeometry()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);

        var path = new NavigationSystem().FindPath(
            state.Facility,
            "control",
            "reactor");

        Assert.NotEmpty(path);
        Assert.Equal("control", path[0]);
        Assert.Equal("hall-control", path[1]);
        Assert.Equal("hall-reactor", path[^2]);
        Assert.Equal("reactor", path[^1]);
        Assert.All(
            path.Skip(2).SkipLast(2),
            roomId => Assert.Equal(RoomType.Corridor, state.Facility.Rooms[roomId].Type));

        for (var index = 0; index < path.Count - 1; index++)
        {
            Assert.NotNull(state.Facility.FindDoorBetween(path[index], path[index + 1]));
        }
    }

    [Fact]
    public void LocalMovement_RecordsTheExactGeneratedHallwayDoorCrossed()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 303);
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var networkDoor = state.Facility.Doors.Single(door =>
            (door.RoomAId == "hall-airlock" || door.RoomBId == "hall-airlock")
            && !door.Connects("airlock", "hall-airlock"));
        var networkRoomId = networkDoor.RoomAId == "hall-airlock"
            ? networkDoor.RoomBId
            : networkDoor.RoomAId;

        marcus.CurrentRoomId = networkRoomId;
        marcus.PositionX = 50;
        marcus.PositionY = 50;

        var success = new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-airlock",
                "Heading toward the airlock."),
            out _);

        Assert.True(success);
        Assert.Equal(networkRoomId, marcus.CurrentRoomId);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(4));

        Assert.Equal("hall-airlock", marcus.CurrentRoomId);
        Assert.Contains(
            networkDoor.Id,
            state.EventLog[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "crosses",
            state.EventLog[0],
            StringComparison.OrdinalIgnoreCase);
    }
}
