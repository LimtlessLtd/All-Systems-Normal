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
    public void WalkingSpeedUsesPhysicalMapDistanceRegardlessOfCorridorSize()
    {
        static double OneSecondStep(GameState state, Room room)
        {
            var npc = state.Crew[0];
            var door = state.Facility.Doors.First(candidate =>
                candidate.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                || candidate.RoomBId.Equals(room.Id, StringComparison.OrdinalIgnoreCase));
            var targetRoomId = door.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId
                : door.RoomAId;

            door.IsOpen = true;
            door.IsLocked = false;
            door.IsPowered = true;
            npc.CurrentRoomId = room.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.Fatigue = 0;
            npc.SleepDebtMinutes = 0;
            npc.Movement = null;
            npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Speed regression reset.");

            Assert.True(new ActionResolver().TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.Move, targetRoomId, "Walking speed regression."),
                out var message), message);

            var beforeX = npc.PositionX;
            var beforeY = npc.PositionY;
            new LocalMovementSystem().Tick(state, TimeSpan.FromSeconds(1));

            var dx = (npc.PositionX - beforeX) / 100d * room.MapWidth;
            var dy = (npc.PositionY - beforeY) / 100d * room.MapHeight;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        var firstState = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var corridors = firstState.Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor)
            .OrderBy(room => room.MapWidth * room.MapHeight)
            .ToList();

        Assert.True(corridors.Count >= 2);
        var small = corridors.First();
        var large = corridors.Last();
        Assert.NotEqual(
            small.MapWidth * small.MapHeight,
            large.MapWidth * large.MapHeight);

        var smallStep = OneSecondStep(firstState, small);

        var secondState = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var matchingLarge = secondState.Facility.Rooms[large.Id];
        var largeStep = OneSecondStep(secondState, matchingLarge);

        Assert.InRange(smallStep, .06, .12);
        Assert.InRange(largeStep, .06, .12);
        Assert.InRange(Math.Abs(smallStep - largeStep), 0, .01);
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

        var movement = new LocalMovementSystem();
        for (var step = 0; step < 12; step++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        var after = Math.Sqrt(
            Math.Pow(robot.PositionX - targetX, 2)
            + Math.Pow(robot.PositionY - targetY, 2));

        // A collision-safe route is not required to reduce straight-line
        // distance on every intermediate waypoint. The dedicated blocker test
        // below guards against geometry skipping; this integration check verifies
        // the repair robot still makes real progress toward its machinery.
        Assert.True(
            after + 5 < before,
            $"Robot did not physically approach generator after detour route: {before:0.0} -> {after:0.0}.");
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

    [Fact]
    public void CrewFollowingTheSameLocalTarget_DoNotRemainStacked()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var room = state.Facility.Rooms["quarters"];
        var crew = state.Crew.Take(2).ToList();

        foreach (var npc in crew)
        {
            npc.CurrentRoomId = room.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.Movement = null;
            npc.CurrentAction = new NpcAction(
                ActionKind.Sleep,
                room.Id,
                "Use the same sleep target for the overlap regression.");
        }

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 20; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        var physicalDx = (crew[0].PositionX - crew[1].PositionX) / 100d * room.MapWidth;
        var physicalDy = (crew[0].PositionY - crew[1].PositionY) / 100d * room.MapHeight;
        var separation = Math.Sqrt((physicalDx * physicalDx) + (physicalDy * physicalDy));

        Assert.True(
            separation >= 1.2,
            $"Crew remained stacked at {crew[0].PositionX:0.00},{crew[0].PositionY:0.00} and " +
            $"{crew[1].PositionX:0.00},{crew[1].PositionY:0.00} ({separation:0.00} map units apart).");
    }

    [Fact]
    public void TwoCrewCrossingTheSameDoor_DoNotDeadlockOrStackAtTheEntry()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 16180);
        var door = state.Facility.Doors.First();
        door.IsPowered = true;
        door.IsLocked = false;
        door.IsOpen = true;

        var crew = state.Crew.Take(2).ToList();
        var resolver = new ActionResolver();

        foreach (var npc in crew)
        {
            npc.CurrentRoomId = door.RoomAId;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.Movement = null;

            Assert.True(
                resolver.TryApply(
                    state,
                    npc.Id,
                    new NpcAction(ActionKind.Move, door.RoomBId, "Cross together."),
                    out var message),
                message);

            var movement = Assert.IsType<NpcMovement>(npc.Movement);
            npc.PositionX = movement.ExitX;
            npc.PositionY = movement.ExitY;
        }

        new LocalMovementSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.All(crew, npc =>
        {
            Assert.Equal(door.RoomBId, npc.CurrentRoomId);
            Assert.Null(npc.Movement);
        });

        var room = state.Facility.Rooms[door.RoomBId];
        var physicalDx = (crew[0].PositionX - crew[1].PositionX) / 100d * room.MapWidth;
        var physicalDy = (crew[0].PositionY - crew[1].PositionY) / 100d * room.MapHeight;
        var separation = Math.Sqrt((physicalDx * physicalDx) + (physicalDy * physicalDy));

        Assert.True(
            separation >= 1.2,
            $"Door entrants stacked at the same authoritative point ({separation:0.00} map units apart).");
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
