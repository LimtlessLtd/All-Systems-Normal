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
