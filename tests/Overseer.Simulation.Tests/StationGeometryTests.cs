using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationGeometryTests
{
    [Fact]
    public void EveryDoor_IsAnchoredToTheExactSharedWall_AcrossSeeds()
    {
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var door in state.Facility.Doors)
            {
                var first = state.Facility.Rooms[door.RoomAId];
                var second = state.Facility.Rooms[door.RoomBId];
                var portal = StationGeometry.FindSharedPortal(first, second);

                Assert.True(
                    IsOnBoundary(first, portal.X, portal.Y),
                    $"Seed {seed}: {door.Id} portal is not on {first.Id}'s boundary.");
                Assert.True(
                    IsOnBoundary(second, portal.X, portal.Y),
                    $"Seed {seed}: {door.Id} portal is not on {second.Id}'s boundary.");
                Assert.True(StationGeometry.Contains(first, portal.X, portal.Y));
                Assert.True(StationGeometry.Contains(second, portal.X, portal.Y));
            }
        }
    }

    [Fact]
    public void ConnectorHallways_TerminateFlushWithoutEnteringRoomsOrNetwork()
    {
        foreach (var seed in Enumerable.Range(100, 12))
        {
            var facility = FacilitySeeder.CreateDefault(stationSeed: seed).Facility;

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
    }

    [Fact]
    public void GeneratedRoomsAndCorridors_DoNotIllegallyOverlap()
    {
        foreach (var seed in Enumerable.Range(300, 20))
        {
            var rooms = FacilitySeeder.CreateDefault(stationSeed: seed)
                .Facility.Rooms.Values.ToList();

            for (var firstIndex = 0; firstIndex < rooms.Count; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < rooms.Count; secondIndex++)
                {
                    Assert.InRange(
                        StationGeometry.InteriorOverlapArea(
                            rooms[firstIndex],
                            rooms[secondIndex]),
                        0,
                        0.000001);
                }
            }
        }
    }

    [Fact]
    public void FunctionalRoomsRemainVisuallyDominantOverCirculation()
    {
        foreach (var seed in Enumerable.Range(500, 16))
        {
            var rooms = FacilitySeeder.CreateDefault(stationSeed: seed).Facility.Rooms.Values;
            var functionalArea = rooms
                .Where(room => room.Type != RoomType.Corridor)
                .Sum(room => room.MapWidth * room.MapHeight);
            var corridorArea = rooms
                .Where(room => room.Type == RoomType.Corridor)
                .Sum(room => room.MapWidth * room.MapHeight);

            Assert.True(
                functionalArea > corridorArea,
                $"Seed {seed}: circulation area {corridorArea:0} dominates functional room area {functionalArea:0}.");
        }
    }

    [Fact]
    public void AllGeneratedGeometry_StaysInsideStationCanvas()
    {
        foreach (var seed in Enumerable.Range(700, 20))
        {
            foreach (var room in FacilitySeeder.CreateDefault(stationSeed: seed).Facility.Rooms.Values)
            {
                var bounds = StationGeometry.Bounds(room);
                Assert.InRange(bounds.Left, 0, 100);
                Assert.InRange(bounds.Right, 0, 100);
                Assert.InRange(bounds.Top, 0, 100);
                Assert.InRange(bounds.Bottom, 0, 100);
            }
        }
    }

    [Fact]
    public void FunctionalModules_HaveVariedFootprintsRatherThanUniformGridCells()
    {
        foreach (var seed in Enumerable.Range(900, 12))
        {
            var modules = FacilitySeeder.CreateDefault(stationSeed: seed).Facility.Rooms.Values
                .Where(room => room.Type != RoomType.Corridor)
                .ToList();

            var distinctFootprints = modules
                .Select(room => (
                    Width: Math.Round(room.MapWidth, 1),
                    Height: Math.Round(room.MapHeight, 1)))
                .Distinct()
                .Count();

            Assert.True(
                distinctFootprints >= 7,
                $"Seed {seed}: only {distinctFootprints} distinct functional module footprints.");
        }
    }

    [Fact]
    public void PhysicalPassagesHaveUsableCrossSections()
    {
        foreach (var seed in Enumerable.Range(1100, 16))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var hallway in state.Facility.Rooms.Values.Where(IsConnectorHallway))
            {
                var door = state.Facility.Doors.First(candidate =>
                    candidate.RoomAId.Equals(hallway.Id, StringComparison.OrdinalIgnoreCase)
                    || candidate.RoomBId.Equals(hallway.Id, StringComparison.OrdinalIgnoreCase));
                var neighbourId = door.RoomAId.Equals(hallway.Id, StringComparison.OrdinalIgnoreCase)
                    ? door.RoomBId
                    : door.RoomAId;
                var portal = StationGeometry.FindSharedPortal(
                    hallway,
                    state.Facility.Rooms[neighbourId]);

                var crossSection = portal.Wall == StationWall.Horizontal
                    ? hallway.MapWidth
                    : hallway.MapHeight;

                Assert.True(
                    crossSection >= 3.35,
                    $"Seed {seed}: {hallway.Id} cross-section is only {crossSection:0.00}%.");
            }

            foreach (var corridor in state.Facility.Rooms.Values.Where(room =>
                         room.Type == RoomType.Corridor && !IsConnectorHallway(room)))
            {
                Assert.True(
                    Math.Min(corridor.MapWidth, corridor.MapHeight) >= 3.5,
                    $"Seed {seed}: {corridor.Id} is microscopically narrow.");
            }
        }
    }

    [Fact]
    public void AccessTunnelsMatchTheConnectedCorridorCrossSectionExactly()
    {
        foreach (var seed in new[] { 17, 14142, 31415 })
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var hallway in state.Facility.Rooms.Values.Where(IsConnectorHallway))
            {
                var networkDoor = state.Facility.Doors
                    .Where(door => door.RoomAId == hallway.Id || door.RoomBId == hallway.Id)
                    .Select(door => new
                    {
                        Door = door,
                        Other = state.Facility.Rooms[
                            door.RoomAId == hallway.Id ? door.RoomBId : door.RoomAId]
                    })
                    .Single(pair => pair.Other.Type == RoomType.Corridor);

                var portal = StationGeometry.FindSharedPortal(hallway, networkDoor.Other);
                var hallwayCrossSection = portal.Wall == StationWall.Horizontal
                    ? hallway.MapWidth
                    : hallway.MapHeight;
                var networkPortal = StationGeometry.FindSharedPortal(networkDoor.Other, hallway);
                var networkCrossSection = networkPortal.Wall == StationWall.Horizontal
                    ? networkDoor.Other.MapWidth
                    : networkDoor.Other.MapHeight;

                Assert.Equal(
                    networkCrossSection,
                    hallwayCrossSection,
                    6);
            }
        }
    }

    [Fact]
    public void CorridorsUseOnlyRestrainedWindowSeatingAndCameraFixtures()
    {
        var corridorFixtures = FacilitySeeder.CreateDefault(stationSeed: 1337).Facility.Rooms.Values
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
        foreach (var seed in new[] { 7, 41, 1337, 90210 })
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

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
    }

    [Fact]
    public void HumanUseFixtures_DescribeThePoseTheySupport()
    {
        var fixtures = FacilitySeeder.CreateDefault(stationSeed: 17)
            .Facility.Rooms.Values.SelectMany(room => room.Fixtures).ToList();

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
