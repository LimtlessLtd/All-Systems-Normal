using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #76: a compartment fire starts at a point and its front grows
/// outward with its intensity. The front is room-local (percent of each room
/// axis) and derived only from the fire's origin and intensity, so the map
/// draws exactly what burns.
/// </summary>
public static class FireFrontRules
{
    /// <summary>Front radius at the smallest fire, in room-local percent.</summary>
    public const double IgnitionRadius = 10;

    /// <summary>Front growth per point of fire intensity, in room-local percent.</summary>
    public const double RadiusPerIntensity = 1.8;

    /// <summary>
    /// Front radius in room-local percent. A newly lit fire (~16-38) covers a
    /// patch around its origin; an inferno (75+) reaches every corner.
    /// </summary>
    public static double FrontRadius(double intensity) =>
        intensity <= 0 ? 0 : IgnitionRadius + (Math.Clamp(intensity, 0, 100) * RadiusPerIntensity);

    public static void Ignite(Room room, double originX, double originY, double intensity)
    {
        ArgumentNullException.ThrowIfNull(room);
        room.FireIntensity = intensity;
        room.FireOriginX = Math.Clamp(originX, 0, 100);
        room.FireOriginY = Math.Clamp(originY, 0, 100);
    }

    /// <summary>Forgets the origin of a fire that has gone out.</summary>
    public static void ClearIfOut(Room room)
    {
        if (room.FireIntensity <= 0)
        {
            room.FireOriginX = null;
            room.FireOriginY = null;
        }
    }

    /// <summary>
    /// Whether a room-local point is inside the burning front. A fire with no
    /// recorded origin fills the whole room.
    /// </summary>
    public static bool IsInsideFront(Room room, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.FireIntensity <= 0)
            return false;

        if (room.FireOriginX is not { } originX || room.FireOriginY is not { } originY)
            return true;

        var dx = x - originX;
        var dy = y - originY;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= FrontRadius(room.FireIntensity);
    }

    /// <summary>
    /// How far beyond the burning front a fire-fighter can still put
    /// suppressant on it, in room-local percent.
    /// </summary>
    public const double SuppressionReach = 12;

    /// <summary>
    /// Whether someone standing at this room-local point can reach the front
    /// with suppressant. A fire with no recorded origin fills the room, so
    /// anywhere in it is in reach.
    /// </summary>
    public static bool CanReachFront(Room room, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.FireIntensity <= 0)
            return false;

        if (room.FireOriginX is not { } originX || room.FireOriginY is not { } originY)
            return true;

        var dx = x - originX;
        var dy = y - originY;
        return Math.Sqrt((dx * dx) + (dy * dy))
            <= FrontRadius(room.FireIntensity) + SuppressionReach;
    }

    /// <summary>
    /// Where a fire-fighter coming from (x, y) stands to attack the front:
    /// on the line from the origin towards them, half their reach outside the
    /// flames. Someone inside the front backs out to it, and someone further
    /// away closes in. Null when the fire fills the room or has gone out.
    /// </summary>
    public static (double X, double Y)? AttackPoint(Room room, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.FireIntensity <= 0
            || room.FireOriginX is not { } originX
            || room.FireOriginY is not { } originY)
        {
            return null;
        }

        var dx = x - originX;
        var dy = y - originY;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            // Standing on the origin: step out towards the room centre.
            dx = 50 - originX;
            dy = 50 - originY;
            length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                (dx, dy, length) = (0, 1, 1);
            }
        }

        var standOff = FrontRadius(room.FireIntensity) + (SuppressionReach / 2);
        return (
            Math.Clamp(originX + (dx / length * standOff), 8, 92),
            Math.Clamp(originY + (dy / length * standOff), 8, 92));
    }

    /// <summary>One flame the map draws: a room-local point inside the front and a size factor.</summary>
    public readonly record struct FlameSprite(double X, double Y, double Scale);

    /// <summary>Most flames drawn in one room, nearest the origin first.</summary>
    public const int MaxFlameSprites = 32;

    /// <summary>Spacing between neighbouring flames, in station-map units.</summary>
    public const double FlameSpacingMapUnits = 2.3;

    private const double FlameWallMargin = 6;
    private const double MinFlameStep = 8;
    private const double MaxFlameStep = 45;

    /// <summary>
    /// Owner idea #100: where the map draws flames. Each sprite sits on a fixed
    /// lattice anchored at the fire's origin and spaced in physical map units,
    /// and only lattice points inside the front (<see cref="IsInsideFront"/>) and
    /// clear of the walls burn. The lattice never moves, so as intensity grows
    /// the front only gains flames, nearest the origin first. Flames are larger
    /// for hotter fires and near the origin. A fire with no recorded origin
    /// fills the room, so its lattice is anchored at the centre. Presentation
    /// only; nothing reads this back into the simulation.
    /// </summary>
    public static IReadOnlyList<FlameSprite> FlameSprites(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.FireIntensity <= 0)
            return [];

        var hasOrigin = room.FireOriginX is not null && room.FireOriginY is not null;
        var anchorX = Math.Clamp(room.FireOriginX ?? 50, FlameWallMargin, 100 - FlameWallMargin);
        var anchorY = Math.Clamp(room.FireOriginY ?? 50, FlameWallMargin, 100 - FlameWallMargin);
        var stepX = FlameStep(room.MapWidth);
        var stepY = FlameStep(room.MapHeight);
        var reach = hasOrigin ? FrontRadius(room.FireIntensity) : 71;
        var heat = Math.Clamp(room.FireIntensity, 0, 100) / 100;

        var candidates = new List<(double X, double Y, double Distance, int Row, int Column)>();
        var rows = (int)Math.Ceiling(100 / stepY);
        var columns = (int)Math.Ceiling(100 / stepX) + 1;
        for (var row = -rows; row <= rows; row++)
        {
            // Alternate rows are offset half a step, so flames read as a
            // spreading patch rather than a grid.
            var offset = (row & 1) == 0 ? 0 : stepX / 2;
            var y = anchorY + (row * stepY);
            if (y < FlameWallMargin || y > 100 - FlameWallMargin)
                continue;

            for (var column = -columns; column <= columns; column++)
            {
                // A fixed per-point jitter (never the origin's own flame)
                // breaks up the lattice; it is a function of the lattice cell
                // only, so a flame never moves while the fire burns.
                var (jitterX, jitterY) = row == 0 && column == 0
                    ? (0d, 0d)
                    : (Jitter(row, column, 17) * stepX * 0.3, Jitter(row, column, 31) * stepY * 0.3);
                var x = anchorX + offset + (column * stepX) + jitterX;
                var jitteredY = y + jitterY;
                if (jitteredY < FlameWallMargin || jitteredY > 100 - FlameWallMargin)
                    continue;

                if (x < FlameWallMargin || x > 100 - FlameWallMargin || !IsInsideFront(room, x, jitteredY))
                    continue;

                var dx = x - anchorX;
                var dy = jitteredY - anchorY;
                candidates.Add((x, jitteredY, Math.Sqrt((dx * dx) + (dy * dy)), row, column));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Row)
            .ThenBy(candidate => candidate.Column)
            .Take(MaxFlameSprites)
            .Select(candidate => new FlameSprite(
                candidate.X,
                candidate.Y,
                (0.75 + (0.55 * heat)) * (1 - (0.35 * Math.Clamp(candidate.Distance / reach, 0, 1)))))
            .ToList();
    }

    /// <summary>A stable pseudo-random offset in [-1, 1] for one lattice cell.</summary>
    private static double Jitter(int row, int column, int salt)
    {
        unchecked
        {
            var hash = (uint)((row * 73856093) ^ (column * 19349663) ^ (salt * 83492791));
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return ((hash % 2001) / 1000d) - 1;
        }
    }

    private static double FlameStep(double mapExtent) =>
        Math.Clamp(FlameSpacingMapUnits / Math.Max(mapExtent, 0.01) * 100, MinFlameStep, MaxFlameStep);

    /// <summary>Where a fire starts on a machine: its fixture, else the room centre.</summary>
    public static (double X, double Y) MachineOrigin(Room room, StationDevice device)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(device);
        return LocalMovementSystem.FixtureForDevice(room, device.Kind) is { } fixture
            ? (fixture.X, fixture.Y)
            : (50, 50);
    }

    /// <summary>
    /// Where a fire enters a room through a hatch: the shared wall portal, in
    /// the receiving room's local coordinates.
    /// </summary>
    public static (double X, double Y) PortalOrigin(Room source, Room receiving)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(receiving);

        StationPortal portal;
        try
        {
            portal = StationGeometry.FindSharedPortal(source, receiving);
        }
        catch (InvalidOperationException)
        {
            // Hand-built test stations can join rooms that share no wall.
            return (50, 50);
        }

        return (
            Math.Clamp(50 + ((portal.X - receiving.MapX) / receiving.MapWidth * 100), 0, 100),
            Math.Clamp(50 + ((portal.Y - receiving.MapY) / receiving.MapHeight * 100), 0, 100));
    }
}
