using Overseer.Domain;

namespace Overseer.Simulation;

public enum StationWall
{
    Horizontal,
    Vertical
}

public readonly record struct StationBounds(
    double Left,
    double Right,
    double Top,
    double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

public readonly record struct StationPortal(
    double X,
    double Y,
    StationWall Wall);

/// <summary>
/// Shared physical deck geometry used by deterministic movement, rendering and
/// regression tests. A connected pair must meet at one real wall edge; doors
/// are never inferred from room centres.
/// </summary>
public static class StationGeometry
{
    private const double Tolerance = 0.001;

    public static StationBounds Bounds(Room room) =>
        new(
            room.MapX - (room.MapWidth / 2),
            room.MapX + (room.MapWidth / 2),
            room.MapY - (room.MapHeight / 2),
            room.MapY + (room.MapHeight / 2));

    public static StationPortal FindSharedPortal(Room first, Room second)
    {
        var a = Bounds(first);
        var b = Bounds(second);

        var overlapTop = Math.Max(a.Top, b.Top);
        var overlapBottom = Math.Min(a.Bottom, b.Bottom);
        var verticalOverlap = overlapBottom - overlapTop;

        var overlapLeft = Math.Max(a.Left, b.Left);
        var overlapRight = Math.Min(a.Right, b.Right);
        var horizontalOverlap = overlapRight - overlapLeft;

        if (verticalOverlap > Tolerance)
        {
            if (NearlyEqual(a.Right, b.Left))
            {
                return new StationPortal(
                    (a.Right + b.Left) / 2,
                    (overlapTop + overlapBottom) / 2,
                    StationWall.Vertical);
            }

            if (NearlyEqual(a.Left, b.Right))
            {
                return new StationPortal(
                    (a.Left + b.Right) / 2,
                    (overlapTop + overlapBottom) / 2,
                    StationWall.Vertical);
            }
        }

        if (horizontalOverlap > Tolerance)
        {
            if (NearlyEqual(a.Bottom, b.Top))
            {
                return new StationPortal(
                    (overlapLeft + overlapRight) / 2,
                    (a.Bottom + b.Top) / 2,
                    StationWall.Horizontal);
            }

            if (NearlyEqual(a.Top, b.Bottom))
            {
                return new StationPortal(
                    (overlapLeft + overlapRight) / 2,
                    (a.Top + b.Bottom) / 2,
                    StationWall.Horizontal);
            }
        }

        throw new InvalidOperationException(
            $"Rooms '{first.Id}' and '{second.Id}' do not share a physical wall portal.");
    }

    public static double InteriorOverlapArea(Room first, Room second)
    {
        var a = Bounds(first);
        var b = Bounds(second);
        var width = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
        var height = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        return width * height;
    }

    public static bool Contains(
        Room room,
        double mapX,
        double mapY,
        double tolerance = Tolerance)
    {
        var bounds = Bounds(room);
        return mapX >= bounds.Left - tolerance
            && mapX <= bounds.Right + tolerance
            && mapY >= bounds.Top - tolerance
            && mapY <= bounds.Bottom + tolerance;
    }

    private static bool NearlyEqual(double first, double second) =>
        Math.Abs(first - second) <= Tolerance;
}
