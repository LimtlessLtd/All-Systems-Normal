using Overseer.Domain;

namespace Overseer.Simulation;

public enum SeatFacing
{
    Up,
    Right,
    Down,
    Left
}

/// <summary>
/// Owner idea #105: which way a chair or sofa is drawn facing, so its backrest
/// sits on the far side. A sofa faces the room's television, else a screen,
/// when it has one; otherwise a seat faces the nearest table, console or workbench.
/// The seed's facing angle is the fallback when the room has nothing to face.
/// Presentation only, derived from the room's own fixture geometry.
/// </summary>
public static class SeatFacingRules
{
    public static SeatFacing? Facing(Room room, RoomFixture seat)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(seat);

        if (seat.Type is not (FixtureType.Chair or FixtureType.Sofa))
            return null;

        // A sofa watches the television before any wall display: generated
        // rooms scatter status screens that can sit nearer than the set.
        var faced = (seat.Type == FixtureType.Sofa
                ? Nearest(room, seat, fixture => fixture.Type == FixtureType.Television)
                    ?? Nearest(room, seat, IsViewingTarget)
                : null)
            ?? Nearest(room, seat, IsWorkSurface)
            ?? Nearest(room, seat, IsViewingTarget);

        double dx, dy;
        if (faced is not null)
        {
            dx = (faced.X - seat.X) * room.MapWidth;
            dy = (faced.Y - seat.Y) * room.MapHeight;
        }
        else
        {
            // 0 degrees faces up the map, 90 faces right.
            var radians = seat.FacingDegrees * Math.PI / 180;
            dx = Math.Sin(radians);
            dy = -Math.Cos(radians);
        }

        return Math.Abs(dx) >= Math.Abs(dy)
            ? (dx >= 0 ? SeatFacing.Right : SeatFacing.Left)
            : (dy >= 0 ? SeatFacing.Down : SeatFacing.Up);
    }

    private static bool IsViewingTarget(RoomFixture fixture) =>
        fixture.Type is FixtureType.Television or FixtureType.Screen;

    private static bool IsWorkSurface(RoomFixture fixture) =>
        fixture.Type is FixtureType.Table
            or FixtureType.Console
            or FixtureType.Workbench
            or FixtureType.RecreationConsole;

    private static RoomFixture? Nearest(Room room, RoomFixture seat, Func<RoomFixture, bool> accepts) =>
        room.Fixtures
            .Where(accepts)
            .MinBy(candidate =>
                Math.Pow((candidate.X - seat.X) * room.MapWidth, 2)
                + Math.Pow((candidate.Y - seat.Y) * room.MapHeight, 2));
}
