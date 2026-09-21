using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Final lifecycle invariant pass. No ordinary casualty may silently vanish:
/// every dead crew member has a cause, an event-log entry and, unless explicitly
/// ejected into space, a body that remains aboard for cameras and inspection.
/// </summary>
public sealed class CrewLifecycleAuditSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var npc in state.Crew.Where(npc => !npc.IsAlive))
        {
            npc.CauseOfDeath ??= InferCause(state, npc);

            var explicitlyLostToSpace =
                npc.CauseOfDeath.Contains("Ejected into space", StringComparison.OrdinalIgnoreCase)
                || npc.CauseOfDeath.Contains("no body remains", StringComparison.OrdinalIgnoreCase)
                || npc.CauseOfDeath.Contains("lost to space", StringComparison.OrdinalIgnoreCase);

            if (!explicitlyLostToSpace)
                npc.IsPresent = true;

            if (npc.LastDeathAnnouncementAt is not null)
                continue;

            var timestamp = $"T+{state.Elapsed:hh\\:mm}";
            var alreadyLoggedThisTurn = state.EventLog.Take(40).Any(entry =>
                entry.StartsWith(timestamp, StringComparison.OrdinalIgnoreCase)
                && entry.Contains(npc.Name, StringComparison.OrdinalIgnoreCase)
                && (entry.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase)
                    || entry.Contains("died", StringComparison.OrdinalIgnoreCase)
                    || entry.Contains("lost to space", StringComparison.OrdinalIgnoreCase)));

            if (!alreadyLoggedThisTurn)
            {
                state.EventLog.Insert(
                    0,
                    $"{timestamp}: CRITICAL: {npc.Name} has died — {npc.CauseOfDeath}");
                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Critical,
                    npc.Id.ToString(),
                    npc.CurrentRoomId);
            }

            npc.LastDeathAnnouncementAt = state.Elapsed;
            npc.Intent = null;
            npc.Movement = null;
            npc.PlannedDestinationRoomId = null;
            npc.RoutineUntil = TimeSpan.Zero;
            npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Deceased.");
        }
    }

    private static string InferCause(GameState state, Npc npc)
    {
        if (!state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
            return "Died from unresolved critical injuries.";

        if (room.FireIntensity > 0 || room.SmokePercent >= 35)
            return $"Died during the fire in {room.Name}.";
        if (room.PressureKpa < 55)
            return "Died from decompression.";
        if (room.OxygenPercent < 17)
            return "Died from oxygen deprivation.";
        if (room.CarbonDioxidePercent > 3)
            return "Died from carbon dioxide exposure.";
        if (npc.Hunger > 95)
            return "Died from starvation.";

        return "Died from unresolved critical injuries.";
    }
}
