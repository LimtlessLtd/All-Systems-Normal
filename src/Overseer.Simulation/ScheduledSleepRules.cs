using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Shared "is this person off shift and should stay in bed?" rule for the
/// deterministic routine, the duty-assignment systems and both fallback
/// minds. Before it existed, every one of those independently pulled
/// sleepers out of bed for mild hunger, routine chores or a round-robin
/// thought, so crew spent their whole sleep window commuting and were almost
/// never seen asleep. Genuinely serious needs and emergencies still win:
/// callers check those first, and this rule itself yields to critical hunger
/// and an escalated missing-person concern (a full bladder means a trip to
/// the toilet, then back to bed).
/// </summary>
public static class ScheduledSleepRules
{
    /// <summary>
    /// Off-shift crew are only called out of bed for maintenance at or above
    /// this service urgency (the same level at which provisioning work yields
    /// to critical maintenance).
    /// </summary>
    public const double OffShiftCallOutUrgency = 50;

    public static bool IsOffShift(Npc npc, TimeSpan elapsed) =>
        !npc.IsPrisoner && CrewDutySchedule.IsSleepWindow(npc, elapsed);

    /// <summary>
    /// True when an off-shift person should stay in (or return to) bed rather
    /// than take up an ordinary want. A full bladder is the one mundane need
    /// that still gets them up: callers send them to the toilet instead of
    /// sleep when <see cref="NeedsToiletBreak"/> is also true.
    /// </summary>
    public static bool ShouldKeepScheduledSleep(Npc npc, TimeSpan elapsed) =>
        IsOffShift(npc, elapsed)
        && npc.Hunger < CrewNeedThresholds.HungerCritical
        && !npc.MissingPersonConcerns.Values.Any(concern =>
            concern.Stage == MissingPersonConcernStage.Escalated);

    public static bool NeedsToiletBreak(Npc npc) =>
        npc.BladderNeed >= CrewNeedThresholds.BladderNeed;
}
