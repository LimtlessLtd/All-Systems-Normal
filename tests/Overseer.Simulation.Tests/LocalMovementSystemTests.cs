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
            }
            else
            {
                Assert.Equal(functionalRoom.MapY, hallway.MapY, 6);
                Assert.Equal(StationWall.Vertical, networkPortal.Wall);
            }
        }
    }

    [Fact]
    public void MaintenanceWorkerWalksToTheActualFixtureInteractionPoint()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var npc = state.Crew.First();
        var room = state.Facility.Rooms["generator"];
        var fixture = room.Fixtures.Single(item => item.Type == FixtureType.Generator);

        npc.CurrentRoomId = room.Id;
        npc.PositionX = 10;
        npc.PositionY = 10;
        npc.ServicingDeviceId = "generator:generator";
        npc.CurrentAction = new NpcAction(ActionKind.Repair, room.Id, "Servicing generator.");

        var targetX = fixture.InteractionX ?? fixture.X;
        var targetY = fixture.InteractionY ?? fixture.Y;
        var before = Math.Sqrt(
            Math.Pow(npc.PositionX - targetX, 2)
            + Math.Pow(npc.PositionY - targetY, 2));

        new LocalMovementSystem().Tick(state, TimeSpan.FromMinutes(1));

        var after = Math.Sqrt(
            Math.Pow(npc.PositionX - targetX, 2)
            + Math.Pow(npc.PositionY - targetY, 2));

        Assert.True(
            after < before,
            $"worker={npc.PositionX:0.0},{npc.PositionY:0.0}; target={targetX:0.0},{targetY:0.0}; " +
            $"before={before:0.0}; after={after:0.0}; fixtures=" +
            string.Join(" | ", room.Fixtures.Select(item =>
                $"{item.Type}:{item.Label}@{item.X:0.0},{item.Y:0.0}/{item.Width:0.0}x{item.Height:0.0} " +
                $"use={item.InteractionX:0.0},{item.InteractionY:0.0}")));
    }

    [Fact]
    public void FriendlyRobotWalksToMachineryWhileRepairingIt()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 61616);
        var robot = Assert.Single(state.Robots);
        var room = state.Facility.Rooms["generator"];
        var fixture = room.Fixtures.Single(item => item.Type == FixtureType.Generator);

        robot.CurrentRoomId = room.Id;
        robot.PositionX = 10;
        robot.PositionY = 10;
        room.IsPowered = false;

        var robots = new RobotSystem();
        robots.Tick(state, TimeSpan.FromMinutes(1));
        Assert.NotNull(robot.ActionCompletesAt);

        var targetX = fixture.InteractionX ?? fixture.X;
        var targetY = fixture.InteractionY ?? fixture.Y;
        var before = Math.Sqrt(
            Math.Pow(robot.PositionX - targetX, 2)
            + Math.Pow(robot.PositionY - targetY, 2));

        new LocalMovementSystem().Tick(state, TimeSpan.FromMinutes(1));

        var after = Math.Sqrt(
            Math.Pow(robot.PositionX - targetX, 2)
            + Math.Pow(robot.PositionY - targetY, 2));

        Assert.True(after < before);
    }

    [Fact]
    public void LocalMovement_DoesNotSnapThroughAFixtureWhenTargetFitsInsideOneTick()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var npc = state.Crew.First();
        var room = state.Facility.Rooms["hydroponics"];
        var bed = state.CropBeds.First();
        var targetFixture = room.Fixtures.Single(fixture => fixture.Label == bed.FixtureLabel);
        var targetX = targetFixture.InteractionX ?? targetFixture.X;
        var targetY = targetFixture.InteractionY ?? targetFixture.Y;

        npc.CurrentRoomId = room.Id;
        npc.PositionX = 75;
        npc.PositionY = targetY;
        npc.ProvisioningJob = ActionKind.TendCrops;
        npc.TendingBedId = bed.Id;
        npc.CurrentAction = new NpcAction(ActionKind.TendCrops, bed.Id, "Walk to the grow bay.");

        room.Fixtures.Add(new RoomFixture(
            FixtureType.Crate,
            "Regression blocker",
            52,
            targetY,
            12,
            18));

        new LocalMovementSystem().Tick(state, TimeSpan.FromMinutes(5));

        Assert.True(
            Math.Abs(npc.PositionX - targetX) > 0.5 || Math.Abs(npc.PositionY - targetY) > 0.5,
            "The worker snapped directly through the blocking fixture to a later waypoint.");
        Assert.True(
            Math.Abs(npc.PositionY - targetY) > 0.5,
            $"Expected a visible detour around the blocker, got {npc.PositionX:0.0},{npc.PositionY:0.0}.");
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
