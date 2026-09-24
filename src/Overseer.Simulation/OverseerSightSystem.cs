using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// What Overseer knows about a room's fire. <see cref="IsLive"/> is true while
/// the room has a visual feed; otherwise this is the last camera sighting,
/// seen at <see cref="SeenAt"/> (null if the cameras never saw the room).
/// </summary>
public readonly record struct FireSighting(double Intensity, TimeSpan? SeenAt, bool IsLive)
{
    public bool IsBurning => Intensity > 0;
}

/// <summary>
/// Owner idea #23: Overseer knows a fire only through its cameras. A room
/// with no visual feed (unpowered, camera down or unreachable) is a genuine
/// blind spot: the player sees the last camera sighting, not live truth.
/// Atmosphere readings (O₂, temperature, smoke) come from their own sensor
/// channels and are unaffected.
/// </summary>
public static class OverseerSightSystem
{
    /// <summary>Records what every room with a visual feed shows right now.</summary>
    public static void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var room in state.Facility.Rooms.Values)
        {
            if (!room.HasVisualFeed)
                continue;

            room.ObservedFireIntensity = room.FireIntensity;
            room.FireObservedAt = state.Elapsed;
        }
    }

    public static FireSighting Fire(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return room.HasVisualFeed
            ? new FireSighting(room.FireIntensity, null, true)
            : room.FireObservedAt is { } seenAt
                ? new FireSighting(room.ObservedFireIntensity, seenAt, false)
                : new FireSighting(0, null, false);
    }
}
