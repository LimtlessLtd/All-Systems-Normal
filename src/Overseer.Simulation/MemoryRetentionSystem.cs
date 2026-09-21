using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// How present a memory is in someone's mind right now. Importance sets how
/// strong it starts and how slowly it fades: trivia is gone within hours, a
/// killing or a death stays vivid for about a day.
/// </summary>
public static class MemorySalience
{
    private const double BaseHalfLifeHours = 4;
    private const double ImportantHalfLifeHours = 20;

    public static double Score(Memory memory, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var ageHours = Math.Max(0, (now - memory.OccurredAt).TotalHours);
        var importance = Math.Clamp(memory.Importance, 0, 1);
        var halfLife = BaseHalfLifeHours + (ImportantHalfLifeHours * importance * importance);

        return importance * Math.Pow(0.5, ageHours / halfLife);
    }

    /// <summary>The memories most on this person's mind, most salient first.</summary>
    public static IReadOnlyList<Memory> MostSalient(Npc npc, TimeSpan now, int count)
    {
        ArgumentNullException.ThrowIfNull(npc);

        return npc.Memories
            .OrderByDescending(memory => Score(memory, now))
            .ThenByDescending(memory => memory.OccurredAt)
            .Take(count)
            .ToList();
    }
}

/// <summary>
/// Keeps memory bounded. Memories used to accumulate forever, so a long run
/// grew every crew member's list without limit while the prompt kept showing
/// the same early high-importance entries.
/// </summary>
public sealed class MemoryRetentionSystem
{
    public const int MaxMemoriesPerCrew = 40;
    private const int IntervalMinutes = 30;
    private const double ForgottenBelow = 0.01;
    private static readonly TimeSpan MinimumAgeToForget = TimeSpan.FromHours(24);

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);
        if (minute <= 0 || minute % IntervalMinutes != 0)
            return;

        foreach (var npc in state.Crew)
        {
            Trim(npc, state.Elapsed);
        }
    }

    public static void Trim(Npc npc, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(npc);

        npc.Memories.RemoveAll(memory =>
            now - memory.OccurredAt >= MinimumAgeToForget
            && MemorySalience.Score(memory, now) < ForgottenBelow);

        if (npc.Memories.Count <= MaxMemoriesPerCrew)
            return;

        var keep = npc.Memories
            .OrderByDescending(memory => MemorySalience.Score(memory, now))
            .ThenByDescending(memory => memory.OccurredAt)
            .Take(MaxMemoriesPerCrew)
            .ToHashSet();

        // Preserve chronological order for anything that reads the list.
        npc.Memories.RemoveAll(memory => !keep.Contains(memory));
    }
}
