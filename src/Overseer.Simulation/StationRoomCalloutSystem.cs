using Overseer.Domain;

namespace Overseer.Simulation;

public sealed record StationRoomCallout(
    string RoomId,
    double LabelX,
    double LabelY,
    double AnchorX,
    double AnchorY,
    string Side,
    bool IsExternal = false);

/// <summary>
/// Presentation-only room telemetry. Every functional room owns a compact
/// status strip physically attached to either its top or bottom hull edge.
/// Procedural rooms reserve extra vertical shell during generation, so these
/// panels never need unrelated left/right margin callouts.
/// </summary>
public static class StationRoomCalloutSystem
{
    public const double AuthorityInsetPercent = 10;
    public const double AuthorityScale = 0.8;
    private const double LabelHalfHeight = 1.15;
    private const double AttachedGap = .16;

    public static IReadOnlyList<StationRoomCallout> Build(Facility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);

        return facility.Rooms.Values
            .Where(room => room.Type != RoomType.Corridor)
            .OrderBy(room => room.Id, StringComparer.Ordinal)
            .Select(room => BuildAttached(facility, room))
            .ToList();
    }

    public static double ToDeck(double authoritativePercent) =>
        AuthorityInsetPercent + (authoritativePercent * AuthorityScale);

    private static StationRoomCallout BuildAttached(Facility facility, Room room)
    {
        var topClearance = EdgeClearance(facility, room, top: true);
        var bottomClearance = EdgeClearance(facility, room, top: false);
        var side = topClearance >= bottomClearance ? "top" : "bottom";
        var anchorX = ToDeck(room.MapX);
        var anchorY = ToDeck(
            side == "top"
                ? room.MapY - (room.MapHeight / 2)
                : room.MapY + (room.MapHeight / 2));
        var labelY = anchorY
            + (side == "top"
                ? -(LabelHalfHeight + AttachedGap)
                : LabelHalfHeight + AttachedGap);

        return new StationRoomCallout(
            room.Id,
            anchorX,
            Math.Clamp(labelY, 1.5, 98.5),
            anchorX,
            anchorY,
            side);
    }

    private static double EdgeClearance(Facility facility, Room room, bool top)
    {
        var bounds = StationGeometry.Bounds(room);
        var edge = top ? bounds.Top : bounds.Bottom;
        var nearest = top ? bounds.Top - 2 : 98 - bounds.Bottom;

        foreach (var other in facility.Rooms.Values.Where(other => other.Id != room.Id))
        {
            var otherBounds = StationGeometry.Bounds(other);
            var horizontalOverlap =
                Math.Min(bounds.Right, otherBounds.Right)
                - Math.Max(bounds.Left, otherBounds.Left);

            if (horizontalOverlap <= 0)
                continue;

            if (top && otherBounds.Bottom <= edge)
                nearest = Math.Min(nearest, edge - otherBounds.Bottom);
            else if (!top && otherBounds.Top >= edge)
                nearest = Math.Min(nearest, otherBounds.Top - edge);
        }

        return Math.Max(0, nearest);
    }
}
