using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #91: a bounded per-crew log of what changed their core stats.
/// Consequence sites call <see cref="Set"/> with the value they already
/// computed, so recording never changes the simulation's arithmetic. A cause
/// that keeps acting tick after tick extends one entry instead of adding a
/// line per tick. Presentation/diagnostic only.
/// </summary>
public static class StatLogSystem
{
    public const int Capacity = 40;

    /// <summary>
    /// Changes from one cause this close together read as one continuous
    /// effect. Wider than any simulation tick, narrow enough that eating
    /// between two stretches of metabolism splits them.
    /// </summary>
    public static readonly TimeSpan CoalesceGap = TimeSpan.FromMinutes(3);

    /// <summary>Anything smaller is float noise, not an event.</summary>
    private const double Epsilon = 1e-9;

    /// <summary>Accumulated changes smaller than this are not worth a line.</summary>
    public const double DisplayThreshold = 0.1;

    /// <summary>The entries the Inspector shows, newest first.</summary>
    public static IReadOnlyList<StatLogEntry> DisplayRows(Npc npc, int max = 20) =>
        npc.StatLog
            .Where(entry => Math.Abs(entry.Delta) >= DisplayThreshold)
            .Take(max)
            .ToList();

    /// <summary>
    /// True when the change was good for the person: health up, or any of the
    /// pressure stats (stress, fear, hunger, fatigue) down.
    /// </summary>
    public static bool IsImprovement(StatLogEntry entry) =>
        entry.Stat == CrewStat.Health ? entry.Delta > 0 : entry.Delta < 0;

    public static string Describe(StatLogEntry entry)
    {
        var delta = entry.Delta.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture);
        var span = entry.LastAt - entry.StartedAt >= TimeSpan.FromMinutes(1)
            ? $"T+{Clock(entry.StartedAt)}–{Clock(entry.LastAt)}"
            : $"T+{Clock(entry.StartedAt)}";
        return $"{entry.Stat.ToString().ToUpperInvariant()} {delta} · {entry.Cause} · {span}";
    }

    private static string Clock(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}";

    public static double Get(Npc npc, CrewStat stat) => stat switch
    {
        CrewStat.Health => npc.Health,
        CrewStat.Stress => npc.Stress,
        CrewStat.Fear => npc.Fear,
        CrewStat.Hunger => npc.Hunger,
        CrewStat.Fatigue => npc.Fatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(stat))
    };

    /// <summary>Assigns <paramref name="value"/> and logs the change it made.</summary>
    public static void Set(GameState state, Npc npc, CrewStat stat, double value, string cause)
    {
        var before = Get(npc, stat);
        switch (stat)
        {
            case CrewStat.Health: npc.Health = value; break;
            case CrewStat.Stress: npc.Stress = value; break;
            case CrewStat.Fear: npc.Fear = value; break;
            case CrewStat.Hunger: npc.Hunger = value; break;
            case CrewStat.Fatigue: npc.Fatigue = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(stat));
        }

        Record(state, npc, stat, value - before, cause);
    }

    /// <summary>
    /// Logs a change of <paramref name="actualDelta"/> that several causes made
    /// together. When no clamp intervened, each part is its cause's real share
    /// and is logged separately. When a clamp did, the shares no longer add up,
    /// so the real change goes to the largest contributor.
    /// </summary>
    public static void RecordParts(
        GameState state,
        Npc npc,
        CrewStat stat,
        double actualDelta,
        IReadOnlyList<(string Cause, double Delta)> parts)
    {
        var sum = parts.Sum(part => part.Delta);
        if (Math.Abs(sum - actualDelta) < 1e-6)
        {
            foreach (var (cause, delta) in parts)
            {
                Record(state, npc, stat, delta, cause);
            }

            return;
        }

        if (parts.Count == 0)
        {
            return;
        }

        var dominant = parts.MaxBy(part => Math.Abs(part.Delta));
        Record(state, npc, stat, actualDelta, dominant.Cause);
    }

    public static void Record(GameState state, Npc npc, CrewStat stat, double delta, string cause)
    {
        if (Math.Abs(delta) < Epsilon)
        {
            return;
        }

        var now = state.Elapsed;
        var ongoing = npc.StatLog.FirstOrDefault(entry =>
            entry.Stat == stat
            && string.Equals(entry.Cause, cause, StringComparison.Ordinal)
            && now - entry.LastAt <= CoalesceGap);

        if (ongoing is not null)
        {
            ongoing.Delta += delta;
            ongoing.LastAt = now;
            return;
        }

        npc.StatLog.Insert(0, new StatLogEntry
        {
            Stat = stat,
            Cause = cause,
            Delta = delta,
            StartedAt = now,
            LastAt = now
        });

        if (npc.StatLog.Count > Capacity)
        {
            npc.StatLog.RemoveRange(Capacity, npc.StatLog.Count - Capacity);
        }
    }
}
