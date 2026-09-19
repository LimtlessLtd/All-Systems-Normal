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

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var vacuumDepths = EnvironmentSystem.FindVacuumDepths(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            if (!vacuumDepths.ContainsKey(npc.CurrentRoomId))
            {
                continue;
            }

            var room = state.Facility.Rooms[npc.CurrentRoomId];

            if (room.PressureKpa >= EjectionPressureKpa)
            {
                npc.Fear = Math.Clamp(npc.Fear + 18, 0, 100);
                npc.Stress = Math.Clamp(npc.Stress + 15, 0, 100);
                continue;
            }

            Eject(state, npc, room);
        }
    }

    private static void Eject(GameState state, Npc npc, Room room)
    {
        npc.Health = 0;
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
