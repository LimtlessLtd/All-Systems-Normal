using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class LocalMovementSystemTests
{
    [Fact]
    public void Tick_MovesCrewTowardTheCorridorEndOfAHallwayBeforeCrossing()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-airlock",
                "Heading toward the airlock."),
            out _);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        Assert.Equal("corridor", marcus.CurrentRoomId);
        Assert.NotNull(marcus.Movement);
        Assert.True(marcus.PositionX < 50);
    }

    [Fact]
    public void Tick_RechecksHallwayDoorAtThresholdAndStopsIfPlayerSealsIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = state.Facility.FindDoorBetween("corridor", "hall-airlock")!;

        new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-airlock",
                "Heading toward the airlock."),
            out _);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        door.IsOpen = false;
        door.IsLocked = true;

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        Assert.Equal("corridor", marcus.CurrentRoomId);
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
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew.Single(candidate => candidate.Name == "Marcus Reed");
        var resolver = new ActionResolver();

        foreach (var door in state.Facility.Doors)
        {
            // This test verifies portal continuity, not the station's initial
            // hatch policy. The Airlock inner hatch intentionally starts closed.
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
    public void ConnectorHallways_AreStraightOrthogonalBranchesOffTheMainCorridor()
    {
        var state = FacilitySeeder.CreateDefault();
        var corridor = state.Facility.Rooms["corridor"];

        foreach (var hallway in state.Facility.Rooms.Values
                     .Where(room => room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase)))
        {
            var connectedDoors = state.Facility.Doors
                .Where(door => door.RoomAId == hallway.Id || door.RoomBId == hallway.Id)
                .ToList();

            Assert.Equal(2, connectedDoors.Count);

            var otherRoomIds = connectedDoors
                .Select(door => door.RoomAId == hallway.Id ? door.RoomBId : door.RoomAId)
                .ToList();

            Assert.Contains("corridor", otherRoomIds);

            var functionalRoomId = Assert.Single(
                otherRoomIds,
                id => !id.Equals("corridor", StringComparison.OrdinalIgnoreCase));
            var functionalRoom = state.Facility.Rooms[functionalRoomId];

            var roomPortal = StationGeometry.FindSharedPortal(
                hallway,
                functionalRoom);
            var vertical = roomPortal.Wall == StationWall.Horizontal;

            if (vertical)
            {
                Assert.Equal(functionalRoom.MapX, hallway.MapX, 6);
                Assert.InRange(
                    hallway.MapX,
                    corridor.MapX - corridor.MapWidth / 2,
                    corridor.MapX + corridor.MapWidth / 2);
            }
            else
            {
                Assert.Equal(functionalRoom.MapY, hallway.MapY, 6);
                Assert.InRange(
                    hallway.MapY,
                    corridor.MapY - corridor.MapHeight / 2,
                    corridor.MapY + corridor.MapHeight / 2);
            }
        }
    }

    private static (double X, double Y) ToGlobal(
        Room room,
        double localX,
        double localY) =>
        (
            room.MapX + (((localX - 50) / 100) * room.MapWidth),
            room.MapY + (((localY - 50) / 100) * room.MapHeight)
        );

}
