using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Canonical physical footprints for the standard modules installed in most
/// compartments. Fixture geometry is stored as a percentage of its room, so a
/// fixed percentage renders at a different real size in every differently
/// sized room. These footprints are in station map units instead: the fixture
/// packer converts them into each room's local percentages, so the same module
/// family is physically identical wherever it is installed.
/// </summary>
public static class StandardModuleFootprints
{
    /// <summary>
    /// Length runs along the bulkhead the module is mounted on; depth is how
    /// far it protrudes into the compartment.
    /// </summary>
    public readonly record struct Footprint(double Length, double Depth);

    public static bool TryGet(StationSystemKind kind, out Footprint footprint)
    {
        footprint = kind switch
        {
            StationSystemKind.Lighting => new Footprint(1.3, 0.9),
            StationSystemKind.ClimateControl => new Footprint(1.5, 1.0),
            StationSystemKind.Ventilation => new Footprint(1.5, 0.9),
            StationSystemKind.Door => new Footprint(1.1, 0.8),
            _ => default
        };

        return footprint.Length > 0;
    }

    public static bool TryGet(
        IReadOnlyDictionary<string, StationDevice> devices,
        RoomFixture fixture,
        out Footprint footprint)
    {
        footprint = default;
        return fixture.DeviceId is { } deviceId
            && devices.TryGetValue(deviceId, out var device)
            && TryGet(device.Kind, out footprint);
    }

    /// <summary>
    /// The footprint expressed in <paramref name="room"/>'s local 0-100
    /// coordinates. A module on a top/bottom bulkhead runs along X; one on a
    /// side bulkhead runs along Y.
    /// </summary>
    public static (double Width, double Height) LocalSize(
        Room room,
        Footprint footprint,
        bool horizontalWall) =>
        horizontalWall
            ? (footprint.Length / room.MapWidth * 100, footprint.Depth / room.MapHeight * 100)
            : (footprint.Depth / room.MapWidth * 100, footprint.Length / room.MapHeight * 100);
}
