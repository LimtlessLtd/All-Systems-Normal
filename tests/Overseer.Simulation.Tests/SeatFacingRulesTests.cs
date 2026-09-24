using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #105: seats are drawn facing what they serve, so the backrest
/// lands on the far side.
/// </summary>
public sealed class SeatFacingRulesTests
{
    [Theory]
    [InlineData(50, 30, SeatFacing.Down)]
    [InlineData(50, 80, SeatFacing.Up)]
    [InlineData(20, 55, SeatFacing.Right)]
    [InlineData(80, 55, SeatFacing.Left)]
    public void AChairAroundATableFacesTheTable(double x, double y, SeatFacing expected)
    {
        var room = Room(
            new RoomFixture(FixtureType.Table, "Mess Table", 50, 55, 30, 16),
            Chair(x, y));

        Assert.Equal(expected, SeatFacingRules.Facing(room, room.Fixtures[1]));
    }

    [Fact]
    public void ASofaFacesTheTelevisionEvenWithATableCloser()
    {
        var sofa = new RoomFixture(FixtureType.Sofa, "Sofa", 30, 60, 26, 18);
        var room = Room(
            new RoomFixture(FixtureType.Television, "TV", 50, 8, 30, 6),
            new RoomFixture(FixtureType.Table, "Low Table", 50, 62, 18, 12),
            sofa);

        Assert.Equal(SeatFacing.Up, SeatFacingRules.Facing(room, sofa));
    }

    [Fact]
    public void ASofaFacesTheTelevisionRatherThanANearerWallScreen()
    {
        // Seen on a generated lounge: a status screen on the wall behind the
        // sofa was nearer than the set, so the sofa faced the wall.
        var sofa = new RoomFixture(FixtureType.Sofa, "Sofa", 30, 70, 26, 18);
        var room = Room(
            new RoomFixture(FixtureType.Television, "TV", 50, 8, 30, 6),
            new RoomFixture(FixtureType.Screen, "Screen 2", 30, 94, 20, 6),
            sofa);

        Assert.Equal(SeatFacing.Up, SeatFacingRules.Facing(room, sofa));
    }

    [Fact]
    public void AChairFacesItsDeskRatherThanTheTelevision()
    {
        var chair = Chair(50, 70);
        var room = Room(
            new RoomFixture(FixtureType.Television, "TV", 50, 95, 30, 6),
            new RoomFixture(FixtureType.Table, "Desk", 50, 55, 24, 12),
            chair);

        Assert.Equal(SeatFacing.Up, SeatFacingRules.Facing(room, chair));
    }

    [Theory]
    [InlineData(0, SeatFacing.Up)]
    [InlineData(90, SeatFacing.Right)]
    [InlineData(180, SeatFacing.Down)]
    [InlineData(270, SeatFacing.Left)]
    public void WithNothingToFaceASeatUsesItsSeededFacing(double degrees, SeatFacing expected)
    {
        var chair = Chair(50, 50) with { FacingDegrees = degrees };
        var room = Room(chair);

        Assert.Equal(expected, SeatFacingRules.Facing(room, chair));
    }

    [Fact]
    public void OnlySeatsHaveAFacing()
    {
        var table = new RoomFixture(FixtureType.Table, "Table", 50, 50, 20, 10);

        Assert.Null(SeatFacingRules.Facing(Room(table), table));
    }

    [Fact]
    public void FacingIsJudgedInPhysicalDistanceNotRoomPercent()
    {
        // A wide, shallow room: 30% across is far further than 30% down.
        var chair = Chair(50, 50);
        var room = new Room { Id = "wide", Name = "Wide", Type = RoomType.Kitchen, MapWidth = 60, MapHeight = 10 };
        room.Fixtures.AddRange([
            new RoomFixture(FixtureType.Table, "Across", 80, 50, 10, 10),
            new RoomFixture(FixtureType.Table, "Below", 50, 85, 10, 10),
            chair]);

        Assert.Equal(SeatFacing.Down, SeatFacingRules.Facing(room, chair));
    }

    private static RoomFixture Chair(double x, double y) =>
        new(FixtureType.Chair, "Chair", x, y, 10, 10, UsePose: FixtureUsePose.Sit);

    private static Room Room(params RoomFixture[] fixtures)
    {
        var room = new Room { Id = "test", Name = "Test", Type = RoomType.Kitchen };
        room.Fixtures.AddRange(fixtures);
        return room;
    }
}
