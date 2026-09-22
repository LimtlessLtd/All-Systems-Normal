using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Resolves crew countermeasures against the station robot. Intent formation may
/// come from Ollama or the deterministic browser mind, but every outcome here
/// requires authoritative physical position, skill and elapsed action time.
/// </summary>
public sealed class RobotCountermeasureSystem
{
    public const string ControlRoomId = "engineering";

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            switch (npc.CurrentAction.Kind)
            {
                case ActionKind.ShutdownRobot:
                    TickLocalShutdown(state, npc);
                    break;
                case ActionKind.IsolateRobotNetwork:
                    TickNetworkIsolation(state, npc);
                    break;
                case ActionKind.DisableRobotCharging:
                    TickChargingDenial(state, npc);
                    break;
                case ActionKind.DamageRobot:
                    TickDamage(state, npc);
                    break;
                case ActionKind.ReprogramRobot:
                    TickReprogram(state, npc);
                    break;
            }
        }
    }

    public static StationRobot? FindRobot(GameState state, string? robotId) =>
        string.IsNullOrWhiteSpace(robotId)
            ? null
            : state.Robots.FirstOrDefault(robot =>
                robot.Id.Equals(robotId, StringComparison.OrdinalIgnoreCase));

    public static bool IsCoLocated(Npc npc, StationRobot robot) =>
        npc.CurrentRoomId.Equals(robot.CurrentRoomId, StringComparison.OrdinalIgnoreCase);

    public static bool HasHostileRobotEvidence(Npc npc, StationRobot robot) =>
        npc.OverseerEvidence.Any(evidence =>
            evidence.CurrentWeight > 1
            && evidence.Description.Contains(robot.Name, StringComparison.OrdinalIgnoreCase)
            && (evidence.Description.Contains("attack", StringComparison.OrdinalIgnoreCase)
                || evidence.Description.Contains("hostile", StringComparison.OrdinalIgnoreCase)));

    public static bool CanUseEngineeringControls(Npc npc) =>
        npc.CurrentRoomId.Equals(ControlRoomId, StringComparison.OrdinalIgnoreCase)
        && CrewCounterplaySystem.BestTechnicalSkill(npc) >= 45;

    /// <summary>
    /// Deterministic countermeasure decision, shared by every mind (LLM
    /// fallback and browser-demo) that considers acting against a robot.
    /// Neither mind decides physical outcomes; this only decides which
    /// action is available to propose given current skill/visibility/evidence.
    /// </summary>
    public static CountermeasureDecision? FindCountermeasure(GameState state, Npc npc)
    {
        var visibleRobot = state.Robots.FirstOrDefault(robot =>
            !robot.IsDestroyed
            && robot.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (visibleRobot is not null)
        {
            if (!visibleRobot.IsOperational
                && visibleRobot.Policy == RobotPolicy.Hostile
                && CrewCounterplaySystem.BestTechnicalSkill(npc) >= 65)
            {
                return new CountermeasureDecision(
                    ActionKind.ReprogramRobot,
                    visibleRobot.Id,
                    $"Reprogram and reboot {visibleRobot.Name}.",
                    "The hostile robot is shut down in front of me and I can reach its local service port.",
                    96);
            }

            if (visibleRobot.Policy == RobotPolicy.Hostile && visibleRobot.IsOperational)
            {
                var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
                var force = CrewCounterplaySystem.BestForceSkill(npc);

                return technical >= 45
                    ? new CountermeasureDecision(
                        ActionKind.ShutdownRobot,
                        visibleRobot.Id,
                        $"Use {visibleRobot.Name}'s manual shutdown.",
                        "A hostile robot is physically here; I want to stop it at the local emergency cutoff.",
                        100)
                    : new CountermeasureDecision(
                        ActionKind.DamageRobot,
                        visibleRobot.Id,
                        $"Physically disable {visibleRobot.Name}.",
                        force >= 40
                            ? "A hostile robot is physically here and force is the countermeasure I can attempt."
                            : "The robot is an immediate threat; I have no safer technical option.",
                        100);
            }
        }

        var knownThreat = state.Robots.FirstOrDefault(robot =>
            !robot.IsDestroyed
            && HasHostileRobotEvidence(npc, robot));

        if (knownThreat is null)
        {
            return null;
        }

        var technicalSkill = CrewCounterplaySystem.BestTechnicalSkill(npc);
        if (!knownThreat.IsNetworkIsolated && technicalSkill >= 55)
        {
            return new CountermeasureDecision(
                ActionKind.IsolateRobotNetwork,
                knownThreat.Id,
                $"Isolate {knownThreat.Name} from Overseer's control link.",
                "I have direct evidence the robot is dangerous and Engineering has a physical network isolation control.",
                94);
        }

        if (knownThreat.ChargingEnabled && technicalSkill >= 45)
        {
            return new CountermeasureDecision(
                ActionKind.DisableRobotCharging,
                knownThreat.Id,
                $"Cut power to {knownThreat.Name}'s charging circuit.",
                "I have direct evidence the robot is dangerous and can deny its charger from Engineering.",
                88);
        }

        return null;
    }

    private static void TickLocalShutdown(GameState state, Npc npc)
    {
        var robot = FindRobot(state, npc.CurrentAction.TargetId);
        if (robot is null || robot.IsDestroyed || !IsCoLocated(npc, robot))
        {
            End(npc, "I need to be physically beside the robot to use its manual shutdown.");
            return;
        }

        if (robot.IsLocallyShutdown)
        {
            End(npc, $"{robot.Name} is already locally shut down.");
            return;
        }

        var score = Math.Max(
            CrewCounterplaySystem.BestTechnicalSkill(npc),
            npc.Skills.TryGetValue("Security", out var security) ? security : 0);
        if (score < 45)
        {
            End(npc, "I cannot safely operate the robot's manual shutdown.");
            return;
        }

        if (!BeginOrComplete(state, npc, 2, $"using {robot.Name}'s manual shutdown"))
        {
            return;
        }

        robot.IsLocallyShutdown = true;
        robot.Movement = null;
        robot.TargetNpcId = null;
        robot.TargetRoomId = null;
        robot.ActionCompletesAt = null;
        robot.CurrentTask = $"Locally shut down by {npc.Name}.";
        End(npc, $"{robot.Name} is locally shut down.");
        AudioCueSystem.Emit(state, AudioCueKind.Important, npc.Id.ToString(), npc.CurrentRoomId);
        Log(state, $"{npc.Name} physically shuts down {robot.Name}.");
    }

    private static void TickNetworkIsolation(GameState state, Npc npc)
    {
        var robot = FindRobot(state, npc.CurrentAction.TargetId);
        if (robot is null || robot.IsDestroyed)
        {
            End(npc, "That robot is not available.");
            return;
        }

        if (!npc.CurrentRoomId.Equals(ControlRoomId, StringComparison.OrdinalIgnoreCase)
            || CrewCounterplaySystem.BestTechnicalSkill(npc) < 55)
        {
            End(npc, "I need Engineering access and enough technical skill to isolate the robot control link.");
            return;
        }

        if (robot.IsNetworkIsolated)
        {
            End(npc, $"{robot.Name}'s remote link is already isolated.");
            return;
        }

        if (!BeginOrComplete(state, npc, 2, $"isolating {robot.Name}'s control network"))
        {
            return;
        }

        robot.IsNetworkIsolated = true;
        robot.CurrentTask = robot.IsOperational
            ? "Remote control link isolated; local autonomy continuing."
            : robot.CurrentTask;
        End(npc, $"{robot.Name}'s remote control link is isolated.");
        Log(state, $"{npc.Name} isolates {robot.Name} from Overseer's remote control network.");
    }

    private static void TickChargingDenial(GameState state, Npc npc)
    {
        var robot = FindRobot(state, npc.CurrentAction.TargetId);
        if (robot is null || robot.IsDestroyed)
        {
            End(npc, "That robot is not available.");
            return;
        }

        if (!npc.CurrentRoomId.Equals(ControlRoomId, StringComparison.OrdinalIgnoreCase)
            || CrewCounterplaySystem.BestTechnicalSkill(npc) < 45)
        {
            End(npc, "I need the Engineering charging controls to deny robot power.");
            return;
        }

        if (!robot.ChargingEnabled)
        {
            End(npc, $"{robot.Name}'s charger is already disabled.");
            return;
        }

        if (!BeginOrComplete(state, npc, 1, $"cutting power to {robot.Name}'s charging circuit"))
        {
            return;
        }

        robot.ChargingEnabled = false;
        End(npc, $"{robot.Name}'s charger is disabled.");
        Log(state, $"{npc.Name} disables {robot.Name}'s charging circuit.");
    }

    private static void TickDamage(GameState state, Npc npc)
    {
        var robot = FindRobot(state, npc.CurrentAction.TargetId);
        if (robot is null || robot.IsDestroyed || !IsCoLocated(npc, robot))
        {
            End(npc, "I need to be physically beside the robot to damage it.");
            return;
        }

        var score = CrewCounterplaySystem.BestForceSkill(npc);
        if (score < 40)
        {
            End(npc, "I cannot find a credible way to damage the robot.");
            return;
        }

        if (!BeginOrComplete(state, npc, 1, $"physically disabling {robot.Name}"))
        {
            return;
        }

        var damage = 24 + (StableRoll(npc.Name, robot.Id, (int)state.Elapsed.TotalMinutes) % 17);
        robot.Health = Math.Max(0, robot.Health - damage);
        if (robot.IsDestroyed)
        {
            robot.Movement = null;
            robot.TargetNpcId = null;
            robot.TargetRoomId = null;
            robot.CurrentTask = "Destroyed by crew countermeasure.";
        }

        End(npc, robot.IsDestroyed
            ? $"{robot.Name} is disabled beyond operation."
            : $"Damaged {robot.Name}; integrity is {robot.Health:0}%.");
        AudioCueSystem.Emit(state, AudioCueKind.Hostile, npc.Id.ToString(), npc.CurrentRoomId);
        Log(state, $"{npc.Name} damages {robot.Name} for {damage}; integrity {robot.Health:0}%.");
    }

    private static void TickReprogram(GameState state, Npc npc)
    {
        var robot = FindRobot(state, npc.CurrentAction.TargetId);
        if (robot is null || robot.IsDestroyed || !IsCoLocated(npc, robot))
        {
            End(npc, "I need the intact robot physically in front of me to reprogram it.");
            return;
        }

        if (robot.IsOperational)
        {
            End(npc, $"{robot.Name} must be shut down before local reprogramming.");
            return;
        }

        if (CrewCounterplaySystem.BestTechnicalSkill(npc) < 65)
        {
            End(npc, "I do not have enough technical skill to rewrite the local robot policy.");
            return;
        }

        if (!BeginOrComplete(state, npc, 3, $"reprogramming and locally rebooting {robot.Name}"))
        {
            return;
        }

        robot.Policy = RobotPolicy.Friendly;
        robot.IsLocallyShutdown = false;
        robot.IsRemotelyShutdown = false;
        robot.TargetNpcId = null;
        robot.TargetRoomId = null;
        robot.ActionCompletesAt = null;
        robot.NextAttackAt = null;
        robot.CurrentTask = "Locally reprogrammed to crew-assist policy.";
        End(npc, $"{robot.Name} is locally rebooted under crew-assist policy.");
        AudioCueSystem.Emit(state, AudioCueKind.Important, npc.Id.ToString(), npc.CurrentRoomId);
        Log(state, $"{npc.Name} locally reprograms {robot.Name} to Friendly policy.");
    }

    private static bool BeginOrComplete(
        GameState state,
        Npc npc,
        int minutes,
        string activity)
    {
        if (npc.RoutineUntil == TimeSpan.Zero)
        {
            npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(minutes);
            npc.Bubble = new NpcBubble(
                $"I'm {activity}.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(Math.Max(2, minutes)));
            Log(state, $"{npc.Name} begins {activity}.");
            return false;
        }

        return state.Elapsed >= npc.RoutineUntil;
    }

    private static void End(Npc npc, string reason)
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
            foreach (var ch in $"{first}|{second}|{minute}|robot")
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
