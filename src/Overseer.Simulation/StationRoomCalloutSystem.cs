using Overseer.Domain;

namespace Overseer.Simulation;

public sealed record StationRoomCallout(
    string RoomId,
    double LabelX,
    double LabelY,
    double AnchorX,
    double AnchorY,
    string Side);

/// <summary>
/// Presentation-only room telemetry layout. Functional-room callouts live in
/// the outer 10% margin of the virtual deck while authoritative geometry lives
/// in the central 80%, so status UI never obscures rooms or corridors.
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
            .OrderBy(room => room.Id, StringComparer.Ordinal)
            .ToList();

        var assignments = rooms
            .Select(room => new Assignment(room, ChooseSide(room)))
            .ToList();

        var result = new List<StationRoomCallout>(assignments.Count);

        foreach (var side in Enum.GetValues<Side>())
        {
            var group = assignments
                .Where(item => item.Side == side)
                .OrderBy(item => side is Side.Left or Side.Right
                    ? item.Room.MapY
                    : item.Room.MapX)
                .ToList();

            if (group.Count == 0)
                continue;

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
                var x = side switch
                {
                    Side.Left => HorizontalLabelXLeft,
                    Side.Right => HorizontalLabelXRight,
                    _ => positioned[index]
                };
                var y = side switch
                {
                    Side.Top => VerticalLabelYTop,
                    Side.Bottom => VerticalLabelYBottom,
                    _ => positioned[index]
                };

                var anchorX = side switch
                {
                    Side.Left => ToDeck(room.MapX - (room.MapWidth / 2)),
                    Side.Right => ToDeck(room.MapX + (room.MapWidth / 2)),
                    _ => ToDeck(room.MapX)
                };
                var anchorY = side switch
                {
                    Side.Top => ToDeck(room.MapY - (room.MapHeight / 2)),
                    Side.Bottom => ToDeck(room.MapY + (room.MapHeight / 2)),
                    _ => ToDeck(room.MapY)
                };

                result.Add(new StationRoomCallout(
                    room.Id,
                    x,
                    y,
                    anchorX,
                    anchorY,
                    side.ToString().ToLowerInvariant()));
            }
        }

        return result
            .OrderBy(callout => callout.RoomId, StringComparer.Ordinal)
            .ToList();
    }

    public static double ToDeck(double authoritativePercent) =>
        AuthorityInsetPercent + (authoritativePercent * AuthorityScale);

    private static Side ChooseSide(Room room)
    {
        var left = room.MapX - (room.MapWidth / 2);
        var right = 100 - (room.MapX + (room.MapWidth / 2));
        var top = room.MapY - (room.MapHeight / 2);
        var bottom = 100 - (room.MapY + (room.MapHeight / 2));

        var best = new[]
        {
            (Side.Left, left),
            (Side.Right, right),
            (Side.Top, top),
            (Side.Bottom, bottom)
        };

        return best
            .OrderBy(item => item.Item2)
            .ThenBy(item => item.Item1)
            .First().Item1;
    }

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
}
