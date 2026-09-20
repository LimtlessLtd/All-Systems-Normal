using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class IntentExecutionSystem
{
    private readonly NavigationSystem _navigation = new();
    private readonly ActionResolver _actions = new();

    public void Tick(GameState state)
    {
        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.Intent is not null))
        {
            var intent = npc.Intent!;

            if (state.Elapsed - intent.CreatedAt > TimeSpan.FromMinutes(20))
            {
                npc.Intent = null;
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "My previous goal no longer feels relevant.");
                continue;
            }

            switch (intent.Action)
            {
                case ActionKind.Eat:
                    MoveOrActInRoom(state, npc, intent, "kitchen", ActionKind.Eat);
                    break;

                case ActionKind.Rest:
                case ActionKind.Sleep:
                    MoveOrActInRoom(state, npc, intent, "quarters", intent.Action);
                    break;

                case ActionKind.Recreate:
                    MoveOrActInRoom(state, npc, intent, "lounge", ActionKind.Recreate);
                    break;

                case ActionKind.Groom:
                case ActionKind.Shower:
                case ActionKind.UseToilet:
                    MoveOrActInRoom(state, npc, intent, "washroom", intent.Action);
                    break;

                case ActionKind.Move:
                case ActionKind.Investigate:
                case ActionKind.Repair:
                case ActionKind.Work:

                // Provisioning work happens at a place, so getting there is the
                // same problem as any other room intent. CrewProvisioningSystem
                // resolves what happens once they arrive.
                case ActionKind.TendCrops:
                case ActionKind.Harvest:
                case ActionKind.Cook:
                    ExecuteRoomIntent(state, npc, intent);
                    break;

                case ActionKind.Talk:
                case ActionKind.Socialize:
                case ActionKind.Argue:
                case ActionKind.RequestHelp:
                case ActionKind.RecruitShutdownAlly:
                    ExecuteSocialIntent(state, npc, intent);
                    break;

                case ActionKind.JoinShutdownTeam:
                    ExecuteJoinShutdownTeamIntent(state, npc, intent);
                    break;

                case ActionKind.ShutdownOverseer:
                    ExecuteShutdownIntent(state, npc, intent);
                    break;

                case ActionKind.ForceDoor:
                    ExecuteForceDoorIntent(state, npc, intent);
                    break;

                case ActionKind.RestoreSystem:
                    ExecuteRestoreSystemIntent(state, npc, intent);
                    break;

                case ActionKind.SecureAirlock:
                    ExecuteSecureAirlockIntent(state, npc, intent);
                    break;

                case ActionKind.RepairDoor:
                case ActionKind.WeldDoor:
                case ActionKind.BarricadeDoor:
                    ExecuteDoorWorkIntent(state, npc, intent);
                    break;

                case ActionKind.ShutdownRobot:
                case ActionKind.IsolateRobotNetwork:
                case ActionKind.DisableRobotCharging:
                case ActionKind.DamageRobot:
                case ActionKind.ReprogramRobot:
                    ExecuteRobotCountermeasureIntent(state, npc, intent);
                    break;

                case ActionKind.Idle:
                    npc.CurrentAction = new NpcAction(
                        ActionKind.Idle,
                        null,
                        intent.Reason);
                    npc.Intent = null;
                    break;

                // Only the deterministic routine/social layer creates this
                // intent. Final mutual-interest checks happen again on arrival.
                case ActionKind.Intimacy:
                    ExecuteIntimacyIntent(state, npc, intent);
                    break;

                // Violence remains simulation-controlled. LLMs may express anger,
                // but lethal outcomes must emerge from deterministic social rules.
                case ActionKind.Attack:
                    npc.Intent = intent with
                    {
                        Action = ActionKind.Argue,
                        Goal = $"Confront {intent.TargetId}",
                        Reason = $"{intent.Reason} I decide to confront them rather than attack outright."
                    };
                    break;
            }
        }
    }

    private void ExecuteRobotCountermeasureIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var robot = RobotCountermeasureSystem.FindRobot(state, intent.TargetId);
        if (robot is null || robot.IsDestroyed)
        {
            FailIntent(npc, "That robot is no longer an actionable target.");
            return;
        }

        if (intent.Action is ActionKind.IsolateRobotNetwork
            or ActionKind.DisableRobotCharging)
        {
            if (!npc.CurrentRoomId.Equals(
                    RobotCountermeasureSystem.ControlRoomId,
                    StringComparison.OrdinalIgnoreCase))
            {
                MoveTowardRoom(
                    state,
                    npc,
                    intent,
                    RobotCountermeasureSystem.ControlRoomId);
                return;
            }
        }
        else if (!RobotCountermeasureSystem.IsCoLocated(npc, robot))
        {
            MoveTowardRoom(state, npc, intent, robot.CurrentRoomId);
            return;
        }

        if (_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(intent.Action, robot.Id, intent.Reason),
                out _))
        {
            npc.Intent = null;
        }
    }

    private void ExecuteDoorWorkIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));
        if (door is null)
        {
            FailIntent(npc, "I cannot identify that hatch.");
            return;
        }

        var adjacent = npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
            || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);
        if (!adjacent)
        {
            FailIntent(npc, "I need to be beside that hatch before working on it.");
            return;
        }

        _actions.TryApply(state, npc.Id,
            new NpcAction(intent.Action, door.Id, intent.Reason), out _);
    }

    private void ExecuteForceDoorIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(
                intent.TargetId,
                StringComparison.OrdinalIgnoreCase));

        if (door is null)
        {
            FailIntent(npc, "I cannot identify that hatch.");
            return;
        }

        var adjacent =
            npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
            || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

        if (!adjacent)
        {
            FailIntent(npc, "I need to be beside that hatch before I can defeat it.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.ForceDoor,
                door.Id,
                intent.Reason),
            out _);
    }

    private void ExecuteSecureAirlockIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetId)
            || !state.Facility.Rooms.TryGetValue(intent.TargetId, out var airlock)
            || airlock.Type != RoomType.Airlock
            || !airlock.HasExteriorHatch
            || !AirlockSafetySystem.NeedsCrewSecuring(state, airlock))
        {
            FailIntent(npc, "The airlock no longer needs emergency securing.");
            return;
        }

        if (!AirlockSafetySystem.CanCrewSecure(npc))
        {
            FailIntent(npc, "I do not know the emergency airlock controls well enough.");
            return;
        }

        if (!AirlockSafetySystem.IsAtCrewControls(state, npc, airlock))
        {
            var controlRoomId = AirlockSafetySystem.CrewControlRoomId(
                state,
                airlock);

            if (controlRoomId is null)
            {
                FailIntent(npc, "I cannot identify the airlock emergency controls.");
                return;
            }

            MoveTowardRoom(
                state,
                npc,
                intent,
                controlRoomId);
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.SecureAirlock,
                airlock.Id,
                intent.Reason),
            out _);
    }

    private void ExecuteRestoreSystemIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetId)
            || !CrewCounterplaySystem.HasRestorableProblem(
                state,
                intent.TargetId))
        {
            FailIntent(npc, "That system no longer needs restoration.");
            return;
        }

        var requiredRoom = CrewCounterplaySystem.RequiredRoomForRestore(
            state,
            intent.TargetId);

        if (requiredRoom is null)
        {
            FailIntent(npc, "I cannot identify where those controls are.");
            return;
        }

        if (!npc.CurrentRoomId.Equals(
                requiredRoom,
                StringComparison.OrdinalIgnoreCase))
        {
            MoveTowardRoom(
                state,
                npc,
                intent,
                requiredRoom);
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.RestoreSystem,
                intent.TargetId,
                intent.Reason),
            out _);
    }

    private void ExecuteShutdownIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
            m.IsOnline && m.Id.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));
        if (mechanism is null || !SuspicionSystem.KnowsMechanism(npc, mechanism))
        {
            FailIntent(npc, "I have not personally verified that shutdown control.");
            return;
        }

        var team = state.ShutdownTeams.FirstOrDefault(candidate =>
            candidate.IsActive
            && candidate.MechanismId.Equals(mechanism.Id, StringComparison.OrdinalIgnoreCase)
            && candidate.MemberIds.Contains(npc.Id));

        if (mechanism.RequiredCrewCount > 1
            && (team is null || team.MemberIds.Count < mechanism.RequiredCrewCount))
        {
            FailIntent(npc, $"I need a coordinated team before attempting {mechanism.Label}.");
            npc.NeedsMindReconsideration = true;
            return;
        }

        if (npc.CurrentRoomId.Equals(mechanism.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            if (_actions.TryApply(state, npc.Id,
                new NpcAction(ActionKind.ShutdownOverseer, mechanism.Id, intent.Reason), out _))
            {
                npc.RoutineUntil = TimeSpan.Zero;
                npc.Intent = null;
            }
            return;
        }

        var path = _navigation.FindPath(
            state.Facility,
            npc.CurrentRoomId,
            mechanism.RoomId);

        if (path.Count >= 2)
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    path[1],
                    $"Pursuing shutdown goal: {intent.Goal}"),
                out _);
            return;
        }

        if (!mechanism.CrewCanOverrideRoute)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                mechanism.RoomId,
                $"I want to reach {mechanism.Label}, but its route is sealed.");
            return;
        }

        var topologyPath = _navigation.FindPathIgnoringDoorState(
            state.Facility,
            npc.CurrentRoomId,
            mechanism.RoomId);

        if (topologyPath.Count < 2)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                mechanism.RoomId,
                $"I cannot identify a physical route to {mechanism.Label}.");
            return;
        }

        var nextRoomId = topologyPath[1];
        var blockingDoor = state.Facility.FindDoorBetween(
            npc.CurrentRoomId,
            nextRoomId);

        if (blockingDoor is null)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                mechanism.RoomId,
                "The route topology is incomplete.");
            return;
        }

        if (blockingDoor.IsPassable)
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    nextRoomId,
                    $"Advancing toward {mechanism.Label}."),
                out _);
            return;
        }

        if (npc.CurrentAction.Kind == ActionKind.OverrideDoor
            && npc.CurrentAction.TargetId == blockingDoor.Id
            && npc.RoutineUntil > state.Elapsed)
        {
            return;
        }

        if (_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.OverrideDoor,
                    blockingDoor.Id,
                    $"Force a route toward {mechanism.Label}."),
                out _))
        {
            npc.Bubble = new NpcBubble(
                "Overseer sealed it. I'm overriding the hatch.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(4));
            return;
        }

        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            blockingDoor.Id,
            $"The route to {mechanism.Label} is sealed and I cannot override {blockingDoor.Id}.");
    }

    private void ExecuteRoomIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var room = ResolveRoom(state, intent.TargetId);

        if (room is null)
        {
            FailIntent(npc, "I cannot identify where to go.");
            return;
        }

        MoveOrActInRoom(state, npc, intent, room.Id, intent.Action);
    }

    private void MoveOrActInRoom(
        GameState state,
        Npc npc,
        NpcIntent intent,
        string targetRoomId,
        ActionKind arrivalAction)
    {
        if (npc.CurrentRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(arrivalAction, targetRoomId, intent.Reason),
                out _);

            if (arrivalAction is not (ActionKind.Rest or ActionKind.Sleep))
            {
                npc.Intent = null;
            }

            return;
        }

        var path = _navigation.FindPath(state.Facility, npc.CurrentRoomId, targetRoomId);

        if (path.Count < 2)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                targetRoomId,
                $"I want to: {intent.Goal}, but every known route is sealed.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.Move,
                path[1],
                $"Pursuing goal: {intent.Goal}"),
            out _);
    }

    private void ExecuteIntimacyIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetId))
        {
            FailIntent(npc, "I need a specific consenting partner.");
            return;
        }

        var partner = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.Id != npc.Id
            && other.Name.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));

        if (partner is null)
        {
            FailIntent(npc, $"I cannot find {intent.TargetId}.");
            return;
        }

        if (!npc.CurrentRoomId.Equals("quarters", StringComparison.OrdinalIgnoreCase))
        {
            MoveTowardRoom(state, npc, intent, "quarters");
            return;
        }

        if (!partner.CurrentRoomId.Equals("quarters", StringComparison.OrdinalIgnoreCase))
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                partner.Name,
                $"Waiting in crew quarters for {partner.Name}.");
            return;
        }

        if (_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.Intimacy, partner.Name, intent.Reason),
                out _))
        {
            npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(20);
            npc.Bubble = new NpcBubble(
                "Some privacy, please.",
                NpcBubbleKind.Speech,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(4));
            npc.Intent = null;
        }
        else
        {
            FailIntent(npc, "Private time no longer feels mutually right.");
        }
    }

    private void MoveTowardRoom(
        GameState state,
        Npc npc,
        NpcIntent intent,
        string targetRoomId)
    {
        var path = _navigation.FindPath(
            state.Facility,
            npc.CurrentRoomId,
            targetRoomId);

        if (path.Count < 2)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                targetRoomId,
                $"I want to: {intent.Goal}, but every known route is sealed.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.Move,
                path[1],
                $"Pursuing goal: {intent.Goal}"),
            out _);
    }

    private void ExecuteJoinShutdownTeamIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var invitation = npc.PendingShutdownTeamInvitation;
        if (invitation is null
            || string.IsNullOrWhiteSpace(intent.TargetId)
            || !invitation.TeamId.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            FailIntent(npc, "There is no matching shutdown-team invitation.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.JoinShutdownTeam,
                invitation.TeamId,
                intent.Reason),
            out _);

        npc.Intent = null;
    }

    private void ExecuteSocialIntent(GameState state, Npc npc, NpcIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetId))
        {
            FailIntent(npc, "I need a specific person for this goal.");
            return;
        }

        var target = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.Name.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            FailIntent(npc, $"I cannot find {intent.TargetId}.");
            return;
        }

        if (npc.CurrentRoomId.Equals(target.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(intent.Action, target.Name, intent.Reason),
                out _);

            npc.Intent = null;
            return;
        }

        var path = _navigation.FindPath(
            state.Facility,
            npc.CurrentRoomId,
            target.CurrentRoomId);

        if (path.Count < 2)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                target.Name,
                $"I want to reach {target.Name}, but the route is sealed.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.Move,
                path[1],
                $"Trying to reach {target.Name}: {intent.Goal}"),
            out _);
    }

    private static Room? ResolveRoom(GameState state, string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        if (state.Facility.Rooms.TryGetValue(targetId, out var byId))
        {
            return byId;
        }

        return state.Facility.Rooms.Values.FirstOrDefault(room =>
            room.Name.Equals(targetId, StringComparison.OrdinalIgnoreCase));
    }

    private static void FailIntent(Npc npc, string reason)
    {
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
        npc.Intent = null;
    }
}
