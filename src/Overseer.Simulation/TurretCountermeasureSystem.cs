using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic physical crew counterplay against the fixed security turret.
/// Intent can come from a model or fallback mind; location, skill, elapsed work
/// and the resulting world-state mutation are resolved only here.
/// </summary>
public sealed class TurretCountermeasureSystem
{
    public const string ControlRoomId = "engineering";

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            var action = npc.ActiveTask is { Status: CrewTaskStatus.InProgress } task
                ? task.Action
                : npc.CurrentAction.Kind;

            switch (action)
            {
                case ActionKind.DisarmTurret:
                    TickLocalDisarm(state, npc);
                    break;
                case ActionKind.IsolateTurretNetwork:
                    TickNetworkIsolation(state, npc);
                    break;
                case ActionKind.DisableTurretPower:
                    TickPowerDenial(state, npc);
                    break;
                case ActionKind.DamageTurret:
                    TickDamage(state, npc);
                    break;
                case ActionKind.ReprogramTurret:
                    TickReprogram(state, npc);
                    break;
            }
        }
    }

    public static SecurityTurret? FindTurret(GameState state, string? turretId) =>
        TurretSystem.FindTurret(state, turretId);

    public static bool IsCoLocated(Npc npc, SecurityTurret turret) =>
        npc.CurrentRoomId.Equals(turret.RoomId, StringComparison.OrdinalIgnoreCase);

    public static bool HasHostileTurretEvidence(Npc npc, SecurityTurret turret) =>
        npc.OverseerEvidence.Any(evidence =>
            evidence.CurrentWeight > 1
            && evidence.Description.Contains(turret.Name, StringComparison.OrdinalIgnoreCase)
            && (evidence.Description.Contains("fire", StringComparison.OrdinalIgnoreCase)
                || evidence.Description.Contains("track", StringComparison.OrdinalIgnoreCase)
                || evidence.Description.Contains("suppress-crew", StringComparison.OrdinalIgnoreCase)
                || evidence.Description.Contains("physically arm", StringComparison.OrdinalIgnoreCase)));

    private static void TickLocalDisarm(GameState state, Npc npc)
    {
        var turret = FindTurret(state, npc.CurrentAction.TargetId);
        if (turret is null || turret.IsDestroyed || !IsCoLocated(npc, turret))
        {
            End(state, npc, "I need to be physically beside the turret to reach its local safing controls.");
            return;
        }

        if (!turret.IsArmed)
        {
            End(state, npc, $"{turret.Name} is already disarmed.");
            return;
        }

        var score = Math.Max(
            CrewCounterplaySystem.BestTechnicalSkill(npc),
            npc.Skills.TryGetValue("Security", out var security) ? security : 0);
        if (score < 45)
        {
            End(state, npc, "I cannot safely operate the turret's local disarm.");
            return;
        }

        if (!BeginOrComplete(state, npc, 2, $"disarming {turret.Name} at its local service panel"))
        {
            return;
        }

        turret.IsArmed = false;
        turret.TrackedNpcId = null;
        turret.CurrentTask = $"Locally disarmed by {npc.Name}.";
        Complete(state, npc, $"{turret.Name} is locally disarmed.");
        AudioCueSystem.Emit(state, AudioCueKind.Important, npc.Id.ToString(), npc.CurrentRoomId);
        Log(state, $"{npc.Name} physically disarms {turret.Name}.");
    }

    private static void TickNetworkIsolation(GameState state, Npc npc)
    {
        var turret = FindTurret(state, npc.CurrentAction.TargetId);
        if (turret is null || turret.IsDestroyed)
        {
            End(state, npc, "That turret is not available.");
            return;
        }

        if (!npc.CurrentRoomId.Equals(ControlRoomId, StringComparison.OrdinalIgnoreCase)
            || CrewCounterplaySystem.BestTechnicalSkill(npc) < 55)
        {
            End(state, npc, "I need Engineering access and enough technical skill to isolate the turret control link.");
            return;
        }

        if (!HasHostileTurretEvidence(npc, turret))
        {
            End(state, npc, $"I do not have personally grounded evidence to justify isolating {turret.Name}.");
            return;
        }

        if (turret.IsNetworkIsolated)
        {
            End(state, npc, $"{turret.Name}'s remote link is already isolated.");
            return;
        }

        if (!BeginOrComplete(state, npc, 2, $"isolating {turret.Name}'s security network"))
        {
            return;
        }

        turret.IsNetworkIsolated = true;
        turret.CurrentTask = turret.IsArmed
            ? "Remote control isolated; local armed policy remains active."
            : "Remote control isolated.";
        Complete(state, npc, $"{turret.Name}'s remote control link is isolated.");
        Log(state, $"{npc.Name} isolates {turret.Name} from Overseer's security network.");
    }

    private static void TickPowerDenial(GameState state, Npc npc)
    {
        var turret = FindTurret(state, npc.CurrentAction.TargetId);
        if (turret is null || turret.IsDestroyed)
        {
            End(state, npc, "That turret is not available.");
            return;
        }

        if (!npc.CurrentRoomId.Equals(ControlRoomId, StringComparison.OrdinalIgnoreCase)
            || CrewCounterplaySystem.BestTechnicalSkill(npc) < 45)
        {
            End(state, npc, "I need the Engineering security-power controls to deny turret power.");
            return;
        }

        if (!HasHostileTurretEvidence(npc, turret))
        {
            End(state, npc, $"I do not have personally grounded evidence to justify denying {turret.Name}'s power.");
            return;
        }

        if (!turret.PowerFeedEnabled)
        {
            End(state, npc, $"{turret.Name}'s dedicated power feed is already disabled.");
            return;
        }

        if (!BeginOrComplete(state, npc, 1, $"cutting {turret.Name}'s dedicated power feed"))
        {
            return;
        }

        turret.PowerFeedEnabled = false;
        turret.TrackedNpcId = null;
        turret.CurrentTask = turret.IsArmed
            ? "Armed but offline: dedicated power feed denied."
            : "Power feed denied.";
        Complete(state, npc, $"{turret.Name}'s dedicated power feed is disabled.");
        Log(state, $"{npc.Name} disables {turret.Name}'s dedicated security power feed.");
    }

    private static void TickDamage(GameState state, Npc npc)
    {
        var turret = FindTurret(state, npc.CurrentAction.TargetId);
        if (turret is null || turret.IsDestroyed || !IsCoLocated(npc, turret))
        {
            End(state, npc, "I need to be physically beside the turret to sabotage it.");
            return;
        }

        if (CrewCounterplaySystem.BestForceSkill(npc) < 40)
        {
            End(state, npc, "I cannot find a credible way to physically disable the turret.");
            return;
        }

        if (!BeginOrComplete(state, npc, 1, $"physically sabotaging {turret.Name}"))
        {
            return;
        }

        var damage = 26 + (StableRoll(npc.Name, turret.Id, (int)state.Elapsed.TotalMinutes) % 19);
        turret.Integrity = Math.Max(0, turret.Integrity - damage);
        if (turret.IsDestroyed)
        {
            turret.IsArmed = false;
            turret.TrackedNpcId = null;
            turret.CurrentTask = "Destroyed by crew sabotage.";
        }

        Complete(state, npc, turret.IsDestroyed
            ? $"{turret.Name} is disabled beyond operation."
            : $"Damaged {turret.Name}; integrity is {turret.Integrity:0}%.");
        AudioCueSystem.Emit(state, AudioCueKind.Hostile, npc.Id.ToString(), npc.CurrentRoomId);
        Log(state, $"{npc.Name} damages {turret.Name} for {damage}; integrity {turret.Integrity:0}%.");
    }

    private static void TickReprogram(GameState state, Npc npc)
    {
        var turret = FindTurret(state, npc.CurrentAction.TargetId);
        if (turret is null || turret.IsDestroyed || !IsCoLocated(npc, turret))
        {
            End(state, npc, "I need the intact turret physically in front of me to reprogram it.");
            return;
        }

        if (turret.IsArmed)
        {
            End(state, npc, $"{turret.Name} must be disarmed before local reprogramming.");
            return;
        }

        if (CrewCounterplaySystem.BestTechnicalSkill(npc) < 65)
        {
            End(state, npc, "I do not have enough technical skill to rewrite the local turret policy.");
            return;
        }

        if (!BeginOrComplete(state, npc, 3, $"reprogramming {turret.Name}'s local targeting controller"))
        {
            return;
        }

        turret.Policy = TurretPolicy.Safe;
        turret.IsArmed = false;
        turret.TrackedNpcId = null;
        turret.NextShotAt = null;
        turret.CurrentTask = "Locally reprogrammed to safe policy.";
        Complete(state, npc, $"{turret.Name} is locally reprogrammed to safe policy.");
        AudioCueSystem.Emit(state, AudioCueKind.Important, npc.Id.ToString(), npc.CurrentRoomId);
        Log(state, $"{npc.Name} locally reprograms {turret.Name} to Safe policy.");
    }

    private static bool BeginOrComplete(
        GameState state,
        Npc npc,
        int minutes,
        string activity)
    {
        if (!CrewTaskSystem.IsWorking(npc, npc.CurrentAction.Kind))
        {
            npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(minutes);
            CrewTaskSystem.Start(
                state,
                npc,
                npc.CurrentAction.Kind,
                npc.CurrentAction.TargetId,
                activity,
                TimeSpan.FromMinutes(minutes));
            npc.Bubble = new NpcBubble(
                $"I'm {activity}.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(Math.Max(2, minutes)));
            Log(state, $"{npc.Name} begins {activity}.");
            return false;
        }

        return CrewTaskSystem.IsComplete(state, npc);
    }

    private static void Complete(GameState state, Npc npc, string reason)
    {
        if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress })
            CrewTaskSystem.Succeed(state, npc, reason);

        Finish(npc, reason);
    }

    private static void End(GameState state, Npc npc, string reason)
    {
        // Reaching the nominal deadline is not success by itself. If a worker
        // loses location, skill, evidence, power or a valid target before the
        // deterministic mutation occurs, record a failed task even when the
        // clock has reached 100%.
        if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress })
            CrewTaskSystem.Fail(state, npc, reason);

        Finish(npc, reason);
    }

    private static void Finish(Npc npc, string reason)
    {
        npc.RoutineUntil = TimeSpan.Zero;
        npc.Intent = null;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
    }

    private static int StableRoll(string first, string second, int minute)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in $"{first}|{second}|{minute}|turret")
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)(hash % 1000);
        }
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
