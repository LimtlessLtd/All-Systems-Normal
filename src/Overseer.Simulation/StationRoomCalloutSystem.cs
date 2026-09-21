using Overseer.Domain;

namespace Overseer.Simulation;

public sealed record StationRoomCallout(
    string RoomId,
    double LabelX,
    double LabelY,
    double AnchorX,
    double AnchorY,
    string Side,
    bool IsExternal = false,
    double LabelWidth = StationRoomCalloutSystem.MaxLabelWidthPercent);

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
    public const double LabelHalfHeight = 1.15;

    /// <summary>Widest a label may be, as a percentage of deck width (~260px).</summary>
    public const double MaxLabelWidthPercent = 8.1;
    private const double AttachedGap = .16;

    public static IReadOnlyList<StationRoomCallout> Build(Facility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);

        var rooms = facility.Rooms.Values
            .Where(room => room.Type != RoomType.Corridor)
            .OrderBy(room => room.Id, StringComparer.Ordinal)
            .ToList();
        var callouts = rooms
            .Select(room => BuildAttached(
                facility,
                room,
                room.StatusPlateSide switch
                {
                    RoomStatusPlateSide.Top => "top",
                    RoomStatusPlateSide.Bottom => "bottom",
                    _ => null
                }))
            .ToList();

        // Labels are as wide as their room, so labels on the same side of
        // non-overlapping rooms cannot collide. Two labels can still meet in a
        // shared gap between stacked rooms; move one to its room's other edge
        // when that side is clear.
        for (var index = 0; index < callouts.Count; index++)
        {
            if (!callouts.Where((other, otherIndex) => otherIndex != index).Any(other => Overlaps(callouts[index], other))
                || rooms[index].StatusPlateSide is not null)
                continue;

            var flipped = BuildAttached(
                facility,
                rooms[index],
                callouts[index].Side == "top" ? "bottom" : "top");

            if (!callouts.Where((other, otherIndex) => otherIndex != index).Any(other => Overlaps(flipped, other)))
                callouts[index] = flipped;
        }

        return callouts;
    }

    public static bool Overlaps(StationRoomCallout first, StationRoomCallout second) =>
        Math.Abs(first.LabelX - second.LabelX) < (first.LabelWidth + second.LabelWidth) / 2
        && Math.Abs(first.LabelY - second.LabelY) < LabelHalfHeight * 2;

    public static double ToDeck(double authoritativePercent) =>
        AuthorityInsetPercent + (authoritativePercent * AuthorityScale);

    private static StationRoomCallout BuildAttached(Facility facility, Room room, string? preferredSide)
    {
        var topClearance = EdgeClearance(facility, room, top: true);
        var bottomClearance = EdgeClearance(facility, room, top: false);
        var side = preferredSide ?? (topClearance >= bottomClearance ? "top" : "bottom");
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
            side,
            LabelWidth: Math.Min(MaxLabelWidthPercent, room.MapWidth * AuthorityScale));
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
