using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic scheduling for model-backed ordinary cognition. This chooses
/// who gets the next opportunity to think; it never chooses what they want.
/// </summary>
public static class MindCadenceRules
{
    /// <summary>
    /// Returns the next crew member without a live intent, scanning from the
    /// round-robin cursor. Busy crew are skipped rather than consuming the
    /// entire cognition slot. The cursor resumes after the selected person.
    /// </summary>
    public static Npc? NextIdleMind(
        IReadOnlyList<Npc> orderedCrew,
        ref int cursor)
    {
        ArgumentNullException.ThrowIfNull(orderedCrew);

        if (orderedCrew.Count == 0)
        {
            cursor = 0;
            return null;
        }

        var start = ((cursor % orderedCrew.Count) + orderedCrew.Count) % orderedCrew.Count;

        for (var offset = 0; offset < orderedCrew.Count; offset++)
        {
            var index = (start + offset) % orderedCrew.Count;
            var candidate = orderedCrew[index];

            if (candidate.Intent is not null)
            {
                continue;
            }

            cursor = (index + 1) % orderedCrew.Count;
            return candidate;
        }

        // Keep a stable starting point when everybody is already pursuing a
        // goal. Once any intent clears, the next cadence slot can find it.
        cursor = start;
        return null;
    }
}
