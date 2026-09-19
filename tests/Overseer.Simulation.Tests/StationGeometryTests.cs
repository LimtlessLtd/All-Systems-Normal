using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationGeometryTests
{
    [Fact]
    public void EveryDoor_IsAnchoredToTheExactSharedWall()
    {
        var state = FacilitySeeder.CreateDefault();

        foreach (var door in state.Facility.Doors)
        {
            var first = state.Facility.Rooms[door.RoomAId];
            var second = state.Facility.Rooms[door.RoomBId];
            var portal = StationGeometry.FindSharedPortal(first, second);

            Assert.True(
                IsOnBoundary(first, portal.X, portal.Y),
                $"{door.Id} portal is not on {first.Id}'s boundary.");
            Assert.True(
                IsOnBoundary(second, portal.X, portal.Y),
                $"{door.Id} portal is not on {second.Id}'s boundary.");
            Assert.True(StationGeometry.Contains(first, portal.X, portal.Y));
            Assert.True(StationGeometry.Contains(second, portal.X, portal.Y));
        }
    }

    [Fact]
    public void ConnectorHallways_TerminateFlushWithoutEnteringRoomsOrMainCorridor()
    {
        var state = FacilitySeeder.CreateDefault();
        var facility = state.Facility;

        foreach (var hallway in facility.Rooms.Values.Where(IsConnectorHallway))
        {
            var connectedDoors = facility.Doors
                .Where(door => door.RoomAId == hallway.Id || door.RoomBId == hallway.Id)
                .ToList();

            Assert.Equal(2, connectedDoors.Count);

            foreach (var door in connectedDoors)
            {
                var otherId = door.RoomAId == hallway.Id ? door.RoomBId : door.RoomAId;
                var other = facility.Rooms[otherId];

                Assert.InRange(
                    StationGeometry.InteriorOverlapArea(hallway, other),
                    0,
                    0.000001);

                _ = StationGeometry.FindSharedPortal(hallway, other);
            }
        }
    }

    [Fact]
    public void ConnectorHallways_DoNotAccidentallyOverlapEachOther()
    {
        var hallways = FacilitySeeder.CreateDefault().Facility.Rooms.Values
            .Where(IsConnectorHallway)
            .ToList();

        for (var firstIndex = 0; firstIndex < hallways.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < hallways.Count; secondIndex++)
            {
                var first = hallways[firstIndex];
                var second = hallways[secondIndex];

                Assert.InRange(
                    StationGeometry.InteriorOverlapArea(first, second),
                    0,
                    0.000001);
            }
        }
    }


    [Fact]
    public void FunctionalRooms_DominateTheDeckCanvas()
    {
        var rooms = FacilitySeeder.CreateDefault().Facility.Rooms.Values;

        var functionalRoomArea = rooms
            .Where(room => room.Type != RoomType.Corridor)
            .Sum(room => room.MapWidth * room.MapHeight);

        var corridorArea = rooms
            .Where(room => room.Type == RoomType.Corridor)
            .Sum(room => room.MapWidth * room.MapHeight);

        Assert.True(
            functionalRoomArea >= 7_000,
            $"Functional rooms occupy only {functionalRoomArea / 100:0.0}% of the deck canvas.");
        Assert.True(
            corridorArea <= 1_000,
            $"Corridors occupy {corridorArea / 100:0.0}% of the deck canvas.");
    }

    [Fact]
    public void FunctionalRooms_DoNotOverlapEachOther()
    {
        var rooms = FacilitySeeder.CreateDefault().Facility.Rooms.Values
            .Where(room => room.Type != RoomType.Corridor)
            .ToList();

        for (var firstIndex = 0; firstIndex < rooms.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < rooms.Count; secondIndex++)
            {
                var first = rooms[firstIndex];
                var second = rooms[secondIndex];

                Assert.InRange(
                    StationGeometry.InteriorOverlapArea(first, second),
                    0,
                    0.000001);
            }
        }
    }

    [Fact]
    public void Passages_AreWideEnoughForTwoWayCrewTraffic()
    {
        var state = FacilitySeeder.CreateDefault();
        var corridor = state.Facility.Rooms["corridor"];

        Assert.True(
            corridor.MapHeight >= 8,
            $"Main corridor height {corridor.MapHeight:0.0}% is too narrow for two-way traffic.");

        foreach (var hallway in state.Facility.Rooms.Values.Where(IsConnectorHallway))
        {
            var neighbours = state.Facility.Doors
                .Where(door => door.RoomAId == hallway.Id || door.RoomBId == hallway.Id)
                .Select(door => state.Facility.Rooms[
                    door.RoomAId == hallway.Id ? door.RoomBId : door.RoomAId])
                .ToList();

            Assert.True(neighbours.Count >= 2);

            var portal = StationGeometry.FindSharedPortal(
                hallway,
                neighbours[0]);
            var vertical = portal.Wall == StationWall.Horizontal;
            var passageWidth = vertical ? hallway.MapWidth : hallway.MapHeight;

            Assert.True(
                passageWidth >= 4.5,
                $"{hallway.Id} is only {passageWidth:0.0}% wide.");
        }
    }

    [Fact]
    public void CorridorsUseOnlyRestrainedWindowSeatingAndCameraFixtures()
    {
        var corridorFixtures = FacilitySeeder.CreateDefault().Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor)
            .SelectMany(room => room.Fixtures)
            .ToList();

        Assert.NotEmpty(corridorFixtures);
        Assert.All(
            corridorFixtures,
            fixture => Assert.Contains(
                fixture.Type,
                new[] { FixtureType.Window, FixtureType.Bench, FixtureType.Camera }));
    }

    [Fact]
    public void SeededFixturesAndInteractionAnchors_StayInsideTheirRooms()
    {
        var state = FacilitySeeder.CreateDefault();

        foreach (var room in state.Facility.Rooms.Values)
        {
            foreach (var fixture in room.Fixtures)
            {
                Assert.InRange(fixture.X - (fixture.Width / 2), 0, 100);
                Assert.InRange(fixture.X + (fixture.Width / 2), 0, 100);
                Assert.InRange(fixture.Y - (fixture.Height / 2), 0, 100);
                Assert.InRange(fixture.Y + (fixture.Height / 2), 0, 100);

                if (fixture.InteractionX is { } interactionX)
                {
                    Assert.InRange(interactionX, 0, 100);
                }

                if (fixture.InteractionY is { } interactionY)
                {
                    Assert.InRange(interactionY, 0, 100);
                }
            }
        }
    }

    [Fact]
    public void HumanUseFixtures_DescribeThePoseTheySupport()
    {
        var state = FacilitySeeder.CreateDefault();
        var fixtures = state.Facility.Rooms.Values.SelectMany(room => room.Fixtures).ToList();

        Assert.All(
            fixtures.Where(fixture => fixture.Type is FixtureType.Bed or FixtureType.MedicalBed),
            fixture => Assert.Equal(FixtureUsePose.Lie, fixture.UsePose));

        Assert.All(
            fixtures.Where(fixture => fixture.Type == FixtureType.Chair),
            fixture => Assert.Equal(FixtureUsePose.Sit, fixture.UsePose));

        Assert.All(
            fixtures.Where(fixture => fixture.Type == FixtureType.Shower),
            fixture => Assert.Equal(FixtureUsePose.Shower, fixture.UsePose));

        Assert.All(
            fixtures.Where(fixture => fixture.Type == FixtureType.Toilet),
            fixture => Assert.Equal(FixtureUsePose.Toilet, fixture.UsePose));
    }

    private static bool IsConnectorHallway(Room room) =>
        room.Type == RoomType.Corridor
        && room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase);

    private static bool IsOnBoundary(Room room, double x, double y)
    {
        const double tolerance = 0.001;
        var bounds = StationGeometry.Bounds(room);
        var onVerticalWall =
            (Math.Abs(x - bounds.Left) <= tolerance || Math.Abs(x - bounds.Right) <= tolerance)
            && y >= bounds.Top - tolerance
            && y <= bounds.Bottom + tolerance;
        var onHorizontalWall =
            (Math.Abs(y - bounds.Top) <= tolerance || Math.Abs(y - bounds.Bottom) <= tolerance)
            && x >= bounds.Left - tolerance
            && x <= bounds.Right + tolerance;

        return onVerticalWall || onHorizontalWall;
    }
}
