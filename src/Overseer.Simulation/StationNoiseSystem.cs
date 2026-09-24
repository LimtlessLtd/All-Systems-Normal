using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// One machine's contribution to a room's noise.
/// </summary>
public sealed record NoiseSource(StationDevice Device, string RoomId, double Level);

/// <summary>
/// Owner idea #65: machinery noise and vibration. Running mechanical
/// equipment is audible, worn equipment rattles louder, and a noisy room makes
/// sleep and rest less restorative for the people in it. C# only decides how
/// loud a room physically is and what that costs; whether anyone shuts a
/// machine down, closes a hatch or sleeps elsewhere is left to cognition,
/// through existing affordances (DisconnectDevice, hatch control, moving).
/// </summary>
public static class StationNoiseSystem
{
    /// <summary>
    /// Room noise at or above this disturbs sleep. A healthy Crew Quarters
    /// (air handler + climate unit, quiet corridor outside) stays well below it.
    /// </summary>
    public const double DisturbingAt = 30;

    /// <summary>Noise at which sleep/rest recovery bottoms out.</summary>
    public const double MaxDisturbanceAt = 70;

    /// <summary>The least restorative sleep gets, however loud the room.</summary>
    public const double MinRecoveryFactor = 0.3;

    /// <summary>Sound through an open hatch reaches the next room at this share.</summary>
    public const double OpenHatchTransmission = 0.5;

    /// <summary>A unit at zero condition would be this many times its healthy level.</summary>
    public const double MaxWearAmplification = 4;

    /// <summary>Healthy running noise by equipment type; quiet equipment is 0.</summary>
    public static double BaseLevel(StationSystemKind kind) => kind switch
    {
        StationSystemKind.Reactor => 30,
        StationSystemKind.PowerGenerator => 26,
        StationSystemKind.CoolantPump => 22,
        StationSystemKind.LifeSupport => 18,
        StationSystemKind.WaterRecycler => 16,
        StationSystemKind.OxygenGenerator => 16,
        StationSystemKind.CarbonScrubber => 14,
        StationSystemKind.Ventilation => 10,
        StationSystemKind.ClimateControl => 8,
        StationSystemKind.GalleyEquipment => 8,
        _ => 0
    };

    /// <summary>
    /// How loud this device is right now. Stopped, failed or unpowered
    /// machinery is silent (the same "running" rule the map uses to animate
    /// it). Below its degraded threshold, noise climbs steadily with wear.
    /// </summary>
    public static double DeviceLevel(GameState state, StationDevice device)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(device);

        var baseLevel = BaseLevel(device.Kind);
        if (baseLevel <= 0 || !IsRunning(state, device))
            return 0;

        if (!device.IsDegraded || device.DegradedAt <= 0)
            return baseLevel;

        var wear = Math.Clamp((device.DegradedAt - device.Condition) / device.DegradedAt, 0, 1);
        return baseLevel * (1 + ((MaxWearAmplification - 1) * wear));
    }

    /// <summary>
    /// Every machine audible in a room: its own running machinery at full
    /// level, plus machinery in rooms joined to it by an open hatch at
    /// <see cref="OpenHatchTransmission"/>. A closed hatch blocks the sound.
    /// </summary>
    public static IReadOnlyList<NoiseSource> Sources(GameState state, string roomId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sources = new List<NoiseSource>();
        AddRoom(state, roomId, 1, sources);

        foreach (var door in state.Facility.Doors.Where(door =>
                     door.IsOpen
                     && (door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
                         || door.RoomBId.Equals(roomId, StringComparison.OrdinalIgnoreCase))))
        {
            var neighbour = door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId
                : door.RoomAId;
            if (!neighbour.Equals(roomId, StringComparison.OrdinalIgnoreCase))
                AddRoom(state, neighbour, OpenHatchTransmission, sources);
        }

        return sources
            .OrderByDescending(source => source.Level)
            .ThenBy(source => source.Device.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static double RoomLevel(GameState state, string roomId) =>
        Sources(state, roomId).Sum(source => source.Level);

    /// <summary>
    /// Multiplier on sleep/rest recovery for someone resting in a room this
    /// loud: 1 below <see cref="DisturbingAt"/>, falling linearly to
    /// <see cref="MinRecoveryFactor"/> at <see cref="MaxDisturbanceAt"/>.
    /// </summary>
    public static double RestRecoveryFactor(double noiseLevel)
    {
        if (noiseLevel < DisturbingAt)
            return 1;

        var severity = Math.Clamp(
            (noiseLevel - DisturbingAt) / (MaxDisturbanceAt - DisturbingAt),
            0,
            1);
        return 1 - ((1 - MinRecoveryFactor) * severity);
    }

    public static bool IsDisturbing(double noiseLevel) => noiseLevel >= DisturbingAt;

    private static void AddRoom(GameState state, string roomId, double share, List<NoiseSource> sources)
    {
        foreach (var device in state.Devices.Values.Where(device =>
                     device.RoomId.Equals(roomId, StringComparison.OrdinalIgnoreCase)))
        {
            var level = DeviceLevel(state, device) * share;
            if (level > 0)
                sources.Add(new NoiseSource(device, device.RoomId, level));
        }
    }

    private static bool IsRunning(GameState state, StationDevice device)
    {
        if (!device.IsOperational)
            return false;

        // Generators and the reactor are power sources; they run whatever
        // their own compartment's feed is doing.
        if (device.Kind is StationSystemKind.Reactor or StationSystemKind.PowerGenerator)
            return true;

        return !state.Facility.Rooms.TryGetValue(device.RoomId, out var room) || room.IsPowered;
    }
}
