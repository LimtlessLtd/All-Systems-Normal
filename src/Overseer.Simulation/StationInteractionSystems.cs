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
        new(ActionKind.Investigate, "room", "Investigate a room or anomaly."),
        new(ActionKind.VerifyClaim, "room", "Go verify a claim against physical evidence."),
        new(ActionKind.InspectEquipment, "room", "Inspect machinery, consoles or fixtures."),
        new(ActionKind.Work, "room", "Perform ordinary role work in a suitable room."),
        new(ActionKind.Repair, "room", "Attempt ordinary repair work."),
        new(ActionKind.StandGuard, "room", "Hold position and watch a room or access point."),
        new(ActionKind.Eat, "none", "Eat when food is available."),
        new(ActionKind.Rest, "none", "Rest in crew quarters."),
        new(ActionKind.Sleep, "none", "Sleep in crew quarters."),
        new(ActionKind.Recreate, "none", "Use recreation facilities."),
        new(ActionKind.Groom, "none", "Groom in a washroom."),
        new(ActionKind.Shower, "none", "Shower in a washroom."),
        new(ActionKind.UseToilet, "none", "Use a washroom."),
        new(ActionKind.Talk, "crew", "Talk to another crew member."),
        new(ActionKind.Socialize, "crew", "Spend social time with another crew member."),
        new(ActionKind.Argue, "crew", "Confront another crew member verbally."),
        new(ActionKind.CheckOnCrew, "crew", "Find someone and check on their wellbeing."),
        new(ActionKind.AssistCrew, "crew", "Go to someone and help with what they are doing."),
        new(ActionKind.CoordinateWork, "crew", "Coordinate a task or plan with another person."),
        new(ActionKind.ReassureCrew, "crew", "Try to calm or reassure another person."),
        new(ActionKind.MisleadCrew, "crew", "Attempt to misdirect another person; no belief changes without deterministic evidence rules."),
        new(ActionKind.ReportConcern, "crew", "Share a concern or observation with another person."),
        new(ActionKind.RequestHelp, "crew", "Ask a specific person for help."),
        new(ActionKind.OpenDoor, "adjacent-door", "Open an adjacent unlocked powered hatch."),
        new(ActionKind.CloseDoor, "adjacent-door", "Close an adjacent unlocked powered hatch."),
        new(ActionKind.LockDoor, "adjacent-door", "Lock an adjacent hatch if authorised."),
        new(ActionKind.UnlockDoor, "adjacent-door", "Unlock an adjacent hatch if authorised."),
        new(ActionKind.ForceDoor, "adjacent-door", "Defeat a blocked hatch by force or technical bypass."),
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
        new(ActionKind.PurgeSecurityController, "security-controller", "Purge and reimage an isolated compromised MR/ST controller.")
    ];

    public static bool IsCognitionAction(ActionKind action) =>
        Catalog.Any(entry => entry.Action == action);

    public static bool IsRoomTarget(ActionKind action) =>
        action is ActionKind.Move
            or ActionKind.SeekSafety
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
            or ActionKind.RecruitShutdownAlly;

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

            if (action == ActionKind.SeekSafety
                && CrewEnvironmentSafety.RiskScore(room)
                    >= CrewEnvironmentSafety.RiskScore(
                        state.Facility.Rooms[npc.CurrentRoomId]))
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

            normalizedTarget = person.Name;
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
        Door door)
    {
        if (!IsAdjacent(npc, door)
            || !npc.IsAlive
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

    public static bool CanOpenForTraversal(
        GameState state,
        Npc npc,
        Door door) =>
        door.IsPassable
        || (!door.IsOpen && CanOpen(state, npc, door));

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
