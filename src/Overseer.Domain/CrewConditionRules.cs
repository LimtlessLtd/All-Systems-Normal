namespace Overseer.Domain;

/// <summary>
/// Deterministic consequences of fatigue and missed scheduled rest. Cognition may
/// want to keep working, but physical speed and effective skill are world rules.
/// </summary>
public static class CrewConditionRules
{
    public static double MovementMultiplier(Npc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        var fatiguePenalty = Math.Max(0, npc.Fatigue - 45) * 0.0075;
        var debtPenalty = Math.Min(0.20, npc.SleepDebtMinutes / 1200d);
        return Math.Clamp(1 - fatiguePenalty - debtPenalty, 0.48, 1);
    }

    public static int CognitivePenalty(Npc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        var fatigue = Math.Max(0, npc.Fatigue - 55) * 0.42;
        var debt = Math.Min(22, npc.SleepDebtMinutes / 45d);
        return (int)Math.Round(Math.Clamp(fatigue + debt, 0, 38));
    }

    public static int EffectiveSkill(Npc npc, int rawSkill) =>
        Math.Clamp(rawSkill - CognitivePenalty(npc), 0, 120);

    public static string ImpairmentLabel(Npc npc)
    {
        var penalty = CognitivePenalty(npc);
        return penalty switch
        {
            >= 28 => "severely sleep-deprived",
            >= 16 => "sleep-deprived",
            >= 7 => "tired",
            _ => "alert"
        };
    }
}
