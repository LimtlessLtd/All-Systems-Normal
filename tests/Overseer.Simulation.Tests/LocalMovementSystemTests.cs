using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class LocalMovementSystemTests
{
    [Fact]
    public void Tick_MovesCrewTowardTheNetworkEndOfAHallwayBeforeCrossing()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 31415);
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = NetworkDoorForHall(state, "hall-airlock", "airlock");
        var networkRoomId = OtherSide(door, "hall-airlock");

        marcus.CurrentRoomId = networkRoomId;
        marcus.PositionX = 50;
        marcus.PositionY = 50;

        Assert.True(new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-airlock",
                "Heading toward the airlock."),
            out _));

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromSeconds(3));

        Assert.Equal(networkRoomId, marcus.CurrentRoomId);
        Assert.NotNull(marcus.Movement);
        Assert.True(
            Math.Abs(marcus.PositionX - 50) > 0.001
            || Math.Abs(marcus.PositionY - 50) > 0.001);
    }

    [Fact]
    public void Tick_RechecksHallwayDoorAtThresholdAndStopsIfPlayerSealsIt()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 27182);
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = NetworkDoorForHall(state, "hall-airlock", "airlock");
        var networkRoomId = OtherSide(door, "hall-airlock");

        marcus.CurrentRoomId = networkRoomId;
        marcus.PositionX = 50;
        marcus.PositionY = 50;

        Assert.True(new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-airlock",
                "Heading toward the airlock."),
            out _));

        var movement = Assert.IsType<NpcMovement>(marcus.Movement);
        marcus.PositionX = movement.ExitX;
        marcus.PositionY = movement.ExitY;

        door.IsOpen = false;
        door.IsLocked = true;

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        Assert.Equal(networkRoomId, marcus.CurrentRoomId);
        Assert.Null(marcus.Movement);
        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Contains(
            "sealed",
            marcus.CurrentAction.Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "cannot cross",
            state.EventLog[0],
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryDoorCrossing_UsesTheSameGlobalPortalOnBothSides()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 16180);
        var npc = state.Crew.Single(candidate => candidate.Name == "Marcus Reed");
        var resolver = new ActionResolver();

        foreach (var door in state.Facility.Doors)
        {
            door.IsPowered = true;
            door.IsLocked = false;
            door.IsOpen = true;

            npc.CurrentRoomId = door.RoomAId;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.Movement = null;
            npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Test reset.");

            var applied = resolver.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    door.RoomBId,
                    "Portal continuity test."),
                out var message);

            Assert.True(applied, message);
            var movement = Assert.IsType<NpcMovement>(npc.Movement);

            var from = state.Facility.Rooms[movement.FromRoomId];
            var to = state.Facility.Rooms[movement.ToRoomId];

            var exitGlobal = ToGlobal(from, movement.ExitX, movement.ExitY);
            var entryGlobal = ToGlobal(to, movement.EntryX, movement.EntryY);

            Assert.InRange(Math.Abs(exitGlobal.X - entryGlobal.X), 0, 0.001);
            Assert.InRange(Math.Abs(exitGlobal.Y - entryGlobal.Y), 0, 0.001);
        }
    }

    [Fact]
    public void ConnectorHallways_AreStraightOrthogonalBranchesOffTheirGeneratedNetwork()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 14142);

        foreach (var hallway in state.Facility.Rooms.Values
                     .Where(room => room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase)))
        {
            var connectedDoors = state.Facility.Doors
                .Where(door => door.RoomAId == hallway.Id || door.RoomBId == hallway.Id)
                .ToList();

            Assert.Equal(2, connectedDoors.Count);

            var neighbours = connectedDoors
                .Select(door => state.Facility.Rooms[OtherSide(door, hallway.Id)])
                .ToList();
            var functionalRoom = Assert.Single(neighbours, room => room.Type != RoomType.Corridor);
            var networkRoom = Assert.Single(neighbours, room => room.Type == RoomType.Corridor);

            var roomPortal = StationGeometry.FindSharedPortal(hallway, functionalRoom);
            var networkPortal = StationGeometry.FindSharedPortal(hallway, networkRoom);
            var vertical = roomPortal.Wall == StationWall.Horizontal;

            if (vertical)
            {
                Assert.Equal(functionalRoom.MapX, hallway.MapX, 6);
                Assert.Equal(StationWall.Horizontal, networkPortal.Wall);
                Assert.True(hallway.MapHeight >= hallway.MapWidth);
            }
            else
            {
                Assert.Equal(functionalRoom.MapY, hallway.MapY, 6);
                Assert.Equal(StationWall.Vertical, networkPortal.Wall);
                Assert.True(hallway.MapWidth >= hallway.MapHeight);
            }
        }
    }

    private static Door NetworkDoorForHall(
        GameState state,
        string hallwayId,
        string functionalRoomId) =>
        state.Facility.Doors.Single(door =>
            (door.RoomAId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase)
             || door.RoomBId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase))
            && !door.Connects(functionalRoomId, hallwayId));

    private static string OtherSide(Door door, string roomId) =>
        door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
            ? door.RoomBId
            : door.RoomAId;

    private static (double X, double Y) ToGlobal(
        Room room,
        double localX,
        double localY) =>
        (
            room.MapX + (((localX - 50) / 100) * room.MapWidth),
            room.MapY + (((localY - 50) / 100) * room.MapHeight)
        );
}
