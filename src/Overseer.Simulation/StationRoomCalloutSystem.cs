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
/// Presentation-only room telemetry layout. Labels prefer a short, visibly
/// attached position beside their room when real station geometry leaves enough
/// clear space. Crowded rooms fall back to the outer deck margin with an
/// explicit leader line; authoritative room/corridor geometry is never moved.
/// </summary>
public static class StationRoomCalloutSystem
{
    public const double AuthorityInsetPercent = 10;
    public const double AuthorityScale = 0.8;

    private const double HorizontalLabelXLeft = 4.7;
    private const double HorizontalLabelXRight = 95.3;
    private const double VerticalLabelYTop = 4.5;
    private const double VerticalLabelYBottom = 95.5;
    private const double HorizontalSpacing = 9.2;
    private const double VerticalSpacing = 3.35;
    private const double LabelHalfWidth = 4.2;
    private const double LabelHalfHeight = 1.35;
    private const double AttachedGap = .75;

    private enum Side
    {
        Left,
        Right,
        Top,
        Bottom
    }

    public static IReadOnlyList<StationRoomCallout> Build(Facility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);

        var rooms = facility.Rooms.Values
            .Where(room => room.Type != RoomType.Corridor)
            .OrderByDescending(room => room.MapWidth * room.MapHeight)
            .ThenBy(room => room.Id, StringComparer.Ordinal)
            .ToList();

        var result = new List<StationRoomCallout>(rooms.Count);
        var occupiedLabels = new List<LabelRect>();
        var external = new List<Assignment>();

        foreach (var room in rooms)
        {
            if (TryPlaceAttached(facility, room, occupiedLabels, out var callout))
            {
                result.Add(callout);
                occupiedLabels.Add(LabelRect.FromCenter(callout.LabelX, callout.LabelY));
            }
            else
            {
                external.Add(new Assignment(room, ChooseSide(room)));
            }
        }

        foreach (var side in Enum.GetValues<Side>())
        {
            var group = external
                .Where(item => item.Side == side)
                .OrderBy(item => side is Side.Left or Side.Right
                    ? item.Room.MapY
                    : item.Room.MapX)
                .ToList();

            if (group.Count == 0)
            {
                continue;
            }

            var desired = group
                .Select(item => side is Side.Left or Side.Right
                    ? ToDeck(item.Room.MapY)
                    : ToDeck(item.Room.MapX))
                .ToArray();

            var positioned = Spread(
                desired,
                side is Side.Left or Side.Right ? VerticalSpacing : HorizontalSpacing,
                side is Side.Left or Side.Right ? 11.5 : 13.5,
                side is Side.Left or Side.Right ? 88.5 : 86.5);

            for (var index = 0; index < group.Count; index++)
            {
                var room = group[index].Room;
                var (x, y) = ResolveExternalLabelPosition(
                    side,
                    positioned[index],
                    occupiedLabels);
                var (anchorX, anchorY) = Anchor(room, side);

                result.Add(new StationRoomCallout(
                    room.Id,
                    x,
                    y,
                    anchorX,
                    anchorY,
                    side.ToString().ToLowerInvariant(),
                    IsExternal: true));
                occupiedLabels.Add(LabelRect.FromCenter(x, y));
            }
        }

        return result
            .OrderBy(callout => callout.RoomId, StringComparer.Ordinal)
            .ToList();
    }

    public static double ToDeck(double authoritativePercent) =>
        AuthorityInsetPercent + (authoritativePercent * AuthorityScale);

    private static (double X, double Y) ResolveExternalLabelPosition(
        Side side,
        double desiredAxis,
        IReadOnlyList<LabelRect> occupiedLabels)
    {
        var minimum = side is Side.Left or Side.Right ? 11.5 : 13.5;
        var maximum = side is Side.Left or Side.Right ? 88.5 : 86.5;
        var spacing = side is Side.Left or Side.Right ? VerticalSpacing : HorizontalSpacing;

        foreach (var axis in ExternalAxisCandidates(desiredAxis, minimum, maximum, spacing))
        {
            var x = side switch
            {
                Side.Left => HorizontalLabelXLeft,
                Side.Right => HorizontalLabelXRight,
                _ => axis
            };
            var y = side switch
            {
                Side.Top => VerticalLabelYTop,
                Side.Bottom => VerticalLabelYBottom,
                _ => axis
            };
            var rect = LabelRect.FromCenter(x, y);

            if (!occupiedLabels.Any(existing => rect.Overlaps(existing, .3)))
            {
                return (x, y);
            }
        }

        for (var axis = minimum; axis <= maximum; axis += .5)
        {
            var x = side switch
            {
                Side.Left => HorizontalLabelXLeft,
                Side.Right => HorizontalLabelXRight,
                _ => axis
            };
            var y = side switch
            {
                Side.Top => VerticalLabelYTop,
                Side.Bottom => VerticalLabelYBottom,
                _ => axis
            };
            var rect = LabelRect.FromCenter(x, y);

            if (!occupiedLabels.Any(existing => rect.Overlaps(existing, .05)))
            {
                return (x, y);
            }
        }

        throw new InvalidOperationException(
            $"Unable to place an external station callout on the {side} margin without overlap.");
    }

    private static IEnumerable<double> ExternalAxisCandidates(
        double desired,
        double minimum,
        double maximum,
        double spacing)
    {
        yield return Math.Clamp(desired, minimum, maximum);

        for (var step = 1; step <= 16; step++)
        {
            yield return Math.Clamp(desired + (spacing * step), minimum, maximum);
            yield return Math.Clamp(desired - (spacing * step), minimum, maximum);
        }
    }

    private static bool TryPlaceAttached(
        Facility facility,
        Room room,
        IReadOnlyList<LabelRect> occupiedLabels,
        out StationRoomCallout callout)
    {
        foreach (var side in PreferredSides(room))
        {
            var (anchorX, anchorY) = Anchor(room, side);
            var x = side switch
            {
                Side.Left => anchorX - LabelHalfWidth - AttachedGap,
                Side.Right => anchorX + LabelHalfWidth + AttachedGap,
                _ => anchorX
            };
            var y = side switch
            {
                Side.Top => anchorY - LabelHalfHeight - AttachedGap,
                Side.Bottom => anchorY + LabelHalfHeight + AttachedGap,
                _ => anchorY
            };
            var rect = LabelRect.FromCenter(x, y);

            if (!rect.IsInsideDeck
                || occupiedLabels.Any(existing => rect.Overlaps(existing, .3))
                || OverlapsStationGeometry(facility, rect, .3))
            {
                continue;
            }

            callout = new StationRoomCallout(
                room.Id,
                x,
                y,
                anchorX,
                anchorY,
                side.ToString().ToLowerInvariant());
            return true;
        }

        callout = default!;
        return false;
    }

    private static IEnumerable<Side> PreferredSides(Room room)
    {
        var clearances = new[]
        {
            (Side.Left, room.MapX - (room.MapWidth / 2)),
            (Side.Right, 100 - (room.MapX + (room.MapWidth / 2))),
            (Side.Top, room.MapY - (room.MapHeight / 2)),
            (Side.Bottom, 100 - (room.MapY + (room.MapHeight / 2)))
        };

        return clearances
            .OrderBy(item => item.Item2)
            .ThenBy(item => item.Item1)
            .Select(item => item.Item1);
    }

    private static bool OverlapsStationGeometry(
        Facility facility,
        LabelRect label,
        double padding) =>
        facility.Rooms.Values.Any(room =>
        {
            var centerX = ToDeck(room.MapX);
            var centerY = ToDeck(room.MapY);
            var halfWidth = (room.MapWidth * AuthorityScale) / 2;
            var halfHeight = (room.MapHeight * AuthorityScale) / 2;
            var geometry = new LabelRect(
                centerX - halfWidth,
                centerX + halfWidth,
                centerY - halfHeight,
                centerY + halfHeight);
            return label.Overlaps(geometry, padding);
        });

    private static (double X, double Y) Anchor(Room room, Side side) =>
        (
            side switch
            {
                Side.Left => ToDeck(room.MapX - (room.MapWidth / 2)),
                Side.Right => ToDeck(room.MapX + (room.MapWidth / 2)),
                _ => ToDeck(room.MapX)
            },
            side switch
            {
                Side.Top => ToDeck(room.MapY - (room.MapHeight / 2)),
                Side.Bottom => ToDeck(room.MapY + (room.MapHeight / 2)),
                _ => ToDeck(room.MapY)
            });

    private static Side ChooseSide(Room room) =>
        PreferredSides(room).First();

    private static double[] Spread(
        IReadOnlyList<double> desired,
        double spacing,
        double minimum,
        double maximum)
    {
        if (desired.Count == 0)
            return [];

        if (desired.Count == 1)
            return [Math.Clamp(desired[0], minimum, maximum)];

        var positions = desired
            .Select(value => Math.Clamp(value, minimum, maximum))
            .ToArray();

        for (var index = 1; index < positions.Length; index++)
        {
            positions[index] = Math.Max(
                positions[index],
                positions[index - 1] + spacing);
        }

        if (positions[^1] > maximum)
        {
            positions[^1] = maximum;
            for (var index = positions.Length - 2; index >= 0; index--)
            {
                positions[index] = Math.Min(
                    positions[index],
                    positions[index + 1] - spacing);
            }
        }

        if (positions[0] < minimum)
        {
            positions[0] = minimum;
            for (var index = 1; index < positions.Length; index++)
            {
                positions[index] = Math.Max(
                    positions[index],
                    positions[index - 1] + spacing);
            }
        }

        return positions;
    }

    private sealed record Assignment(Room Room, Side Side);

    private readonly record struct LabelRect(
        double Left,
        double Right,
        double Top,
        double Bottom)
    {
        public bool IsInsideDeck =>
            Left >= .5 && Right <= 99.5 && Top >= .5 && Bottom <= 99.5;

        public static LabelRect FromCenter(double x, double y) =>
            new(
                x - LabelHalfWidth,
                x + LabelHalfWidth,
                y - LabelHalfHeight,
                y + LabelHalfHeight);

        public bool Overlaps(LabelRect other, double padding) =>
            Left - padding < other.Right
            && Right + padding > other.Left
            && Top - padding < other.Bottom
            && Bottom + padding > other.Top;
    }
}
