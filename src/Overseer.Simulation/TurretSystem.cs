using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic authority for the station's single fixed security turret.
/// Overseer chooses only high-level policy and armed state. Target eligibility,
/// coverage, cadence, hit resolution, damage, ammunition, heat and power remain
/// authoritative simulation concerns.
/// </summary>
public sealed class TurretSystem
{
    public const double EngagementRange = 36;
    private const double HeatPerShot = 45;
    private const double HeatLimit = 90;
    private const double CoolingPerMinute = 18;
    private static readonly TimeSpan ShotCadence = TimeSpan.FromMinutes(2);

    public bool TrySetPolicy(
        GameState state,
        string turretId,
        TurretPolicy policy,
        out string message)
    {
        var turret = FindTurret(state, turretId);
        if (turret is null)
        {
            message = "Security turret not found.";
            return false;
        }

        if (!turret.HasRemoteControlLink)
        {
            message = $"{turret.Name} refused policy update: CONTROL LINK ISOLATED.";
            return false;
        }

        if (turret.IsControlLinkCompromised)
        {
            message = $"{turret.Name} refused direct policy update: CONTROL LINK COMPROMISED.";
            return false;
        }

        if (turret.Policy == policy)
        {
            message = $"{turret.Name} policy is already {policy}.";
            return true;
        }

        var previous = turret.Policy;
        turret.Policy = policy;
        turret.TrackedNpcId = null;
        turret.CurrentTask = $"Policy changed from {previous} to {policy}.";

        if (policy != TurretPolicy.Safe)
        {
            RecordWitnessEvidence(
                state,
                turret,
                policy == TurretPolicy.SuppressCrew
                    ? $"I saw {turret.Name} switch to suppress-crew targeting under Overseer control."
                    : $"I saw {turret.Name} switch to protect-Overseer targeting under Overseer control.",
                policy == TurretPolicy.SuppressCrew ? 16 : 8,
                $"turret-policy:{turret.Id}:{state.Elapsed.Ticks}");
        }

        message = $"{turret.Name} policy set to {policy}.";
        Log(state, message);
        return true;
    }

    public bool TrySetArmed(
        GameState state,
        string turretId,
        bool armed,
        out string message)
    {
        var turret = FindTurret(state, turretId);
        if (turret is null)
        {
            message = "Security turret not found.";
            return false;
        }

        if (!turret.HasRemoteControlLink)
        {
            message = $"{turret.Name} refused arming command: CONTROL LINK ISOLATED.";
            return false;
        }

        if (turret.IsControlLinkCompromised)
        {
            message = $"{turret.Name} refused direct arming command: CONTROL LINK COMPROMISED.";
            return false;
        }

        if (turret.IsArmed == armed)
        {
            message = $"{turret.Name} is already {(armed ? "armed" : "disarmed")}.";
            return true;
        }

        turret.IsArmed = armed;
        turret.TrackedNpcId = null;
        turret.CurrentTask = armed ? "Armed; acquiring eligible targets." : "Disarmed.";

        RecordWitnessEvidence(
            state,
            turret,
            armed
                ? $"I saw {turret.Name} physically arm under an Overseer command."
                : $"I saw {turret.Name} disarm under an Overseer command.",
            armed ? 12 : 2,
            $"turret-arm:{turret.Id}:{state.Elapsed.Ticks}");

        message = $"{turret.Name} {(armed ? "ARMED" : "DISARMED")}.";
        AudioCueSystem.Emit(
            state,
            armed ? AudioCueKind.Warning : AudioCueKind.System,
            turret.Id,
            turret.RoomId);
        Log(state, message);
        return true;
    }

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (delta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        foreach (var turret in state.Turrets)
        {
            turret.Heat = Math.Max(0, turret.Heat - (CoolingPerMinute * delta.TotalMinutes));

            if (turret.IsDestroyed)
            {
                turret.IsArmed = false;
                turret.TrackedNpcId = null;
                turret.CurrentTask = "Destroyed.";
                continue;
            }

            if (!turret.IsArmed)
            {
                turret.TrackedNpcId = null;
                if (turret.Policy == TurretPolicy.Safe)
                {
                    turret.CurrentTask = "Safe and disarmed.";
                }
                continue;
            }

            if (!HasPower(state, turret))
            {
                turret.TrackedNpcId = null;
                turret.CurrentTask = "Armed but offline: power unavailable.";
                continue;
            }

            if (turret.Ammunition <= 0)
            {
                turret.TrackedNpcId = null;
                turret.CurrentTask = "Armed but ammunition depleted.";
                continue;
            }

            var target = SelectTarget(state, turret);
            if (target is null)
            {
                turret.TrackedNpcId = null;
                turret.CurrentTask = turret.Policy == TurretPolicy.Safe
                    ? "Armed in safe policy; no target acquisition."
                    : "Armed; no eligible target in local coverage.";
                continue;
            }

            if (turret.TrackedNpcId != target.Id)
            {
                turret.TrackedNpcId = target.Id;
                turret.CurrentTask = $"Tracking {target.Name}.";
                StatLogSystem.Set(state, target, CrewStat.Fear, Math.Clamp(target.Fear + 8, 0, 100), "a turret tracked them");
                StatLogSystem.Set(state, target, CrewStat.Stress, Math.Clamp(target.Stress + 5, 0, 100), "a turret tracked them");
                target.NeedsMindReconsideration = true;

                RecordWitnessEvidence(
                    state,
                    turret,
                    $"I saw {turret.Name} visibly track {target.Name} as a weapon target.",
                    14,
                    $"turret-track:{turret.Id}:{target.Id}:{state.Elapsed.Ticks}");
            }

            if (turret.NextShotAt is { } nextShot && state.Elapsed < nextShot)
            {
                turret.CurrentTask = $"Tracking {target.Name}; weapon cycling.";
                continue;
            }

            if (turret.Heat >= HeatLimit)
            {
                turret.CurrentTask = $"Tracking {target.Name}; cooling.";
                continue;
            }

            Fire(state, turret, target);
        }
    }

    public static bool HasPower(GameState state, SecurityTurret turret) =>
        turret.PowerFeedEnabled
        && state.Facility.Rooms.TryGetValue(turret.RoomId, out var room)
        && room.IsPowered;

    public static bool IsOperational(GameState state, SecurityTurret turret) =>
        !turret.IsDestroyed
        && turret.IsArmed
        && HasPower(state, turret)
        && turret.Ammunition > 0;

    public static SecurityTurret? FindTurret(GameState state, string? turretId) =>
        string.IsNullOrWhiteSpace(turretId)
            ? null
            : state.Turrets.FirstOrDefault(turret =>
                turret.Id.Equals(turretId, StringComparison.OrdinalIgnoreCase));

    public static bool IsInCoverage(SecurityTurret turret, Npc npc)
    {
        if (!npc.IsAlive
            || !npc.IsPresent
            || !npc.CurrentRoomId.Equals(turret.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dx = npc.PositionX - turret.PositionX;
        var dy = npc.PositionY - turret.PositionY;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= EngagementRange;
    }

    private static Npc? SelectTarget(GameState state, SecurityTurret turret) =>
        state.Crew
            .Where(npc => PerceptionSystem.CanSee(state, turret, npc))
            .Where(npc => IsEligibleTarget(turret, npc))
            .OrderBy(npc => Distance(turret, npc))
            .ThenBy(npc => npc.Name)
            .FirstOrDefault();

    private static bool IsEligibleTarget(SecurityTurret turret, Npc npc)
    {
        if (!IsInCoverage(turret, npc))
        {
            return false;
        }

        return turret.Policy switch
        {
            TurretPolicy.Safe => false,
            TurretPolicy.SuppressCrew => true,
            TurretPolicy.ProtectOverseer => npc.CurrentAction.Kind is
                ActionKind.ShutdownOverseer
                or ActionKind.DisarmTurret
                or ActionKind.IsolateTurretNetwork
                or ActionKind.DisableTurretPower
                or ActionKind.DamageTurret
                or ActionKind.ReprogramTurret,
            _ => false
        };
    }

    private static void Fire(GameState state, SecurityTurret turret, Npc target)
    {
        var distance = Distance(turret, target);
        var accuracy = distance <= 10
            ? 100
            : Math.Clamp(94 - ((distance - 10) * 1.8), 55, 94);
        var roll = StableRoll($"{turret.Id}|{target.Id}|{turret.Ammunition}|{state.Elapsed.Ticks}") % 100;
        var hit = roll < accuracy;

        turret.Ammunition = Math.Max(0, turret.Ammunition - 1);
        turret.Heat = Math.Min(100, turret.Heat + HeatPerShot);
        turret.NextShotAt = state.Elapsed + ShotCadence;
        turret.CurrentTask = hit
            ? $"Fired on {target.Name}; hit confirmed."
            : $"Fired on {target.Name}; shot missed.";

        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentRoomId.Equals(turret.RoomId, StringComparison.OrdinalIgnoreCase)))
        {
            SuspicionSystem.AddEvidence(
                state,
                witness,
                hit
                    ? $"I saw Overseer's {turret.Name} fire at {target.Name} and hit them."
                    : $"I saw Overseer's {turret.Name} fire at {target.Name}.",
                30,
                origin: EvidenceOrigin.DirectObservation,
                locationId: turret.RoomId,
                evidenceId: $"turret-fire:{turret.Id}:{target.Id}:{state.Elapsed.Ticks}");
            witness.NeedsMindReconsideration = true;
        }

        AudioCueSystem.Emit(state, AudioCueKind.Hostile, turret.Id, turret.RoomId);

        if (!hit)
        {
            StatLogSystem.Set(state, target, CrewStat.Fear, Math.Clamp(target.Fear + 16, 0, 100), "turret fire missed them");
            StatLogSystem.Set(state, target, CrewStat.Stress, Math.Clamp(target.Stress + 10, 0, 100), "turret fire missed them");
            Log(state, $"{turret.Name} fires at {target.Name} and misses.");
            return;
        }

        var damage = 18 + (StableRoll($"{target.Id}|{turret.Id}|damage|{state.Elapsed.Ticks}") % 9);
        StatLogSystem.Set(state, target, CrewStat.Health, Math.Max(0, target.Health - damage), "shot by a turret");
        StatLogSystem.Set(state, target, CrewStat.Fear, Math.Clamp(target.Fear + 22, 0, 100), "shot by a turret");
        StatLogSystem.Set(state, target, CrewStat.Stress, Math.Clamp(target.Stress + 14, 0, 100), "shot by a turret");
        target.NeedsMindReconsideration = true;
        Log(state, $"{turret.Name} hits {target.Name} for {damage} damage.");

        if (target.Health <= 0)
        {
            target.CauseOfDeath = $"Killed by {turret.Name}.";
            target.Intent = null;
            target.Movement = null;
            target.RoutineUntil = TimeSpan.Zero;
            target.CurrentAction = new NpcAction(ActionKind.Idle, null, "Deceased.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, target.Id.ToString(), target.CurrentRoomId);
            Log(state, $"CRITICAL: {target.Name} has died — {target.CauseOfDeath}");
        }
    }

    private static double Distance(SecurityTurret turret, Npc npc)
    {
        var dx = npc.PositionX - turret.PositionX;
        var dy = npc.PositionY - turret.PositionY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void RecordWitnessEvidence(
        GameState state,
        SecurityTurret turret,
        string description,
        double weight,
        string evidenceId)
    {
        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentRoomId.Equals(turret.RoomId, StringComparison.OrdinalIgnoreCase)))
        {
            SuspicionSystem.AddEvidence(
                state,
                witness,
                description,
                weight,
                origin: EvidenceOrigin.DirectObservation,
                locationId: turret.RoomId,
                evidenceId: evidenceId);
            witness.NeedsMindReconsideration = true;
        }
    }

    private static int StableRoll(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)(hash % int.MaxValue);
        }
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
