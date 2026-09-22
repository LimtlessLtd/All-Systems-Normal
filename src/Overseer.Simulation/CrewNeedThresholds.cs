namespace Overseer.Simulation;

/// <summary>
/// Shared need-escalation thresholds for the two fallback decision ladders
/// (<see cref="BrowserMindSystem"/> and <c>Overseer.AI.RuleBasedAiDecisionService</c>).
/// Both used to hard-code their own copies of these numbers and drifted out
/// of sync (see docs/handoff/BACKLOG.md -> P1); a threshold change now only
/// needs to happen here.
/// </summary>
public static class CrewNeedThresholds
{
    /// <summary>Hunger level at which eating becomes an emergency that can pre-empt technical/social work.</summary>
    public const double HungerCritical = 72;

    /// <summary>Hunger level at which eating becomes the routine priority.</summary>
    public const double HungerElevated = 58;

    /// <summary>Fatigue level at which sleep becomes an emergency that can pre-empt technical/social work.</summary>
    public const double FatigueCritical = 86;

    /// <summary>Fatigue level at which sleep becomes the routine priority.</summary>
    public const double FatigueElevated = 68;

    public const double BladderNeed = 72;
    public const double HygieneNeed = 60;
    public const double RecreationNeed = 58;
    public const double ResentmentArgue = 48;
    public const double SocialNeed = 68;
    public const double SociabilityForSocialize = 50;
}
