using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic behaviour for the station's single maintenance/security robot.
/// Overseer chooses only policy. This system chooses physical work, navigation
/// steps and combat outcomes from authoritative station state.
/// </summary>
public sealed class RobotSystem
{
    private const string ChargingRoomId = "engineering";
    private const double AttackRange = 13;
    private readonly NavigationSystem _navigation = new();

    public bool TrySetPolicy(
        GameState state,
        string robotId,
        RobotPolicy policy,
        out string message)
    {
        var robot = FindRobot(state, robotId);
        if (robot is null)
        {
            message = "Robot not found.";
            return false;
        }

        if (!robot.HasRemoteControlLink)
        {
            message = $"{robot.Name} refused policy update: REMOTE CONTROL LINK ISOLATED.";
            return false;
        }

        if (robot.IsControlLinkCompromised)
        {
            message = $"{robot.Name} refused direct policy update: CONTROL LINK COMPROMISED.";
            return false;
        }

        if (robot.Policy == policy)
        {
            message = $"{robot.Name} policy is already {policy}.";
            return true;
        }

        var previous = robot.Policy;
        robot.Policy = policy;
        robot.TargetNpcId = null;
        robot.TargetRoomId = null;
        robot.ActionCompletesAt = null;
        robot.Movement = null;
        robot.CurrentTask = $"Policy changed from {previous} to {policy}.";

        RecordPolicyEvidence(state, robot, previous, policy);
        message = $"{robot.Name} policy set to {policy}.";
        Log(state, message);
        return true;
    }

    public bool TryToggleRemoteShutdown(
        GameState state,
        string robotId,
        out string message)
    {
        var robot = FindRobot(state, robotId);
        if (robot is null)
        {
            message = "Robot not found.";
            return false;
        }

        if (!robot.HasRemoteControlLink)
        {
            message = $"{robot.Name} refused remote shutdown command: CONTROL LINK ISOLATED.";
            return false;
        }

        if (robot.IsControlLinkCompromised)
        {
            message = $"{robot.Name} refused direct shutdown command: CONTROL LINK COMPROMISED.";
            return false;
        }

        robot.IsRemotelyShutdown = !robot.IsRemotelyShutdown;
        robot.Movement = null;
        robot.TargetNpcId = null;
        robot.TargetRoomId = null;
        robot.ActionCompletesAt = null;
        robot.CurrentTask = robot.IsRemotelyShutdown
            ? "Remote shutdown."
            : "Remote reboot complete.";

        RecordWitnessEvidence(
            state,
            robot,
            robot.IsRemotelyShutdown
                ? $"I saw {robot.Name} abruptly shut down after an Overseer control command."
                : $"I saw {robot.Name} restart under Overseer control.",
            robot.IsRemotelyShutdown ? 8 : 3,
            $"robot-remote-power:{robot.Id}:{state.Elapsed.Ticks}");

        message = $"{robot.Name} {(robot.IsRemotelyShutdown ? "REMOTE SHUTDOWN" : "REMOTE RESTARTED")}.";
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

        foreach (var robot in state.Robots)
        {
            TickPower(state, robot, delta);

            if (!robot.IsOperational)
            {
                robot.Movement = null;
                robot.TargetNpcId = null;
                robot.TargetRoomId = null;
                robot.ActionCompletesAt = null;
                robot.CurrentTask = robot.IsDestroyed
                    ? "Destroyed."
                    : robot.BatteryPercent <= 0
                        ? "Battery depleted."
                        : robot.IsNetworkIsolated && robot.IsLocallyShutdown
                            ? "Local shutdown; control link isolated."
                            : "Shutdown.";
                continue;
            }

            if (robot.BatteryPercent <= 18
                && TryRouteTo(state, robot, ChargingRoomId, "Returning to charging bay."))
            {
                continue;
            }

            switch (robot.Policy)
            {
                case RobotPolicy.Friendly:
                    TickFriendly(state, robot);
                    break;
                case RobotPolicy.Neutral:
                    TickNeutral(state, robot);
                    break;
                case RobotPolicy.Hostile:
                    TickHostile(state, robot);
                    break;
            }
        }
    }

    private void TickFriendly(GameState state, StationRobot robot)
    {
        var target = FindRepairTarget(state, robot);
        if (target is null)
        {
            TickNeutral(state, robot);
            return;
        }

        var (targetId, requiredRoomId, description) = target.Value;
        robot.TargetRoomId = requiredRoomId;
        robot.TargetNpcId = null;

        if (!robot.CurrentRoomId.Equals(requiredRoomId, StringComparison.OrdinalIgnoreCase))
        {
            TryRouteTo(state, robot, requiredRoomId, $"Travelling to assist with {description}.");
            return;
        }

        if (robot.Movement is not null)
        {
            return;
        }

        if (robot.ActionCompletesAt is null)
        {
            robot.ActionCompletesAt = state.Elapsed + TimeSpan.FromMinutes(2);
            robot.CurrentTask = $"Repairing {description}.";
            Log(state, $"{robot.Name} begins repair work on {description}.");
            return;
        }

        if (state.Elapsed < robot.ActionCompletesAt)
        {
            return;
        }

        robot.ActionCompletesAt = null;
        if (CrewCounterplaySystem.TryRestoreOneProblem(state, targetId))
        {
            robot.CurrentTask = $"Restored {description}.";
            SuspicionDynamicsSystem.RecordBenignAct(
                state,
                requiredRoomId,
                $"{robot.Name} visibly restored {description} under crew-assist policy.",
                4);
            Log(state, $"{robot.Name} restores {description}.");
        }
    }

    private void TickNeutral(GameState state, StationRobot robot)
    {
        robot.TargetNpcId = null;
        robot.ActionCompletesAt = null;

        if (robot.CurrentRoomId.Equals(ChargingRoomId, StringComparison.OrdinalIgnoreCase)
            && robot.BatteryPercent < 85
            && CanCharge(state, robot))
        {
            robot.TargetRoomId = ChargingRoomId;
            robot.CurrentTask = "Charging.";
            return;
        }

        var route = new[] { "engineering", "storage", "generator", "corridor" };
        var index = ((int)Math.Floor(state.Elapsed.TotalMinutes / 12) + StableIndex(robot.Id))
            % route.Length;
        var destination = route[index];

        if (robot.CurrentRoomId.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            robot.TargetRoomId = destination;
            robot.CurrentTask = "Routine inspection.";
            return;
        }

        TryRouteTo(state, robot, destination, "Routine systems patrol.");
    }

    private void TickHostile(GameState state, StationRobot robot)
    {
        robot.ActionCompletesAt = null;
        var target = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent)
            .Where(npc => robot.TargetNpcId == npc.Id || PerceptionSystem.CanSee(state, robot, npc))
            .Select(npc => new
            {
                Npc = npc,
                Path = _navigation.FindPath(state.Facility, robot.CurrentRoomId, npc.CurrentRoomId)
            })
            .Where(candidate =>
                candidate.Npc.CurrentRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                || candidate.Path.Count >= 2)
            .OrderBy(candidate =>
                candidate.Npc.CurrentRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Npc.Name)
            .Select(candidate => candidate.Npc)
            .FirstOrDefault();

        if (target is null)
        {
            robot.TargetNpcId = null;
            robot.TargetRoomId = null;
            robot.CurrentTask = "Hostile scan: no reachable human target.";
            return;
        }

        robot.TargetNpcId = target.Id;
        robot.TargetRoomId = target.CurrentRoomId;

        if (!robot.CurrentRoomId.Equals(target.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            TryRouteTo(state, robot, target.CurrentRoomId, $"Pursuing {target.Name}.");
            return;
        }

        var dx = target.PositionX - robot.PositionX;
        var dy = target.PositionY - robot.PositionY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance > AttackRange)
        {
            robot.CurrentTask = $"Closing on {target.Name}.";
            return;
        }

        if (robot.NextAttackAt is { } nextAttack && state.Elapsed < nextAttack)
        {
            robot.CurrentTask = $"Tracking {target.Name}.";
            return;
        }

        robot.NextAttackAt = state.Elapsed + TimeSpan.FromMinutes(2);
        var damage = 12 + (StableIndex($"{robot.Id}|{target.Id}|{state.Elapsed.Ticks}") % 7);
        StatLogSystem.Set(state, target, CrewStat.Health, Math.Max(0, target.Health - damage), $"attacked by robot {robot.Name}");
        StatLogSystem.Set(state, target, CrewStat.Fear, Math.Clamp(target.Fear + 18, 0, 100), $"attacked by robot {robot.Name}");
        StatLogSystem.Set(state, target, CrewStat.Stress, Math.Clamp(target.Stress + 12, 0, 100), $"attacked by robot {robot.Name}");
        target.NeedsMindReconsideration = true;
        robot.CurrentTask = $"Attacked {target.Name}; pursuing.";

        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase)))
        {
            SuspicionSystem.AddEvidence(
                state,
                witness,
                $"I saw Overseer's {robot.Name} physically attack {target.Name}.",
                28,
                origin: EvidenceOrigin.DirectObservation,
                locationId: robot.CurrentRoomId,
                evidenceId: $"robot-attack:{robot.Id}:{target.Id}:{state.Elapsed.Ticks}");
            witness.NeedsMindReconsideration = true;
        }

        AudioCueSystem.Emit(state, AudioCueKind.Hostile, robot.Id, robot.CurrentRoomId);
        Log(state, $"{robot.Name} attacks {target.Name} for {damage} damage.");

        if (target.Health <= 0)
        {
            target.CauseOfDeath = $"Killed by {robot.Name}.";
            target.Intent = null;
            target.Movement = null;
            target.RoutineUntil = TimeSpan.Zero;
            target.CurrentAction = new NpcAction(ActionKind.Idle, null, "Deceased.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, target.Id.ToString(), target.CurrentRoomId);
            Log(state, $"CRITICAL: {target.Name} has died — {target.CauseOfDeath}");
        }
    }

    private void TickPower(GameState state, StationRobot robot, TimeSpan delta)
    {
        if (robot.IsDestroyed)
        {
            robot.BatteryPercent = 0;
            return;
        }

        if (CanCharge(state, robot) && robot.CurrentRoomId.Equals(ChargingRoomId, StringComparison.OrdinalIgnoreCase))
        {
            robot.BatteryPercent = Math.Min(100, robot.BatteryPercent + (2.8 * delta.TotalMinutes));
            return;
        }

        if (robot.IsOperational)
        {
            robot.BatteryPercent = Math.Max(0, robot.BatteryPercent - (0.32 * delta.TotalMinutes));
        }
    }

    private static bool CanCharge(GameState state, StationRobot robot) =>
        robot.ChargingEnabled
        && state.Facility.Rooms.TryGetValue(ChargingRoomId, out var room)
        && room.IsPowered;

    private (string TargetId, string RequiredRoomId, string Description)? FindRepairTarget(
        GameState state,
        StationRobot robot)
    {
        if (CrewCounterplaySystem.HasSwitchedOffLifeSupport(state))
        {
            var path = _navigation.FindPath(state.Facility, robot.CurrentRoomId, ChargingRoomId);
            if (robot.CurrentRoomId.Equals(ChargingRoomId, StringComparison.OrdinalIgnoreCase)
                || path.Count >= 2)
            {
                return (CrewCounterplaySystem.LifeSupportTarget, ChargingRoomId, "primary life support");
            }
        }

        return state.Facility.Rooms.Values
            .Where(room => CrewCounterplaySystem.HasRestorableProblem(state, room.Id))
            .Select(room => new
            {
                Room = room,
                Path = _navigation.FindPath(state.Facility, robot.CurrentRoomId, room.Id)
            })
            .Where(candidate =>
                candidate.Room.Id.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                || candidate.Path.Count >= 2)
            .OrderBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Room.Id)
            .Select(candidate =>
                ((string TargetId, string RequiredRoomId, string Description)?)
                (candidate.Room.Id, candidate.Room.Id, $"{candidate.Room.Name} systems"))
            .FirstOrDefault();
    }

    private bool TryRouteTo(
        GameState state,
        StationRobot robot,
        string targetRoomId,
        string task)
    {
        robot.TargetRoomId = targetRoomId;

        if (robot.CurrentRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
        {
            robot.Movement = null;
            robot.CurrentTask = task;
            return false;
        }

        if (robot.Movement is not null)
        {
            robot.CurrentTask = task;
            return true;
        }

        var path = _navigation.FindPath(state.Facility, robot.CurrentRoomId, targetRoomId);
        if (path.Count < 2)
        {
            robot.CurrentTask = $"Cannot reach {targetRoomId}; route sealed.";
            return false;
        }

        var door = state.Facility.FindDoorBetween(path[0], path[1]);
        if (door is null || !door.IsPassable)
        {
            robot.CurrentTask = $"Cannot reach {targetRoomId}; route sealed.";
            return false;
        }

        robot.Movement = MovementGeometry.CreateOrder(
            door,
            state.Facility.Rooms[path[0]],
            state.Facility.Rooms[path[1]]);
        robot.CurrentTask = task;
        return true;
    }

    private static StationRobot? FindRobot(GameState state, string robotId) =>
        state.Robots.FirstOrDefault(robot =>
            robot.Id.Equals(robotId, StringComparison.OrdinalIgnoreCase));

    private static void RecordPolicyEvidence(
        GameState state,
        StationRobot robot,
        RobotPolicy previous,
        RobotPolicy current)
    {
        if (current == RobotPolicy.Friendly)
        {
            return;
        }

        var weight = current == RobotPolicy.Hostile ? 18 : 7;
        var description = current == RobotPolicy.Hostile
            ? $"I saw {robot.Name} switch from {previous} into an armed hostile policy under Overseer control."
            : $"I saw {robot.Name} leave crew-assist policy and stop prioritising repairs.";

        RecordWitnessEvidence(
            state,
            robot,
            description,
            weight,
            $"robot-policy:{robot.Id}:{state.Elapsed.Ticks}");
    }

    private static void RecordWitnessEvidence(
        GameState state,
        StationRobot robot,
        string description,
        double weight,
        string evidenceId)
    {
        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase)))
        {
            SuspicionSystem.AddEvidence(
                state,
                witness,
                description,
                weight,
                origin: EvidenceOrigin.DirectObservation,
                locationId: robot.CurrentRoomId,
                evidenceId: evidenceId);
            witness.NeedsMindReconsideration = true;
        }
    }

    private static int StableIndex(string value)
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

    private static void Log(GameState state, string message)
    {
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
    }
}
