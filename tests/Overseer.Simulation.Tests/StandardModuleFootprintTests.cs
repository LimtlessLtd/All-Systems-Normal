using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #78: modules installed in most rooms (lighting, climate,
/// ventilation, door local controls) must render at one physical size in every
/// room rather than a percentage of whichever compartment they sit in.
/// </summary>
public sealed class StandardModuleFootprintTests
{
    private const double Tolerance = 1e-6;

    public static TheoryData<int> Seeds()
    {
        var seeds = new TheoryData<int>();
        foreach (var seed in Enumerable.Range(1, 40).Select(index => index * 7919))
        {
            seeds.Add(seed);
        }

        // Seeds whose cramped hydroponics bay forced the full-size off-wall
        // fallback while tuning this contract.
        seeds.Add(28);
        seeds.Add(178);
        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void StandardModules_HaveTheirCanonicalPhysicalSizeInEveryRoom(int seed)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: seed);
        var checkedFamilies = new HashSet<StationSystemKind>();

        foreach (var (room, fixture, device) in StandardModules(state))
        {
            Assert.True(StandardModuleFootprints.TryGet(device.Kind, out var footprint));
            var physicalWidth = fixture.Width * room.MapWidth / 100;
            var physicalHeight = fixture.Height * room.MapHeight / 100;
            var along = Math.Max(physicalWidth, physicalHeight);
            var across = Math.Min(physicalWidth, physicalHeight);

            Assert.True(
                Math.Abs(along - footprint.Length) < Tolerance
                    && Math.Abs(across - footprint.Depth) < Tolerance,
                $"Seed {seed}: {fixture.Label} in {room.Id} ({room.MapWidth:0.##}x{room.MapHeight:0.##}) " +
                $"is {physicalWidth:0.###}x{physicalHeight:0.###}, expected {footprint.Length}x{footprint.Depth}.");
            checkedFamilies.Add(device.Kind);
        }

        Assert.Superset(
            new HashSet<StationSystemKind>
            {
                StationSystemKind.Lighting,
                StationSystemKind.ClimateControl,
                StationSystemKind.Ventilation,
                StationSystemKind.Door
            },
            checkedFamilies);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void StandardModules_StayInsideTheRoomAndAreUsableFromOpenFloor(int seed)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: seed);

        foreach (var (room, fixture, _) in StandardModules(state))
        {
            Assert.InRange(fixture.X - (fixture.Width / 2), 1 - Tolerance, 100);
            Assert.InRange(fixture.X + (fixture.Width / 2), 0, 99 + Tolerance);
            Assert.InRange(fixture.Y - (fixture.Height / 2), 1 - Tolerance, 100);
            Assert.InRange(fixture.Y + (fixture.Height / 2), 0, 99 + Tolerance);

            Assert.NotNull(fixture.InteractionX);
            Assert.NotNull(fixture.InteractionY);
            Assert.False(
                Inside(fixture, fixture.InteractionX!.Value, fixture.InteractionY!.Value),
                $"Seed {seed}: {fixture.Label} in {room.Id} has its interaction point inside itself.");

            foreach (var other in room.Fixtures.Where(other => !ReferenceEquals(other, fixture)))
            {
                Assert.False(
                    Overlaps(fixture, other),
                    $"Seed {seed}: {fixture.Label} overlaps {other.Label} in {room.Id}.");
            }
        }
    }

    [Fact]
    public void LocalSize_RotatesPhysicalDimensionsNotPercentages()
    {
        var room = new Room
        {
            Id = "wide",
            Name = "Wide",
            Type = RoomType.Storage,
            MapWidth = 20,
            MapHeight = 10
        };
        var footprint = new StandardModuleFootprints.Footprint(2, 1);

        var horizontal = StandardModuleFootprints.LocalSize(room, footprint, horizontalWall: true);
        var vertical = StandardModuleFootprints.LocalSize(room, footprint, horizontalWall: false);

        Assert.Equal((10d, 10d), horizontal);
        Assert.Equal((5d, 20d), vertical);
    }

    private static IEnumerable<(Room Room, RoomFixture Fixture, StationDevice Device)> StandardModules(
        GameState state) =>
        from room in state.Facility.Rooms.Values
        from fixture in room.Fixtures
        where fixture.DeviceId is not null
            && state.Devices.TryGetValue(fixture.DeviceId, out _)
        let device = state.Devices[fixture.DeviceId!]
        where StandardModuleFootprints.TryGet(device.Kind, out _)
        select (room, fixture, device);

    private static bool Inside(RoomFixture fixture, double x, double y) =>
        x > fixture.X - (fixture.Width / 2)
        && x < fixture.X + (fixture.Width / 2)
        && y > fixture.Y - (fixture.Height / 2)
        && y < fixture.Y + (fixture.Height / 2);

    private static bool Overlaps(RoomFixture first, RoomFixture second) =>
        first.X - (first.Width / 2) < second.X + (second.Width / 2)
        && first.X + (first.Width / 2) > second.X - (second.Width / 2)
        && first.Y - (first.Height / 2) < second.Y + (second.Height / 2)
        && first.Y + (first.Height / 2) > second.Y - (second.Height / 2);
}
