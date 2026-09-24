using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #29: alarm fatigue. Someone who has walked into a room and found
/// Overseer's alarm about it false, several times running, has a track record
/// they can weigh. This only keeps that record and reports it to cognition.
/// Whether the next alarm is taken seriously stays the mind's decision; nothing
/// here dampens fear or changes behaviour.
/// </summary>
public static class AlarmFatigueRules
{
    /// <summary>Checks older than this no longer count towards a streak.</summary>
    public static readonly TimeSpan Memory = TimeSpan.FromHours(24);

    /// <summary>How many consecutive false checks of one source before it is worth mentioning.</summary>
    public const int ReportAtStreak = 2;

    /// <summary>Per-person bound on remembered alarm checks.</summary>
    public const int Capacity = 24;

    public static bool IsAlarm(OverseerClaimKind kind) =>
        kind is OverseerClaimKind.Warning or OverseerClaimKind.FireAlarm;

    /// <summary>Records a claim the person has just settled by seeing the room, if it was an alarm.</summary>
    public static void Record(GameState state, Npc npc, OverseerClaimRecord claim)
    {
        if (!IsAlarm(claim.Kind) || claim.SubjectRoomId is not { } roomId)
            return;

        npc.AlarmOutcomes.Insert(0, new AlarmOutcome(claim.Kind, roomId, state.Elapsed, claim.WasFalseWhenMade));

        if (npc.AlarmOutcomes.Count > Capacity)
            npc.AlarmOutcomes.RemoveRange(Capacity, npc.AlarmOutcomes.Count - Capacity);
    }

    /// <summary>
    /// Alarm sources (kind and room) whose most recent checks by this person
    /// within <see cref="Memory"/> were all false, at least
    /// <see cref="ReportAtStreak"/> in a row. A true alarm ends the streak.
    /// </summary>
    public static IReadOnlyList<(OverseerClaimKind Kind, string RoomId, int FalseInARow, TimeSpan LatestAt)> FalseStreaks(
        GameState state,
        Npc npc)
    {
        return npc.AlarmOutcomes
            .Where(outcome => state.Elapsed - outcome.SettledAt <= Memory)
            .GroupBy(outcome => (outcome.Kind, RoomId: outcome.RoomId.ToLowerInvariant()))
            .Select(source =>
            {
                // AlarmOutcomes is newest first, so the streak is the leading run of false checks.
                var recent = source.ToList();
                return (
                    source.Key.Kind,
                    recent[0].RoomId,
                    FalseInARow: recent.TakeWhile(outcome => outcome.WasFalse).Count(),
                    LatestAt: recent[0].SettledAt);
            })
            .Where(streak => streak.FalseInARow >= ReportAtStreak)
            .OrderByDescending(streak => streak.LatestAt)
            .ThenBy(streak => streak.RoomId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
