using Overseer.Domain;

namespace Overseer.Simulation;

public sealed record CrewAffordanceDefinition(
    ActionKind Action,
    string TargetType,
    string Description);

/// <summary>
/// Shared V0.10E vocabulary exposed to both cognition implementations. This is
/// deliberately capability-oriented: minds choose goals from these affordances;
/// execution systems still validate the world, knowledge, permissions and skill.
/// </summary>
public static class CrewAffordanceSystem
{
    public static readonly IReadOnlyList<CrewAffordanceDefinition> Catalog =
    [
        new(ActionKind.Idle, "none", "Wait, observe, or reconsider."),
        new(ActionKind.Move, "room", "Travel to a known room."),
        new(ActionKind.SeekSafety, "room", "Move toward a safer compartment when threatened."),
        new(ActionKind.FightFire, "room", "Attempt to suppress an active compartment fire using local emergency equipment."),
        new(ActionKind.EvacuateHazard, "room", "Evacuate toward a specifically chosen safer compartment."),
        new(ActionKind.SealHazardRoom, "room", "Close operable hatches around a hazardous compartment to contain it."),
        new(ActionKind.VentHazardRoom, "room", "Vent a smoky/burning compartment, trading pressure and oxygen for fire/smoke reduction."),
        new(ActionKind.Investigate, "room", "Investigate a room or anomaly."),
        new(ActionKind.VerifyClaim, "room", "Go verify a claim against physical evidence."),
        new(ActionKind.InspectEquipment, "room", "Inspect machinery, consoles or fixtures."),
        new(ActionKind.Work, "room", "Perform ordinary role work in a suitable room."),
        new(ActionKind.Repair, "room", "Attempt ordinary repair work."),
        new(ActionKind.StandGuard, "room", "Hold position and watch a room or access point."),
        new(ActionKind.Eat, "optional-dining-room", "Eat when food is available: in the galley, or carry a meal from the galley to a recreation room, crew quarters or a free medical bedside."),
        new(ActionKind.Rest, "none", "Rest in crew quarters."),
        new(ActionKind.Sleep, "none", "Sleep in crew quarters."),
        new(ActionKind.Recreate, "optional-activity", "Use recreation facilities: watch TV, play games on the console, read, or just unwind."),
        new(ActionKind.Groom, "none", "Groom in a washroom."),
        new(ActionKind.Shower, "none", "Shower in a washroom."),
        new(ActionKind.UseToilet, "none", "Use a washroom."),
        new(ActionKind.Talk, "crew", "Talk to another crew member."),
        new(ActionKind.Socialize, "crew", "Spend social time with another crew member."),
        new(ActionKind.Argue, "crew", "Confront another crew member verbally."),
        new(ActionKind.CheckOnCrew, "crew", "Find someone and check on their wellbeing."),
        new(ActionKind.AssistCrew, "crew", "Go to someone and help with what they are doing."),
        new(ActionKind.AskAboutLocation, "crew", "Ask a specific person whether they have seen the subject of one of your MISSING-PERSON CONCERNS."),
        new(ActionKind.CoordinateWork, "crew", "Coordinate a task or plan with another person."),
        new(ActionKind.ReassureCrew, "crew", "Try to calm or reassure another person."),
        new(ActionKind.MisleadCrew, "crew", "Attempt to misdirect another person; no belief changes without deterministic evidence rules."),
        new(ActionKind.ReportConcern, "crew", "Share a concern or observation with another person."),
        new(ActionKind.RequestHelp, "crew", "Ask a specific person for help."),
        new(ActionKind.ProposePact, "crew", "Propose a personal promise or deal to another crew member (cover a shift, keep quiet, owe a favour, or any other concrete commitment)."),
        new(ActionKind.Suggest, "crew", "Suggest a specific, concrete action to a co-located crew member. They independently decide whether to act on it, weighing how much they trust you; nothing is scripted or forced."),
        new(ActionKind.AcceptPact, "pact-proposal", "Accept a pending pact proposal made to you, turning it into a real commitment."),
        new(ActionKind.FulfillPact, "pact", "Keep an active promise you made, settling it and its trust/relationship consequences."),
        new(ActionKind.BreakPact, "pact", "Break an active promise you made, settling it and its trust/relationship consequences."),
        new(ActionKind.OpenDoor, "adjacent-door", "Open an adjacent unlocked powered hatch."),
        new(ActionKind.CloseDoor, "adjacent-door", "Close an adjacent unlocked powered hatch."),
        new(ActionKind.LockDoor, "adjacent-door", "Lock an adjacent hatch if authorised."),
        new(ActionKind.UnlockDoor, "adjacent-door", "Unlock an adjacent hatch if authorised."),
        new(ActionKind.HideItem, "possession", "Hide a possession you currently hold — your own, or one you previously borrowed or stole — somewhere in your current room."),
        new(ActionKind.ReturnItem, "possession", "Retrieve a possession you know is hidden in your current room (your own, or a stash you or someone else hid) and take it back into your hands."),
        new(ActionKind.BorrowItem, "possession", "Ask a co-located crew member to lend you a possession you know about that they are currently holding; they may refuse."),
        new(ActionKind.StealItem, "possession", "Take a possession you know about without asking — from a co-located crew member currently holding it, or from a hiding spot you know about in your current room."),
        new(ActionKind.DestroyItem, "possession", "Destroy a possession — one you currently hold (your own, or one you previously borrowed/stole), one a co-located crew member currently holds, or one hidden in your current room you know about."),
        new(ActionKind.ForceDoor, "adjacent-door", "Defeat a blocked hatch by force or technical bypass."),
        new(ActionKind.DisconnectDevice, "local-device", "Physically disconnect a non-door station device in your current room. This only changes the machine; why you want to do it is your decision."),
        new(ActionKind.RestoreSystem, "system", "Restore a disabled station system."),
        new(ActionKind.SecureAirlock, "airlock", "Secure an unsafe exterior airlock."),
        new(ActionKind.RepairDoor, "adjacent-door", "Repair a damaged or bypassed hatch."),
        new(ActionKind.WeldDoor, "adjacent-door", "Weld a suitable hatch shut."),
        new(ActionKind.BarricadeDoor, "adjacent-door", "Barricade a suitable hatch."),
        new(ActionKind.RecruitShutdownAlly, "crew", "Recruit a known person into a justified shutdown plan."),
        new(ActionKind.JoinShutdownTeam, "team", "Accept a known shutdown-team invitation."),
        new(ActionKind.ShutdownOverseer, "shutdown-control", "Operate a personally verified shutdown mechanism."),
        new(ActionKind.ShutdownRobot, "robot", "Attempt a local shutdown of a threatening robot."),
        new(ActionKind.IsolateRobotNetwork, "robot", "Isolate a threatening robot from Engineering."),
        new(ActionKind.DisableRobotCharging, "robot", "Disable a threatening robot charging circuit."),
        new(ActionKind.DamageRobot, "robot", "Physically disable a threatening robot."),
        new(ActionKind.ReprogramRobot, "robot", "Reprogram a locally disabled hostile robot."),
        new(ActionKind.DisarmTurret, "turret", "Disarm a locally accessible hostile turret."),
        new(ActionKind.IsolateTurretNetwork, "turret", "Isolate a hostile turret network from Engineering."),
        new(ActionKind.DisableTurretPower, "turret", "Disable a hostile turret power feed."),
        new(ActionKind.DamageTurret, "turret", "Physically disable a hostile turret."),
        new(ActionKind.ReprogramTurret, "turret", "Reprogram a locally safe hostile turret."),
        new(ActionKind.IsolateSecurityController, "security-controller", "Physically isolate a diagnosed compromised MR/ST controller."),
        new(ActionKind.PurgeSecurityController, "security-controller", "Purge and reimage an isolated compromised MR/ST controller."),
        new(ActionKind.RecapturePrisoner, "prisoner", "Physically restrain an escaped prisoner and return them to containment."),
        new(ActionKind.AssumeRole, "vacant-post", "Step into a post whose holder you know has died, taking on its duties from now on. Whether you should, and what the others make of it, is up to you.")
    ];

    public static bool IsCognitionAction(ActionKind action) =>
        Catalog.Any(entry => entry.Action == action);

    public static bool IsRoomTarget(ActionKind action) =>
        action is ActionKind.Move
            or ActionKind.SeekSafety
            or ActionKind.FightFire
            or ActionKind.EvacuateHazard
            or ActionKind.SealHazardRoom
            or ActionKind.VentHazardRoom
            or ActionKind.Investigate
            or ActionKind.VerifyClaim
            or ActionKind.InspectEquipment
            or ActionKind.Work
            or ActionKind.Repair
            or ActionKind.StandGuard;

    public static bool IsCrewTarget(ActionKind action) =>
        action is ActionKind.Talk
            or ActionKind.Socialize
            or ActionKind.Argue
            or ActionKind.CheckOnCrew
            or ActionKind.AssistCrew
            or ActionKind.CoordinateWork
            or ActionKind.ReassureCrew
            or ActionKind.MisleadCrew
            or ActionKind.ReportConcern
            or ActionKind.RequestHelp
            or ActionKind.ProposePact
            or ActionKind.Suggest
            or ActionKind.RecruitShutdownAlly
            or ActionKind.AskAboutLocation;

    public static bool IsDoorOperation(ActionKind action) =>
        action is ActionKind.OpenDoor
            or ActionKind.CloseDoor
            or ActionKind.LockDoor
            or ActionKind.UnlockDoor;

    public static string PromptCatalog() =>
        string.Join(
            Environment.NewLine,
            Catalog.Select(entry =>
                $"- {entry.Action} [{entry.TargetType}]: {entry.Description}"));

    public static bool TryNormalizeTarget(
        GameState state,
        Npc npc,
        ActionKind action,
        string? requestedTarget,
        out string? normalizedTarget)
    {
        var requested = requestedTarget?.Trim();
        normalizedTarget = requested;

        if (IsRoomTarget(action))
        {
            if (requested is null
                || !state.Facility.Rooms.TryGetValue(requested, out var room))
                return false;

            if (action is ActionKind.SeekSafety or ActionKind.EvacuateHazard
                && CrewEnvironmentSafety.RiskScore(room)
                    >= CrewEnvironmentSafety.RiskScore(
                        state.Facility.Rooms[npc.CurrentRoomId]))
                return false;

            if (action == ActionKind.FightFire && room.FireIntensity <= 0)
                return false;

            if (action is ActionKind.SealHazardRoom or ActionKind.VentHazardRoom
                && room.FireIntensity <= 0 && room.SmokePercent < 8)
                return false;

            normalizedTarget = room.Id;
            return true;
        }

        if (IsCrewTarget(action))
        {
            var person = state.Crew.FirstOrDefault(other =>
                other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && other.Name.Equals(
                    requested,
                    StringComparison.OrdinalIgnoreCase));

            if (person is null)
                return false;

            if (action == ActionKind.RecruitShutdownAlly
                && (npc.OverseerSuspicion < 65
                    || npc.KnownShutdownMechanismIds.Count == 0))
                return false;

            // Asking requires an actual concern to ask about, and asking the
            // very person you are worried about makes no sense while they are
            // not the one physically in front of you (co-location resolves
            // the concern outright elsewhere).
            if (action == ActionKind.AskAboutLocation
                && (npc.MissingPersonConcerns.Count == 0
                    || npc.MissingPersonConcerns.ContainsKey(person.Id)))
                return false;

            normalizedTarget = person.Name;
            return true;
        }

        if (action == ActionKind.RecapturePrisoner)
        {
            var prisoner = state.Crew.FirstOrDefault(other =>
                other.IsPrisoner
                && other.IsAlive
                && other.IsPresent
                && other.HasEscapedContainment
                && other.Id != npc.Id
                && other.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));

            if (prisoner is null)
                return false;

            normalizedTarget = prisoner.Name;
            return true;
        }

        if (action is ActionKind.HideItem or ActionKind.ReturnItem)
        {
            // HideItem needs you to physically hold the item (own, borrowed or
            // stolen). ReturnItem needs it hidden in your current room AND your
            // own belief to place it there — owners included, since an owner
            // only knows where their item is from what they last saw.
            var possession = state.Possessions.FirstOrDefault(candidate =>
                !candidate.IsDestroyed
                && candidate.Id.Equals(requested, StringComparison.OrdinalIgnoreCase));

            if (possession is null)
                return false;

            normalizedTarget = possession.Id;
            return action switch
            {
                ActionKind.HideItem => possession.CurrentHolderId == npc.Id,
                ActionKind.ReturnItem => possession.HiddenAtRoomId is not null
                    && possession.HiddenAtRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                    && npc.KnownPossessions.TryGetValue(possession.Id, out var belief)
                    && belief.HiddenAtRoomId is not null
                    && belief.HiddenAtRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        if (action is ActionKind.BorrowItem or ActionKind.StealItem or ActionKind.DestroyItem)
        {
            // Unlike HideItem/ReturnItem, the possession need not be yours —
            // it just has to be one you actually know about (ambient
            // co-located noticing, or having witnessed someone else's
            // hide/borrow/steal act). Never a blind, omniscient lookup.
            if (string.IsNullOrWhiteSpace(requested) || !npc.KnownPossessions.TryGetValue(requested, out var belief))
                return false;

            var possession = state.Possessions.FirstOrDefault(candidate =>
                !candidate.IsDestroyed
                && candidate.Id.Equals(requested, StringComparison.OrdinalIgnoreCase));

            if (possession is null)
                return false;

            normalizedTarget = possession.Id;

            // Unlike Borrow/Steal, DestroyItem may target something you
            // already hold yourself (your own, or one you previously
            // borrowed/stole).
            if (action == ActionKind.DestroyItem && possession.CurrentHolderId == npc.Id)
                return true;

            if (possession.CurrentHolderId is { } holderId && holderId != npc.Id)
            {
                return state.Crew.Any(other =>
                    other.Id == holderId
                    && other.IsAlive
                    && other.IsPresent
                    && other.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));
            }

            // A hiding spot has no one to ask, so only StealItem/DestroyItem
            // can target one — and only a spot this actor themself believes
            // is here (witnessed the hide), never a live coincidence they
            // never actually learned about.
            return action is ActionKind.StealItem or ActionKind.DestroyItem
                && belief.HiddenAtRoomId is not null
                && belief.HiddenAtRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                && possession.HiddenAtRoomId is not null
                && possession.HiddenAtRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase);
        }

        if (action == ActionKind.AssumeRole)
        {
            if (!RoleSuccessionRules.TryParseRole(requested, out var role)
                || !RoleSuccessionRules.CanAssume(state, npc, role, out _))
            {
                return false;
            }

            normalizedTarget = role.ToString();
            return true;
        }

        if (action == ActionKind.DisconnectDevice)
        {
            if (string.IsNullOrWhiteSpace(requested)
                || !state.Devices.TryGetValue(requested, out var device)
                || device.Kind == StationSystemKind.Door
                || !device.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                || !device.IsEnabled
                || device.IsFailed
                || !state.Facility.Rooms.TryGetValue(device.RoomId, out var deviceRoom)
                || LocalMovementSystem.FixtureForDevice(deviceRoom, device.Kind) is null)
            {
                return false;
            }

            normalizedTarget = device.Id;
            return true;
        }

        if (IsDoorOperation(action))
        {
            var door = state.Facility.Doors.FirstOrDefault(candidate =>
                candidate.Id.Equals(
                    requested,
                    StringComparison.OrdinalIgnoreCase)
                && CrewDoorInteractionSystem.IsAdjacent(npc, candidate));

            if (door is null)
                return false;

            normalizedTarget = door.Id;
            return action switch
            {
                ActionKind.OpenDoor => !door.IsOpen
                    && CrewDoorInteractionSystem.CanOpen(state, npc, door),
                ActionKind.CloseDoor => CrewDoorInteractionSystem.CanClose(npc, door),
                ActionKind.LockDoor => !door.IsLocked
                    && CrewDoorInteractionSystem.CanLockOrUnlock(npc, door),
                ActionKind.UnlockDoor => door.IsLocked
                    && CrewDoorInteractionSystem.CanLockOrUnlock(npc, door),
                _ => false
            };
        }

        return true;
    }
}

/// <summary>
/// Authoritative human hatch interaction. Closed unlocked powered doors are
/// traversable affordances for crew, but are not magically passable: a person
/// reaches the portal, opens it, crosses, and the hatch closes after traffic.
/// </summary>
public sealed class CrewDoorInteractionSystem
{
    public void Tick(GameState state)
    {
        foreach (var door in state.Facility.Doors.Where(door =>
                     door.CrewAutoCloseAt is not null))
        {
            if (door.CrewAutoCloseAt > state.Elapsed)
                continue;

            var traffic = state.Crew.Any(npc =>
                    npc.IsAlive
                    && npc.Movement?.DoorId.Equals(
                        door.Id,
                        StringComparison.OrdinalIgnoreCase) == true)
                || state.Robots.Any(robot =>
                    !robot.IsDestroyed
                    && robot.Movement?.DoorId.Equals(
                        door.Id,
                        StringComparison.OrdinalIgnoreCase) == true);

            if (traffic)
            {
                door.CrewAutoCloseAt =
                    state.Elapsed + TimeSpan.FromMinutes(1);
                continue;
            }

            door.CrewAutoCloseAt = null;

            if (!door.IsOpen
                || door.IsLocked
                || door.IsManuallyOverridden
                || door.HasPhysicalSecuring
                || !door.IsPowered)
                continue;

            door.IsOpen = false;
            Log(state, $"{door.Id} slides closed after crew traffic clears.");
        }
    }

    public static bool IsAdjacent(Npc npc, Door door) =>
        npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
        || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

    public static bool HasLockAuthority(Npc npc)
    {
        if (npc.Role is CrewRole.Engineer or CrewRole.Technician)
            return true;

        if (npc.Role == CrewRole.Security
            && Skill(npc, "Security") >= 55)
            return true;

        return npc.Role == CrewRole.Commander
            && Skill(npc, "Operations") >= 70;
    }

    public static bool CanLockOrUnlock(Npc npc, Door door) =>
        IsAdjacent(npc, door)
        && HasLockAuthority(npc)
        && door.IsPowered
        && !door.IsManuallyOverridden
        && !door.HasPhysicalSecuring;

    public static bool CanClose(Npc npc, Door door) =>
        IsAdjacent(npc, door)
        && door.IsPowered
        && door.IsOpen
        && !door.IsLocked
        && !door.IsManuallyOverridden
        && !door.HasPhysicalSecuring;

    public static bool CanOpen(
        GameState state,
        Npc npc,
        Door door) =>
        IsAdjacent(npc, door)
        && CanOpenOnceBeside(state, npc, door);

    public static bool CanOpenForTraversal(
        GameState state,
        Npc npc,
        Door door) =>
        door.IsPassable
        || (!door.IsOpen && CanOpen(state, npc, door));

    /// <summary>
    /// Route-planning view of a hatch: could this person get through it once
    /// they physically reach it? Unlike <see cref="CanOpenForTraversal"/> this
    /// does not require them to be standing beside it already, so a closed but
    /// ordinary hatch several rooms away is not mistaken for a wall. The
    /// crossing itself still revalidates the live door state at the portal.
    /// </summary>
    public static bool CanTraverseWhenReached(
        GameState state,
        Npc npc,
        Door door) =>
        door.IsPassable
        || (!door.IsOpen && CanOpenOnceBeside(state, npc, door));

    private static bool CanOpenOnceBeside(
        GameState state,
        Npc npc,
        Door door)
    {
        if (!npc.IsAlive
            || !npc.IsPresent
            || !door.IsPowered
            || door.IsLocked
            || door.HasPhysicalSecuring
            || door.IsManuallyOverridden)
            return false;

        return new AirlockSafetySystem().CanToggleInnerHatch(
            state,
            door,
            opening: true,
            out _);
    }

    public bool TryOpenForTraversal(
        GameState state,
        Npc npc,
        Door door,
        out string message)
    {
        if (door.IsPassable)
        {
            message = $"{door.Id} is already passable.";
            return true;
        }

        if (!CanOpen(state, npc, door))
        {
            message = $"{npc.Name} cannot open {door.Id}.";
            return false;
        }

        door.IsOpen = true;
        door.LastCrewOperatorId = npc.Id;
        door.CrewAutoCloseAt =
            state.Elapsed + TimeSpan.FromMinutes(1);

        message = $"{npc.Name} opens {door.Id} to pass through.";
        Log(state, message);
        return true;
    }

    public bool TryOperate(
        GameState state,
        Npc npc,
        Door door,
        ActionKind action,
        out string message)
    {
        if (!IsAdjacent(npc, door))
        {
            message = $"{npc.Name} must be beside {door.Id}.";
            return false;
        }

        switch (action)
        {
            case ActionKind.OpenDoor:
                if (door.IsOpen)
                {
                    message = $"{door.Id} is already open.";
                    return false;
                }
                if (!CanOpen(state, npc, door))
                {
                    message = $"{npc.Name} cannot open {door.Id}.";
                    return false;
                }
                door.IsOpen = true;
                door.LastCrewOperatorId = npc.Id;
                door.CrewAutoCloseAt = null;
                break;

            case ActionKind.CloseDoor:
                if (!CanClose(npc, door))
                {
                    message = $"{npc.Name} cannot close {door.Id}.";
                    return false;
                }
                door.IsOpen = false;
                door.LastCrewOperatorId = npc.Id;
                door.CrewAutoCloseAt = null;
                break;

            case ActionKind.LockDoor:
                if (!CanLockOrUnlock(npc, door) || door.IsLocked)
                {
                    message = $"{npc.Name} is not authorised or able to lock {door.Id}.";
                    return false;
                }
                door.IsOpen = false;
                door.IsLocked = true;
                door.LockedByOverseer = false;
                door.LastCrewOperatorId = npc.Id;
                door.CrewAutoCloseAt = null;
                break;

            case ActionKind.UnlockDoor:
                if (!CanLockOrUnlock(npc, door) || !door.IsLocked)
                {
                    message = $"{npc.Name} is not authorised or able to unlock {door.Id}.";
                    return false;
                }
                door.IsLocked = false;
                door.LockedByOverseer = false;
                door.LastCrewOperatorId = npc.Id;
                break;

            default:
                message = "That is not a normal crew hatch operation.";
                return false;
        }

        message = $"{npc.Name} {DoorVerb(action)} {door.Id}.";
        Log(state, message);
        return true;
    }

    private static int Skill(Npc npc, string name) =>
        npc.Skills.TryGetValue(name, out var value) ? value : 0;

    private static string DoorVerb(ActionKind action) => action switch
    {
        ActionKind.OpenDoor => "opens",
        ActionKind.CloseDoor => "closes",
        ActionKind.LockDoor => "locks",
        ActionKind.UnlockDoor => "unlocks",
        _ => "operates"
    };

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}

public static class StationInspectionSystem
{
    public static bool Exists(GameState state, StationSelection? selection) =>
        selection?.Kind switch
        {
            StationSelectionKind.Room =>
                state.Facility.Rooms.ContainsKey(selection.Id),
            StationSelectionKind.Crew =>
                Guid.TryParse(selection.Id, out var id)
                && state.Crew.Any(npc => npc.Id == id),
            StationSelectionKind.Door =>
                state.Facility.Doors.Any(door =>
                    door.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase)),
            StationSelectionKind.Robot =>
                state.Robots.Any(robot =>
                    robot.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase)),
            StationSelectionKind.Turret =>
                state.Turrets.Any(turret =>
                    turret.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase)),
            StationSelectionKind.Device =>
                state.Devices.ContainsKey(selection.Id),
            StationSelectionKind.CropBed =>
                state.CropBeds.Any(bed =>
                    bed.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

    public static Room? Room(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.Room }
        && state.Facility.Rooms.TryGetValue(selection.Id, out var room)
            ? room
            : null;

    public static Npc? Crew(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.Crew }
        && Guid.TryParse(selection.Id, out var id)
            ? state.Crew.FirstOrDefault(npc => npc.Id == id)
            : null;

    public static Door? Door(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.Door }
            ? state.Facility.Doors.FirstOrDefault(door =>
                door.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase))
            : null;

    public static StationRobot? Robot(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.Robot }
            ? state.Robots.FirstOrDefault(robot =>
                robot.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase))
            : null;

    public static SecurityTurret? Turret(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.Turret }
            ? state.Turrets.FirstOrDefault(turret =>
                turret.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase))
            : null;

    public static StationDevice? Device(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.Device }
        && state.Devices.TryGetValue(selection.Id, out var device)
            ? device
            : null;

    public static CropBed? CropBed(GameState state, StationSelection? selection) =>
        selection is { Kind: StationSelectionKind.CropBed }
            ? state.CropBeds.FirstOrDefault(bed =>
                bed.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase))
            : null;
}

public sealed record DebugTelemetrySnapshot(
    IReadOnlyList<CognitionTelemetryEntry> Cognition,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> GenerationDiagnostics);

/// <summary>
/// Debug telemetry is explicitly non-gameplay state. It is exposed on a
/// separate screen and can be disabled for release without changing simulation.
/// </summary>
public static class DebugTelemetrySystem
{
    public static bool UiEnabled => true;
    public const bool IsGameplayCritical = false;

    public static DebugTelemetrySnapshot Capture(GameState state) =>
        new(
            state.CognitionTelemetry.ToArray(),
            state.EventLog.ToArray(),
            state.StationGeneration?.Diagnostics.ToArray() ?? []);
}
