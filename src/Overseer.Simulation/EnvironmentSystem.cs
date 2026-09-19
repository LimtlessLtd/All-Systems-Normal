using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic station environmental simulation. This system owns physical
/// atmosphere/climate consequences only; it does not choose NPC goals.
/// </summary>
public sealed class EnvironmentSystem
{
    private const double NominalOxygen = 20.9;
    private const double NominalCo2 = 0.04;
    private const double NominalPressure = 101.3;

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (delta <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta));

        var minutes = delta.TotalMinutes;
        var livingCrew = state.Crew.Where(npc => npc.IsAlive).ToList();
        var totalCrew = livingCrew.Count;

        if (state.LifeSupport.IsOnline && state.LifeSupport.OxygenReservePercent > 0)
        {
            state.LifeSupport.OxygenReservePercent = Math.Max(
                0,
                state.LifeSupport.OxygenReservePercent
                - (totalCrew * 0.0012 * minutes));
        }

        foreach (var room in state.Facility.Rooms.Values)
        {
            var occupants = livingCrew.Count(npc =>
                npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase));

            TickAtmosphere(state, room, occupants, minutes);
            TickTemperature(state, room, occupants, minutes);

            // Pressure is currently a monitored foundation for later breaches,
            // airlocks and decompression. Ordinary life-support loss does not
            // magically remove room pressure.
            room.PressureKpa = Math.Clamp(room.PressureKpa, 0, NominalPressure);
        }
    }

    private static void TickAtmosphere(
        GameState state,
        Room room,
        int occupants,
        double minutes)
    {
        var ventilationActive =
            state.LifeSupport.IsOnline
            && state.LifeSupport.OxygenReservePercent > 0
            && room.IsPowered
            && room.VentilationEnabled;

        if (ventilationActive)
        {
            room.OxygenPercent = MoveToward(
                room.OxygenPercent,
                NominalOxygen,
                0.075 * minutes);

            var scrubberFactor = Math.Clamp(
                state.LifeSupport.ScrubberEfficiencyPercent / 100d,
                0,
                1);

            room.CarbonDioxidePercent = MoveToward(
                room.CarbonDioxidePercent,
                NominalCo2,
                0.055 * scrubberFactor * minutes);
        }

        if (occupants > 0)
        {
            // Gameplay-scaled consumption so isolating a populated room becomes
            // meaningful on session timescales without becoming instant death.
            room.OxygenPercent -= occupants * 0.012 * minutes;
            room.CarbonDioxidePercent += occupants * 0.009 * minutes;
        }

        room.OxygenPercent = Math.Clamp(room.OxygenPercent, 0, 23);
        room.CarbonDioxidePercent = Math.Clamp(room.CarbonDioxidePercent, 0.02, 10);
    }

    private static void TickTemperature(
        GameState state,
        Room room,
        int occupants,
        double minutes)
    {
        var activeClimate =
            room.IsPowered
            && room.HasTemperatureControl
            && room.TemperatureControlOnline;

        if (activeClimate)
        {
            room.TemperatureC = MoveToward(
                room.TemperatureC,
                room.TemperatureSetpointC,
                0.22 * minutes);
        }
        else
        {
            var passiveTarget = room.Type switch
            {
                RoomType.Reactor => 36,
                RoomType.Hydroponics => 15,
                _ => 11
            };

            room.TemperatureC = MoveToward(
                room.TemperatureC,
                passiveTarget,
                0.055 * minutes);
        }

        // The central air loop passively moderates spaces even when they do not
        // expose a local thermostat to Overseer.
        if (!activeClimate
            && state.LifeSupport.IsOnline
            && room.IsPowered
            && room.VentilationEnabled)
        {
            var airLoopTarget = room.Type == RoomType.Hydroponics ? 24 : 21;
            room.TemperatureC = MoveToward(
                room.TemperatureC,
                airLoopTarget,
                0.045 * minutes);
        }

        if (occupants > 0)
        {
            room.TemperatureC += Math.Min(0.02 * occupants * minutes, 0.12 * minutes);
        }

        room.TemperatureC = Math.Clamp(room.TemperatureC, -20, 60);
    }

    private static double MoveToward(double current, double target, double maximumDelta)
    {
        if (maximumDelta <= 0)
            return current;

        if (Math.Abs(target - current) <= maximumDelta)
            return target;

        return current + Math.Sign(target - current) * maximumDelta;
    }
}
