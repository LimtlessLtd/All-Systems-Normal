using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Resolves human consequences once EnvironmentSystem has physically changed
/// compartment pressure. Opening the hatch does not directly target a person;
/// people are affected because they are physically present in a compartment
/// connected to vacuum.
/// </summary>
public sealed class VacuumConsequenceSystem
{
    private const double EjectionPressureKpa = 25;

    /// <summary>
    /// Only the compartment open to space (depth 0) and the room directly
    /// behind its open hatch (depth 1) have outflow violent enough to carry a
    /// person out. Further away the air drains through several hatches; people
    /// there suffer the ordinary low-pressure/hypoxia harm in
    /// <see cref="SimulationEngine"/> instead, which leaves time to flee or seal
    /// a door and leaves a body aboard if they don't make it. Before this, one
    /// fire-driven hull breach "ejected" a whole crew from eight different
    /// rooms within two minutes.
    /// </summary>
    public const int MaxEjectionDepth = 1;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        StationHazardSystem.HullRepairRules.UpdateEmergencySuits(state);

        var vacuumDepths = EnvironmentSystem.FindVacuumDepths(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            if (!vacuumDepths.TryGetValue(npc.CurrentRoomId, out var depth))
            {
                continue;
            }

            var room = state.Facility.Rooms[npc.CurrentRoomId];

            if (StationHazardSystem.HullRepairRules.HasEmergencyPressureProtection(npc))
            {
                StatLogSystem.Set(state, npc, CrewStat.Fear, Math.Clamp(npc.Fear + 18, 0, 100), "decompression");
                StatLogSystem.Set(state, npc, CrewStat.Stress, Math.Clamp(npc.Stress + 15, 0, 100), "decompression");
                continue;
            }

            if (room.PressureKpa >= EjectionPressureKpa || depth > MaxEjectionDepth)
            {
                StatLogSystem.Set(state, npc, CrewStat.Fear, Math.Clamp(npc.Fear + 18, 0, 100), "decompression");
                StatLogSystem.Set(state, npc, CrewStat.Stress, Math.Clamp(npc.Stress + 15, 0, 100), "decompression");
                continue;
            }

            Eject(state, npc, room);
        }
    }

    private static void Eject(GameState state, Npc npc, Room room)
    {
        StatLogSystem.Set(state, npc, CrewStat.Health, 0, "ejected into space");
        npc.IsPresent = false;
        npc.CauseOfDeath =
            $"Ejected into space during decompression of {room.Name}; no body remains aboard.";
        npc.Intent = null;
        npc.Movement = null;
        npc.RoutineUntil = TimeSpan.Zero;
        npc.Bubble = null;
        npc.PendingBubbles.Clear();
        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            null,
            "Lost to vacuum.");

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Critical,
            npc.Id.ToString(),
            room.Id);

        Log(
            state,
            $"CRITICAL: {npc.Name} is swept through the decompressed airlock path and lost to space. No body remains aboard.");
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
