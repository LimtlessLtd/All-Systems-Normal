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

            // Physical timed work is sticky. Urgency is a mind hint, not a
            // magic cancellation token. Once hands-on work starts, only a
            // deterministic immediate survival threat can pre-empt it.
            if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress } task
                && intent.Action != task.Action)
            {
                if (!CrewTaskSystem.CanInterruptForLifeThreat(state, npc, intent))
                {
                    npc.Intent = null;
                    npc.Plan = null;
                    continue;
                }

                var reason = $"Emergency interruption: {intent.Action} in an immediate life-threatening situation.";
                if (npc.ProvisioningJob is not null)
                    CrewProvisioningSystem.InterruptForExternalPriority(state, npc, reason);
                else
                    CrewTaskSystem.Interrupt(state, npc, reason);

                npc.RoutineUntil = TimeSpan.Zero;
            }

            if (state.Elapsed - intent.CreatedAt > IntentLifetime(intent))
            {
                npc.Intent = null;
                npc.Plan = null;
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "My previous goal no longer feels relevant.");
                continue;
            }

            switch (intent.Action)
            {
                case ActionKind.Eat:
                    ExecuteEatIntent(state, npc, intent);
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

                case ActionKind.EvacuateHazard:
                case ActionKind.Move:
                case ActionKind.Investigate:
                case ActionKind.Repair:
                case ActionKind.Work:
                case ActionKind.InspectEquipment:
                case ActionKind.VerifyClaim:
                case ActionKind.StandGuard:
                case ActionKind.SeekSafety:

                // Provisioning work happens at a place, so getting there is the
                // same problem as any other room intent. CrewProvisioningSystem
                // resolves what happens once they arrive.
                case ActionKind.TendCrops:
                case ActionKind.Harvest:
                case ActionKind.Cook:
                    ExecuteRoomIntent(state, npc, intent);
                    break;

                case ActionKind.MedicalCheckup:
                case ActionKind.TreatInjury:
                case ActionKind.AdministerMedication:
                case ActionKind.ResurrectCrew:
                    // Clinical outcomes are simulation-authoritative. A model may
                    // express the intent, but MedicalSystem owns eligibility,
                    // timing, supplies, power and health mutation.
                    npc.CurrentAction = new NpcAction(
                        ActionKind.Idle,
                        intent.TargetId,
                        "Clinical request noted; medbay protocols determine the outcome.");
                    npc.Intent = null;
                    break;

                case ActionKind.FightFire:
                case ActionKind.SealHazardRoom:
                case ActionKind.VentHazardRoom:
                    ExecuteHazardIntent(state, npc, intent);
                    break;

                case ActionKind.CleanBlood:
                    var cleaningRoom = ResolveRoom(state, intent.TargetId)
                        ?? state.Facility.Rooms[npc.CurrentRoomId];
                    if (!npc.CurrentRoomId.Equals(cleaningRoom.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        MoveTowardRoom(state, npc, intent, cleaningRoom.Id);
                    }
                    else
                    {
                        npc.CurrentAction = new NpcAction(
                            ActionKind.CleanBlood,
                            cleaningRoom.Id,
                            intent.Reason);
                        npc.Intent = null;
                    }
                    break;

                case ActionKind.Talk:
                case ActionKind.Socialize:
                case ActionKind.Argue:
                case ActionKind.RequestHelp:
                case ActionKind.CheckOnCrew:
                case ActionKind.AssistCrew:
                case ActionKind.AskAboutLocation:
                case ActionKind.CoordinateWork:
                case ActionKind.ReassureCrew:
                case ActionKind.MisleadCrew:
                case ActionKind.ReportConcern:
                case ActionKind.ProposePact:
                case ActionKind.Suggest:
                case ActionKind.RecruitShutdownAlly:
                    ExecuteSocialIntent(state, npc, intent);
                    break;

                case ActionKind.AcceptPact:
                    ExecuteAcceptPactIntent(state, npc, intent);
                    break;

                case ActionKind.FulfillPact:
                case ActionKind.BreakPact:
                    ExecuteSettlePactIntent(state, npc, intent);
                    break;

                case ActionKind.HideItem:
                case ActionKind.ReturnItem:
                case ActionKind.BorrowItem:
                case ActionKind.StealItem:
                case ActionKind.DestroyItem:
                    ExecutePossessionIntent(state, npc, intent);
                    break;

                case ActionKind.JoinShutdownTeam:
                    ExecuteJoinShutdownTeamIntent(state, npc, intent);
                    break;

                case ActionKind.ShutdownOverseer:
                    ExecuteShutdownIntent(state, npc, intent);
                    break;

                case ActionKind.OpenDoor:
                case ActionKind.CloseDoor:
                case ActionKind.LockDoor:
                case ActionKind.UnlockDoor:
                    ExecuteCrewDoorOperationIntent(state, npc, intent);
                    break;

                case ActionKind.ForceDoor:
                    ExecuteForceDoorIntent(state, npc, intent);
                    break;

                case ActionKind.DisconnectDevice:
                    ExecuteDisconnectDeviceIntent(state, npc, intent);
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

                case ActionKind.DisarmTurret:
                case ActionKind.IsolateTurretNetwork:
                case ActionKind.DisableTurretPower:
                case ActionKind.DamageTurret:
                case ActionKind.ReprogramTurret:
                    ExecuteTurretCountermeasureIntent(state, npc, intent);
                    break;

                case ActionKind.RecapturePrisoner:
                    ExecuteRecapturePrisonerIntent(state, npc, intent);
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

    private static TimeSpan IntentLifetime(NpcIntent intent)
    {
        // Ordinary deliberative goals remain deliberately short-lived so the
        // mind can reconsider. Survival needs and critical safety goals must
        // persist long enough to traverse a now-physical, collision-aware
        // station rather than being forgotten halfway to food or safety.
        if (intent.Urgency >= 90)
            return TimeSpan.FromMinutes(90);

        return intent.Action switch
        {
            ActionKind.Eat => TimeSpan.FromMinutes(75),
            ActionKind.Sleep or ActionKind.Rest => TimeSpan.FromMinutes(60),
            ActionKind.UseToilet => TimeSpan.FromMinutes(45),
            ActionKind.SeekSafety => TimeSpan.FromMinutes(90),
            _ => TimeSpan.FromMinutes(20)
        };
    }

    private void ExecuteRobotCountermeasureIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var robot = RobotCountermeasureSystem.FindRobot(state, intent.TargetId);
        if (robot is null || robot.IsDestroyed)
        {
            FailIntent(state, npc, "That robot is no longer an actionable target.");
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

    private void ExecuteTurretCountermeasureIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var turret = TurretCountermeasureSystem.FindTurret(state, intent.TargetId);
        if (turret is null || turret.IsDestroyed)
        {
            FailIntent(state, npc, "That security turret is no longer an actionable target.");
            return;
        }

        if (intent.Action is ActionKind.IsolateTurretNetwork
            or ActionKind.DisableTurretPower)
        {
            if (!npc.CurrentRoomId.Equals(
                    TurretCountermeasureSystem.ControlRoomId,
                    StringComparison.OrdinalIgnoreCase))
            {
                MoveTowardRoom(
                    state,
                    npc,
                    intent,
                    TurretCountermeasureSystem.ControlRoomId);
                return;
            }
        }
        else if (!TurretCountermeasureSystem.IsCoLocated(npc, turret))
        {
            MoveTowardRoom(state, npc, intent, turret.RoomId);
            return;
        }

        if (_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(intent.Action, turret.Id, intent.Reason),
                out _))
        {
            npc.Intent = null;
        }
    }

    private void ExecuteRecapturePrisonerIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var prisoner = state.Crew.FirstOrDefault(other =>
            other.IsPrisoner
            && other.IsAlive
            && other.IsPresent
            && other.HasEscapedContainment
            && other.Name.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));

        if (prisoner is null)
        {
            FailIntent(state, npc, "That escaped prisoner is no longer at large.");
            return;
        }

        if (!PrisonerContainmentSystem.IsCoLocated(npc, prisoner))
        {
            MoveTowardRoom(state, npc, intent, prisoner.CurrentRoomId);
            return;
        }

        if (_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.RecapturePrisoner, prisoner.Name, intent.Reason),
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
            FailIntent(state, npc, "I cannot identify that hatch.");
            return;
        }

        var adjacent = npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
            || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);
        if (!adjacent)
        {
            FailIntent(state, npc, "I need to be beside that hatch before working on it.");
            return;
        }

        _actions.TryApply(state, npc.Id,
            new NpcAction(intent.Action, door.Id, intent.Reason), out _);
    }


    private void ExecuteCrewDoorOperationIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));

        if (door is null || !CrewDoorInteractionSystem.IsAdjacent(npc, door))
        {
            if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress } staleDoorTask
                && staleDoorTask.Action == intent.Action
                && (door is null || staleDoorTask.TargetId == door.Id))
            {
                CrewTaskSystem.Interrupt(state, npc, "No longer beside the hatch to operate it.");
            }

            FailIntent(state, npc, "I need to be beside that hatch to operate it.");
            return;
        }

        if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress } doorTask
            && doorTask.Action == intent.Action
            && doorTask.TargetId == door.Id)
        {
            if (!CrewTaskSystem.IsComplete(state, npc))
                return;

            if (_actions.TryApply(
                    state,
                    npc.Id,
                    new NpcAction(intent.Action, door.Id, intent.Reason),
                    out var outcome))
            {
                CrewTaskSystem.Succeed(state, npc, outcome);
            }
            else
            {
                CrewTaskSystem.Fail(state, npc, outcome);
            }

            npc.Intent = null;
            return;
        }

        npc.CurrentAction = new NpcAction(
            intent.Action,
            door.Id,
            $"Operating {door.Id}.");
        CrewTaskSystem.Start(
            state,
            npc,
            intent.Action,
            door.Id,
            $"operating {door.Id}",
            TimeSpan.FromMinutes(1));
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
            FailIntent(state, npc, "I cannot identify that hatch.");
            return;
        }

        var adjacent =
            npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
            || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

        if (!adjacent)
        {
            FailIntent(state, npc, "I need to be beside that hatch before I can defeat it.");
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
            FailIntent(state, npc, "The airlock no longer needs emergency securing.");
            return;
        }

        if (!AirlockSafetySystem.CanCrewSecure(npc))
        {
            FailIntent(state, npc, "I do not know the emergency airlock controls well enough.");
            return;
        }

        if (!AirlockSafetySystem.IsAtCrewControls(state, npc, airlock))
        {
            var controlRoomId = AirlockSafetySystem.CrewControlRoomId(
                state,
                airlock);

            if (controlRoomId is null)
            {
                FailIntent(state, npc, "I cannot identify the airlock emergency controls.");
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

    private void ExecuteDisconnectDeviceIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetId)
            || !state.Devices.TryGetValue(intent.TargetId, out var device)
            || device.Kind == StationSystemKind.Door
            || device.IsFailed
            || !device.IsEnabled)
        {
            FailIntent(state, npc, "That machine can no longer be disconnected.");
            return;
        }

        if (!device.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
            || !state.Facility.Rooms.TryGetValue(device.RoomId, out var room))
        {
            FailIntent(state, npc, "I need to be in the machine's compartment to disconnect it.");
            return;
        }

        var fixture = LocalMovementSystem.FixtureForDevice(room, device.Kind);
        if (fixture is null)
        {
            FailIntent(state, npc, "I cannot find accessible local hardware for that machine.");
            return;
        }

        npc.CurrentAction = new NpcAction(
            ActionKind.DisconnectDevice,
            device.Id,
            intent.Reason);

        if (!LocalMovementSystem.IsAtInteractionPoint(room, npc, fixture))
        {
            return;
        }

        if (!_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.DisconnectDevice,
                    device.Id,
                    intent.Reason),
                out var message))
        {
            FailIntent(state, npc, message);
            return;
        }

        npc.Intent = null;
        npc.PlannedDestinationRoomId = null;
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
            FailIntent(state, npc, "That system no longer needs restoration.");
            return;
        }

        var requiredRoom = CrewCounterplaySystem.RequiredRoomForRestore(
            state,
            intent.TargetId);

        if (requiredRoom is null)
        {
            FailIntent(state, npc, "I cannot identify where those controls are.");
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
            FailIntent(state, npc, "I have not personally verified that shutdown control.");
            return;
        }

        var team = state.ShutdownTeams.FirstOrDefault(candidate =>
            candidate.IsActive
            && candidate.MechanismId.Equals(mechanism.Id, StringComparison.OrdinalIgnoreCase)
            && candidate.MemberIds.Contains(npc.Id));

        if (mechanism.RequiredCrewCount > 1
            && (team is null || team.MemberIds.Count < mechanism.RequiredCrewCount))
        {
            FailIntent(state, npc, $"I need a coordinated team before attempting {mechanism.Label}.");
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

        var path = _navigation.FindPathForCrew(
            state,
            npc,
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

    private void ExecuteHazardIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var room = ResolveRoom(state, intent.TargetId);
        if (room is null)
        {
            FailIntent(state, npc, "I cannot identify the hazard compartment.");
            return;
        }

        if (!npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
        {
            MoveTowardRoom(state, npc, intent, room.Id);
            return;
        }

        if (npc.ActiveTask is { Status: CrewTaskStatus.InProgress } hazardTask
            && hazardTask.Action == intent.Action
            && hazardTask.TargetId == room.Id)
        {
            if (!CrewTaskSystem.IsComplete(state, npc))
                return;

            if (!StationHazardSystem.TryExecuteCrewAction(
                    state,
                    npc,
                    intent.Action,
                    room,
                    out var message))
            {
                CrewTaskSystem.Fail(state, npc, message);
                FailIntent(state, npc, message);
                return;
            }

            CrewTaskSystem.Succeed(state, npc, message);
            npc.CurrentAction = new NpcAction(intent.Action, room.Id, message);
            npc.Intent = null;
            npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(2);
            return;
        }

        var duration = intent.Action switch
        {
            ActionKind.FightFire => TimeSpan.FromMinutes(3),
            ActionKind.SealHazardRoom => TimeSpan.FromMinutes(2),
            ActionKind.VentHazardRoom => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(1)
        };

        npc.CurrentAction = new NpcAction(
            intent.Action,
            room.Id,
            intent.Reason);
        CrewTaskSystem.Start(
            state,
            npc,
            intent.Action,
            room.Id,
            intent.Action switch
            {
                ActionKind.FightFire => $"suppressing the fire in {room.Name}",
                ActionKind.SealHazardRoom => $"sealing hatches around {room.Name}",
                ActionKind.VentHazardRoom => $"venting {room.Name}",
                _ => $"responding to the hazard in {room.Name}"
            },
            duration);
    }

    private void ExecuteRoomIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var room = ResolveRoom(state, intent.TargetId);

        if (room is null)
        {
            FailIntent(state, npc, "I cannot identify where to go.");
            return;
        }

        MoveOrActInRoom(state, npc, intent, room.Id, intent.Action);
    }

    // Owner idea #90: food is in the galley. A mind that wants to eat in the
    // recreation room or quarters walks to the galley, collects a portion and
    // carries it there. With no prepared meal to take, it eats in the galley.
    private void ExecuteEatIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var galleyId = DiningSeatRules.GalleyId(state);
        var diningRoomId = DiningSeatRules.DiningRoomFor(state, intent.TargetId);
        if (diningRoomId.Equals(galleyId, StringComparison.OrdinalIgnoreCase)
            || npc.CarriedMealPortion > 0)
        {
            MoveOrActInRoom(state, npc, intent, diningRoomId, ActionKind.Eat);
            return;
        }

        if (!npc.CurrentRoomId.Equals(galleyId, StringComparison.OrdinalIgnoreCase))
        {
            MoveOrActInRoom(state, npc, intent, galleyId, ActionKind.Eat);
            return;
        }

        if (!DiningSeatRules.TryCollectMeal(state, npc))
        {
            MoveOrActInRoom(state, npc, intent, galleyId, ActionKind.Eat);
            return;
        }

        var diningRoom = state.Facility.Rooms[diningRoomId];
        state.EventLog.Insert(
            0,
            $"T+{state.Elapsed:hh\\:mm}: {npc.Name} takes a meal from the galley to eat in {diningRoom.Name}.");
        MoveOrActInRoom(state, npc, intent, diningRoomId, ActionKind.Eat);
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
            npc.PlannedDestinationRoomId = null;
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

        npc.PlannedDestinationRoomId = targetRoomId;
        var path = _navigation.FindPathForCrew(
            state,
            npc,
            npc.CurrentRoomId,
            targetRoomId);

        if (path.Count < 2)
        {
            ReportSealedRoute(state, npc, intent, targetRoomId);
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
            FailIntent(state, npc, "I need a specific consenting partner.");
            return;
        }

        var partner = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.Id != npc.Id
            && other.Name.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));

        if (partner is null)
        {
            FailIntent(state, npc, $"I cannot find {intent.TargetId}.");
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
            FailIntent(state, npc, "Private time no longer feels mutually right.");
        }
    }

    private void MoveTowardRoom(
        GameState state,
        Npc npc,
        NpcIntent intent,
        string targetRoomId)
    {
        npc.PlannedDestinationRoomId = targetRoomId;
        var path = _navigation.FindPathForCrew(
            state,
            npc,
            npc.CurrentRoomId,
            targetRoomId);

        if (path.Count < 2)
        {
            ReportSealedRoute(state, npc, intent, targetRoomId);
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
            FailIntent(state, npc, "There is no matching shutdown-team invitation.");
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

    private void ExecuteAcceptPactIntent(
        GameState state,
        Npc npc,
        NpcIntent intent)
    {
        var proposal = npc.PendingPactProposal;
        if (proposal is null
            || string.IsNullOrWhiteSpace(intent.TargetId)
            || !proposal.FromNpcName.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            FailIntent(state, npc, "There is no matching pact proposal to accept.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.AcceptPact,
                proposal.FromNpcName,
                intent.Reason),
            out _);

        npc.Intent = null;
    }

    private void ExecuteSettlePactIntent(GameState state, Npc npc, NpcIntent intent)
    {
        var pact = string.IsNullOrWhiteSpace(intent.TargetId)
            ? null
            : CrewPactSystem.ActiveFor(state, npc.Id).FirstOrDefault(candidate =>
                candidate.Id.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase)
                && candidate.PromisorId == npc.Id);

        if (pact is null)
        {
            FailIntent(state, npc, "There is no matching active promise of their own to settle.");
            return;
        }

        _actions.TryApply(
            state,
            npc.Id,
            new NpcAction(
                intent.Action,
                pact.Id,
                intent.Reason),
            out _);

        npc.Intent = null;
    }

    private void ExecutePossessionIntent(GameState state, Npc npc, NpcIntent intent)
    {
        // Owner idea #11 (contraband): HideItem/ReturnItem are no longer
        // ownership-restricted here — a possession you know about (your own,
        // seeded at generation, or one you learned about by holding/witnessing
        // it) is enough to attempt any possession action. ActionResolver's
        // TryHidePossession/TryReturnPossession still authoritatively enforce
        // the real holder/belief rules before anything actually mutates.
        var possession = string.IsNullOrWhiteSpace(intent.TargetId)
            ? null
            : state.Possessions.FirstOrDefault(candidate =>
                candidate.Id.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase)
                && !candidate.IsDestroyed
                && npc.KnownPossessions.ContainsKey(candidate.Id));

        if (possession is null)
        {
            FailIntent(state, npc, "There is no matching possession I know about to act on.");
            return;
        }

        if (!_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(intent.Action, possession.Id, intent.Reason),
                out var message))
        {
            FailIntent(state, npc, message);
            return;
        }

        npc.Intent = null;
    }

    private void ExecuteSocialIntent(GameState state, Npc npc, NpcIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.TargetId))
        {
            FailIntent(state, npc, "I need a specific person for this goal.");
            return;
        }

        var target = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.Name.Equals(intent.TargetId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            FailIntent(state, npc, $"I cannot find {intent.TargetId}.");
            return;
        }

        // Physically sharing a room is direct perception, not omniscience, so
        // the co-located fast path stays ground-truth.
        if (npc.CurrentRoomId.Equals(target.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(intent.Action, target.Name, intent.Reason, intent.SubjectId),
                out _);

            npc.Intent = null;
            return;
        }

        // Not co-located: the actor has no ground truth on the target's live
        // position, only what they last saw or expect from the duty
        // schedule. Route toward that belief instead.
        var believedRoomId = BelievedRoomId(npc, target, state.Elapsed);

        if (npc.CurrentRoomId.Equals(believedRoomId, StringComparison.OrdinalIgnoreCase))
        {
            // Arrived at the last-known/expected room and the target
            // genuinely is not here: the belief was stale, not a pathing
            // problem. Report the miss but keep the intent alive rather than
            // giving up outright — there is no search affordance yet to act
            // on a hard failure, so the actor just keeps their goal and
            // re-routes automatically the moment a fresher sighting updates
            // their belief, mirroring how a sealed route is reported.
            npc.PlannedDestinationRoomId = null;
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                target.Name,
                $"{target.Name} is not here. I last knew them to be around {RoomLabel(state, believedRoomId)}.");
            return;
        }

        npc.PlannedDestinationRoomId = believedRoomId;
        var path = _navigation.FindPathForCrew(
            state,
            npc,
            npc.CurrentRoomId,
            believedRoomId);

        if (path.Count < 2)
        {
            npc.PlannedDestinationRoomId = null;
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

    /// <summary>
    /// Where <paramref name="npc"/> believes <paramref name="target"/> to be:
    /// their own last direct sighting, or the target's public duty-schedule
    /// room when they have never crossed paths. Every <see cref="CrewRole"/>,
    /// including <see cref="CrewRole.Prisoner"/>, has a duty route, so this
    /// always resolves to a concrete room.
    /// </summary>
    private static string BelievedRoomId(Npc npc, Npc target, TimeSpan elapsed) =>
        npc.LastSeenCrew.TryGetValue(target.Id, out var sighting)
            ? sighting.RoomId
            : CrewDutySchedule.ExpectedDutyRoomId(target.Role, elapsed);

    private static string RoomLabel(GameState state, string roomId) =>
        state.Facility.Rooms.TryGetValue(roomId, out var room) ? room.Name : roomId;

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

    /// <summary>
    /// A crew member's known route to <paramref name="targetRoomId"/> is fully
    /// sealed. When a still-locked Overseer hatch is the identifiable cause,
    /// name it specifically and let the crew member witness their own denial
    /// of access as suspicion evidence, rather than a generic dead end.
    /// </summary>
    private void ReportSealedRoute(
        GameState state,
        Npc npc,
        NpcIntent intent,
        string targetRoomId)
    {
        npc.PlannedDestinationRoomId = null;

        var sealedRoom = FindOverseerSealedRoom(state, npc.CurrentRoomId, targetRoomId);
        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            targetRoomId,
            sealedRoom is not null
                ? $"I want to: {intent.Goal}, but Overseer sealed {sealedRoom.Name}."
                : $"I want to: {intent.Goal}, but every known route is sealed.");

        if (sealedRoom is null)
        {
            return;
        }

        SuspicionSystem.AddEvidence(
            state,
            npc,
            $"I was blocked from reaching {sealedRoom.Name} because Overseer sealed the way in.",
            12,
            origin: EvidenceOrigin.DirectObservation,
            locationId: npc.CurrentRoomId,
            evidenceId: $"route-sealed:{npc.Id:N}:{sealedRoom.Id}:{state.Elapsed.Ticks}",
            claim: EvidenceClaim.AccessRestricted);
    }

    /// <summary>
    /// Walks the pure topology route (ignoring live door state) toward
    /// <paramref name="toRoomId"/> and returns the room just past the first
    /// still-locked Overseer hatch blocking it, or null if the block cannot
    /// be attributed to an Overseer lock.
    /// </summary>
    private Room? FindOverseerSealedRoom(GameState state, string fromRoomId, string toRoomId)
    {
        var topologyPath = _navigation.FindPathIgnoringDoorState(state.Facility, fromRoomId, toRoomId);

        for (var i = 0; i < topologyPath.Count - 1; i++)
        {
            var door = state.Facility.FindDoorBetween(topologyPath[i], topologyPath[i + 1]);

            if (door is { LockedByOverseer: true, IsPassable: false }
                && state.Facility.Rooms.TryGetValue(topologyPath[i + 1], out var room))
            {
                return room;
            }
        }

        return null;
    }

    private static void FailIntent(GameState state, Npc npc, string reason)
    {
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
        npc.Intent = null;
        npc.Plan = null;
        npc.PlannedDestinationRoomId = null;
        npc.Memories.Add(new Memory(
            reason,
            state.Elapsed,
            FailedIntentMemoryImportance,
            IsFailedAttempt: true));
        StatLogSystem.Set(state, npc, CrewStat.Stress, Math.Clamp(npc.Stress + FailedIntentStressCost, 0, 100), "a plan fell through");
    }

    private const double FailedIntentMemoryImportance = 0.35;
    private const double FailedIntentStressCost = 3;
}
