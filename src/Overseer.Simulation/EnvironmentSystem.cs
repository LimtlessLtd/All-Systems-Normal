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

    /// <summary>Respiration of someone physically asleep, relative to awake.</summary>
    public const double SleepingRespirationFactor = 0.6;

    /// <summary>
    /// Owner idea #73: a near-vacuum compartment has effectively no atmosphere
    /// left to hold or transfer heat, so it cools toward deep-space cold instead
    /// of any powered/passive equilibrium. Far colder than any pressurised room's
    /// floor; this is a gameplay approximation, not an exact 0 K model.
    /// </summary>
    private const double VacuumTemperatureFloorC = -90;

    /// <summary>
    /// Pressure, not oxygen concentration, determines whether the compartment is
    /// effectively vacuum. An oxygen-depleted but still pressurised CO2-rich room
    /// must continue to use ordinary thermal/climate behaviour.
    /// </summary>
    private const double VacuumPressureThresholdKpa = 1;

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (delta <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta));

        var minutes = delta.TotalMinutes;
        var livingCrew = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent)
            .ToList();
        var totalCrew = livingCrew.Count;
        var vacuumDepths = FindVacuumDepths(state);

        if (state.LifeSupport.OxygenGeneratorOnline
            && state.LifeSupport.IsOnline)
        {
            state.LifeSupport.OxygenReservePercent = Math.Min(
                100,
                state.LifeSupport.OxygenReservePercent
                + (0.018 * minutes)
                - (totalCrew * 0.0012 * minutes));
        }
        else
        {
            state.LifeSupport.OxygenReservePercent = Math.Max(
                0,
                state.LifeSupport.OxygenReservePercent
                - ((0.006 + (totalCrew * 0.0018)) * minutes));
        }

        if (state.LifeSupport.WaterRecyclerOnline && state.LifeSupport.IsOnline)
        {
            state.LifeSupport.WaterReservePercent = Math.Min(
                100,
                state.LifeSupport.WaterReservePercent
                + (0.012 * minutes)
                - (totalCrew * 0.001 * minutes));
        }
        else
        {
            state.LifeSupport.WaterReservePercent = Math.Max(
                0,
                state.LifeSupport.WaterReservePercent
                - ((0.004 + (totalCrew * 0.0015)) * minutes));
        }

        foreach (var room in state.Facility.Rooms.Values)
        {
            var roomCrew = livingCrew
                .Where(npc => npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var occupants = roomCrew.Count;

            // People asleep breathe at a resting rate. Without this, a
            // normally ventilated Crew Quarters full of sleepers crossed the
            // CO2 danger line within the hour, and the whole room evacuated
            // and came back every ~2.5h all night.
            var breathingLoad = roomCrew.Sum(npc =>
                SimulationEngine.IsPhysicallyAsleep(state, npc)
                    ? SleepingRespirationFactor
                    : 1d);

            if (vacuumDepths.TryGetValue(room.Id, out var vacuumDepth))
            {
                TickVacuum(room, vacuumDepth, minutes);
            }
            else
            {
                TickAtmosphere(state, room, breathingLoad, minutes);
            }

            TickTemperature(state, room, occupants, minutes);
            room.PressureKpa = Math.Clamp(room.PressureKpa, 0, NominalPressure);
        }
    }

    /// <summary>
    /// Returns every compartment with an unbroken open-hatch path to space.
    /// Depth zero is the room whose exterior hatch is open; increasing depth
    /// means the decompression wave has crossed another open internal hatch.
    /// </summary>
    public static IReadOnlyDictionary<string, int> FindVacuumDepths(GameState state)
    {
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        foreach (var source in state.Facility.Rooms.Values.Where(room =>
                     room.HasHullBreach
                     || (room.HasExteriorHatch && room.ExteriorHatchOpen)))
        {
            depths[source.Id] = 0;
            queue.Enqueue(source.Id);
        }

        while (queue.TryDequeue(out var currentRoomId))
        {
            var currentDepth = depths[currentRoomId];

            foreach (var door in state.Facility.Doors.Where(door =>
                         IsAtmosphericallyOpen(door)
                         && (door.RoomAId.Equals(currentRoomId, StringComparison.OrdinalIgnoreCase)
                             || door.RoomBId.Equals(currentRoomId, StringComparison.OrdinalIgnoreCase))))
            {
                var next = door.RoomAId.Equals(currentRoomId, StringComparison.OrdinalIgnoreCase)
                    ? door.RoomBId
                    : door.RoomAId;

                if (depths.ContainsKey(next))
                {
                    continue;
                }

                depths[next] = currentDepth + 1;
                queue.Enqueue(next);
            }
        }

        return depths;
    }

    private static bool IsAtmosphericallyOpen(Door door) =>
        door.IsOpen || door.IsManuallyOverridden;

    private static void TickVacuum(Room room, int depth, double minutes)
    {
        var previousPressure = Math.Max(room.PressureKpa, 0.001);

        // Direct exposure is violent; pressure loss propagates more slowly
        // through each additional open hatch so the player has a short window
        // to contain an accidentally opened airlock.
        var pressureLossPerMinute = 80d / (1 + (depth * 0.75));
        room.PressureKpa = Math.Max(
            0,
            room.PressureKpa - (pressureLossPerMinute * minutes));

        var pressureRatio = Math.Clamp(room.PressureKpa / previousPressure, 0, 1);
        room.OxygenPercent *= pressureRatio;
        room.CarbonDioxidePercent *= pressureRatio;
        room.OxygenPercent = Math.Clamp(room.OxygenPercent, 0, 23);
        room.CarbonDioxidePercent = Math.Clamp(room.CarbonDioxidePercent, 0, 10);

        room.TemperatureC = MoveToward(
            room.TemperatureC,
            -20,
            (0.75 / (1 + depth)) * minutes);
    }

    private static void TickAtmosphere(
        GameState state,
        Room room,
        double breathingLoad,
        double minutes)
    {
        var ventilationActive =
            state.LifeSupport.IsOnline
            && state.LifeSupport.OxygenReservePercent > 0
            && room.IsPowered
            && room.VentilationEnabled
            // The airlock pressure pump owns chamber pressure during an
            // intentional depressurization cycle; the central air loop must
            // not simultaneously fight that operation.
            && room.AirlockCycleMode != AirlockCycleMode.Depressurizing;

        if (ventilationActive)
        {
            room.OxygenPercent = MoveToward(
                room.OxygenPercent,
                NominalOxygen,
                0.075 * minutes);

            var scrubberFactor = state.LifeSupport.CarbonScrubberOnline
                ? Math.Clamp(
                    state.LifeSupport.ScrubberEfficiencyPercent / 100d,
                    0,
                    1)
                : 0;

            room.CarbonDioxidePercent = MoveToward(
                room.CarbonDioxidePercent,
                NominalCo2,
                0.055 * scrubberFactor * minutes);

            var repressurisationRate = room.Type == RoomType.Airlock ? 14 : 6;
            room.PressureKpa = MoveToward(
                room.PressureKpa,
                NominalPressure,
                repressurisationRate * minutes);
        }

        if (breathingLoad > 0)
        {
            // Gameplay-scaled consumption so isolating a populated room becomes
            // meaningful on session timescales without becoming instant death.
            room.OxygenPercent -= breathingLoad * 0.012 * minutes;
            room.CarbonDioxidePercent += breathingLoad * 0.009 * minutes;
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
        if (room.PressureKpa <= VacuumPressureThresholdKpa)
        {
            // Near-vacuum means there is effectively no atmosphere left to hold
            // heat: climate control, passive room-type equilibrium and the
            // central air loop all lose their grip, and even occupant body heat
            // can't keep up. Oxygen concentration alone is not a vacuum signal:
            // a pressurised inert/CO2-rich room still has gas and thermal mass.
            room.TemperatureC = MoveToward(
                room.TemperatureC,
                VacuumTemperatureFloorC,
                0.5 * minutes);
            room.TemperatureC = Math.Clamp(room.TemperatureC, VacuumTemperatureFloorC, 60);
            return;
        }

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
