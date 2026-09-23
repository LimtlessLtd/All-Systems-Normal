using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #15, slice 1: surviving a brush with death leaves a
/// location-tagged traumatic memory, so cognition can later decide to
/// hesitate returning to that room, bring company, or push through anyway
/// under a strong enough emergency. C# only records the fact that it
/// happened and where; <c>NpcPromptBuilder</c> shows it to cognition and
/// nothing here decides how an NPC reacts to holding that memory.
///
/// Runs at the end of the tick pipeline, after every health-affecting system
/// has already had its turn this tick, so it sees each NPC's true
/// end-of-tick Health — the same value <see cref="Npc.IsAlive"/> reads.
/// </summary>
public sealed class FearConditioningSystem
{
    /// <summary>
    /// Health at or below this counts as having nearly died, distinct from
    /// the existing "seriously hurt" acute-injury threshold
    /// (<see cref="CrewTaskSystem"/> uses Health &lt;= 40 for that) — this is
    /// deliberately much closer to the actual death threshold of 0.
    /// </summary>
    public const double NearDeathHealthThreshold = 15;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            if (npc.Health > NearDeathHealthThreshold)
            {
                npc.NearDeathCrisisRecorded = false;
                continue;
            }

            if (npc.NearDeathCrisisRecorded)
                continue;

            npc.NearDeathCrisisRecorded = true;
            var room = state.Facility.Rooms[npc.CurrentRoomId];
            npc.Memories.Add(new Memory(
                $"Nearly died in {room.Name}.",
                state.Elapsed,
                0.85,
                TraumaRoomId: npc.CurrentRoomId));
        }
    }
}
