using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class ActionResolver
{
    public bool TryApply(
        GameState state,
        Guid npcId,
        NpcAction action,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        var npc = state.Crew.FirstOrDefault(candidate => candidate.Id == npcId);

        if (npc is null)
        {
            message = "Crew member not found.";
            return false;
        }

        if (!npc.IsAlive)
        {
            message = $"{npc.Name} cannot act because they are deceased.";
            return false;
        }

        return action.Kind switch
        {
            ActionKind.Move => TryMove(state, npc, action, out message),
            ActionKind.Eat => TryInRoomType(
                state, npc, action, RoomType.Kitchen, "eat", "starts eating", out message),
            ActionKind.Rest => TryInRoomType(
                state, npc, action, RoomType.CrewQuarters, "rest", "starts resting", out message),
            ActionKind.Sleep => TryInRoomType(
                state, npc, action, RoomType.CrewQuarters, "sleep", "settles down to sleep", out message),
            ActionKind.Recreate => TryInRoomType(
                state, npc, action, RoomType.Recreation, "relax", "starts relaxing", out message),
            ActionKind.Groom => TryInRoomType(
                state, npc, action, RoomType.Washroom, "groom", "starts grooming", out message),
            ActionKind.Shower => TryInRoomType(
                state, npc, action, RoomType.Washroom, "shower", "takes a shower", out message),
            ActionKind.UseToilet => TryInRoomType(
                state, npc, action, RoomType.Washroom, "use the toilet", "uses the toilet", out message),
            ActionKind.Work => SetAction(state, npc, action, "gets on with their work", out message),
            ActionKind.Intimacy => TryIntimacy(state, npc, action, out message),
            ActionKind.Investigate => SetAction(state, npc, action, "starts investigating", out message),
            ActionKind.Repair => SetAction(state, npc, action, "starts a repair attempt", out message),
            ActionKind.Talk => TrySocialAction(state, npc, action, "starts a conversation", out message),
            ActionKind.Socialize => TrySocialAction(state, npc, action, "socialises", out message),
            ActionKind.Argue => TrySocialAction(state, npc, action, "argues", out message),
            ActionKind.Attack => SetAction(state, npc, action, "attacks", out message),
            ActionKind.RequestHelp => TrySocialAction(state, npc, action, "requests help", out message),
            ActionKind.ShutdownOverseer => TryShutdown(state, npc, action, out message),
            ActionKind.OverrideDoor => TryOverrideDoor(state, npc, action, out message),
            ActionKind.ForceDoor => TryForceDoor(state, npc, action, out message),
            ActionKind.RestoreSystem => TryRestoreSystem(state, npc, action, out message),
            ActionKind.SecureAirlock => TrySecureAirlock(state, npc, action, out message),
            ActionKind.Idle => SetAction(state, npc, action, "waits", out message),
            _ => Fail("Unsupported action.", out message)
        };
    }

    private static bool TryMove(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(action.TargetId)
            || !state.Facility.Rooms.TryGetValue(action.TargetId, out var targetRoom))
        {
            message = "Target room does not exist.";
            return false;
        }

        if (npc.Movement is { } existingMovement)
        {
            if (existingMovement.FromRoomId.Equals(
                    npc.CurrentRoomId,
                    StringComparison.OrdinalIgnoreCase)
                && existingMovement.ToRoomId.Equals(
                    targetRoom.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                npc.CurrentAction = action;
                message =
                    $"{npc.Name} is already moving toward {targetRoom.Name}.";
                return true;
            }

            message =
                $"{npc.Name} is already committed to crossing {existingMovement.DoorId}.";
            return false;
        }

        if (npc.CurrentRoomId.Equals(targetRoom.Id, StringComparison.OrdinalIgnoreCase))
        {
            message = $"{npc.Name} is already in {targetRoom.Name}.";
            return false;
        }

        var door = state.Facility.FindDoorBetween(npc.CurrentRoomId, targetRoom.Id);

        if (door is null)
        {
            message = $"{npc.Name} cannot reach {targetRoom.Name} directly.";
            return false;
        }

        if (!door.IsPassable)
        {
            message = $"{npc.Name} is blocked by {door.Id}.";
            return false;
        }

        var fromRoom = state.Facility.Rooms[npc.CurrentRoomId];

        npc.Movement = MovementGeometry.CreateOrder(
            door,
            fromRoom,
            targetRoom);
        npc.CurrentAction = action;

        message =
            $"{npc.Name} heads for {door.Id} en route to {targetRoom.Name}.";
        Log(state, message);
        return true;
    }

    private static bool TryInRoomType(
        GameState state,
        Npc npc,
        NpcAction action,
        RoomType requiredRoomType,
        string verb,
        string description,
        out string message)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        if (room.Type != requiredRoomType)
        {
            message = $"{npc.Name} needs an appropriate room to {verb}.";
            return false;
        }

        return SetAction(state, npc, action, description, out message);
    }

    private static bool TrySocialAction(
        GameState state,
        Npc npc,
        NpcAction action,
        string description,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(action.TargetId))
        {
            message = $"{npc.Name} needs a specific person for this interaction.";
            return false;
        }

        var target = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.Id != npc.Id
            && other.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
            && other.Name.Equals(action.TargetId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            message = $"{action.TargetId} is not here anymore.";
            return false;
        }

        return SetAction(state, npc, action, description, out message);
    }

    private static bool TryIntimacy(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        if (room.Type != RoomType.CrewQuarters
            || string.IsNullOrWhiteSpace(action.TargetId))
        {
            message = $"{npc.Name} needs privacy and a consenting partner.";
            return false;
        }

        var partner = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.Id != npc.Id
            && other.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
            && other.Name.Equals(action.TargetId, StringComparison.OrdinalIgnoreCase));

        if (partner is null
            || !npc.Relationships.TryGetValue(partner.Name, out var towardPartner)
            || !partner.Relationships.TryGetValue(npc.Name, out var towardNpc)
            || npc.IntimacyNeed < 55
            || partner.IntimacyNeed < 55
            || towardPartner.Trust < 60
            || towardPartner.Affinity < 65
            || towardPartner.Attraction < 55
            || towardPartner.Resentment >= 25
            || towardNpc.Trust < 60
            || towardNpc.Affinity < 65
            || towardNpc.Attraction < 55
            || towardNpc.Resentment >= 25)
        {
            message = $"{npc.Name} and {action.TargetId} do not mutually want intimacy right now.";
            return false;
        }

        npc.CurrentAction = action;
        message = $"{npc.Name} spends private time with {partner.Name}.";
        Log(state, message);
        return true;
    }

    private static bool TryOverrideDoor(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        var door = state.Facility.Doors.FirstOrDefault(d =>
            d.Id.Equals(action.TargetId, StringComparison.OrdinalIgnoreCase));

        if (door is null)
        {
            message = "Manual override target door does not exist.";
            return false;
        }

        var adjacent = npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
            || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

        if (!adjacent)
        {
            message = $"{npc.Name} must physically reach {door.Id} to override it.";
            return false;
        }

        if (!door.ManualOverrideAvailable)
        {
            message = $"{door.Id} has no accessible manual override.";
            return false;
        }

        if (ManualOverrideSystem.BestOverrideSkill(npc) < door.ManualOverrideSkillRequired)
        {
            message = $"{npc.Name} lacks the skill to force {door.Id}.";
            return false;
        }

        if (door.IsPassable)
        {
            message = $"{door.Id} is already passable.";
            return false;
        }

        if (npc.CurrentAction.Kind == ActionKind.OverrideDoor
            && npc.CurrentAction.TargetId == door.Id)
        {
            message = $"{npc.Name} continues overriding {door.Id}.";
            return true;
        }

        npc.RoutineUntil = TimeSpan.Zero;
        return SetAction(
            state,
            npc,
            action,
            $"starts forcing the manual controls on {door.Id}",
            out message);
    }

    private static bool TryForceDoor(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        var door = state.Facility.Doors.FirstOrDefault(candidate =>
            candidate.Id.Equals(action.TargetId, StringComparison.OrdinalIgnoreCase));

        if (door is null)
        {
            message = "Force-door target does not exist.";
            return false;
        }

        var adjacent =
            npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
            || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

        if (!adjacent)
        {
            message = $"{npc.Name} must physically reach {door.Id} before trying to open it.";
            return false;
        }

        if (!door.CanBeForced)
        {
            message = $"{door.Id} cannot be defeated from this side.";
            return false;
        }

        if (door.IsPassable)
        {
            message = $"{door.Id} is already passable.";
            return false;
        }

        if (npc.CurrentAction.Kind == ActionKind.ForceDoor
            && npc.CurrentAction.TargetId == door.Id)
        {
            message = $"{npc.Name} continues trying to open {door.Id}.";
            return true;
        }

        npc.RoutineUntil = TimeSpan.Zero;
        return SetAction(
            state,
            npc,
            action,
            $"starts trying to defeat {door.Id}",
            out message);
    }

    private static bool TryRestoreSystem(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(action.TargetId)
            || !CrewCounterplaySystem.HasRestorableProblem(state, action.TargetId))
        {
            message = "That system does not currently need restoration.";
            return false;
        }

        var requiredRoom = CrewCounterplaySystem.RequiredRoomForRestore(
            state,
            action.TargetId);

        if (requiredRoom is null
            || !npc.CurrentRoomId.Equals(
                requiredRoom,
                StringComparison.OrdinalIgnoreCase))
        {
            message = $"{npc.Name} must physically reach the relevant controls first.";
            return false;
        }

        if (npc.CurrentAction.Kind == ActionKind.RestoreSystem
            && npc.CurrentAction.TargetId == action.TargetId)
        {
            message = $"{npc.Name} continues working on {action.TargetId}.";
            return true;
        }

        npc.RoutineUntil = TimeSpan.Zero;
        return SetAction(
            state,
            npc,
            action,
            $"starts restoring {action.TargetId}",
            out message);
    }

    private static bool TrySecureAirlock(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(action.TargetId)
            || !state.Facility.Rooms.TryGetValue(action.TargetId, out var airlock)
            || airlock.Type != RoomType.Airlock
            || !airlock.HasExteriorHatch)
        {
            message = "Secure-airlock target does not exist.";
            return false;
        }

        if (!AirlockSafetySystem.NeedsCrewSecuring(state, airlock))
        {
            message = $"{airlock.Name} no longer needs emergency securing.";
            return false;
        }

        if (!AirlockSafetySystem.CanCrewSecure(npc))
        {
            message = $"{npc.Name} lacks the training to use the emergency airlock controls.";
            return false;
        }

        if (!AirlockSafetySystem.IsAtCrewControls(state, npc, airlock))
        {
            message = $"{npc.Name} must physically reach the airlock emergency controls.";
            return false;
        }

        if (npc.CurrentAction.Kind == ActionKind.SecureAirlock
            && npc.CurrentAction.TargetId == airlock.Id)
        {
            message = $"{npc.Name} continues securing {airlock.Name}.";
            return true;
        }

        npc.RoutineUntil = TimeSpan.Zero;
        return SetAction(
            state,
            npc,
            action,
            $"starts operating the emergency controls for {airlock.Name}",
            out message);
    }

    private static bool TryShutdown(GameState state, Npc npc, NpcAction action, out string message)
    {
        var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
            m.IsOnline && m.Id.Equals(action.TargetId, StringComparison.OrdinalIgnoreCase));
        if (mechanism is null || !npc.KnowsShutdownControl)
        {
            message = $"{npc.Name} does not know a usable shutdown mechanism.";
            return false;
        }
        if (!npc.CurrentRoomId.Equals(mechanism.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            message = $"{npc.Name} must physically reach {mechanism.Label}.";
            return false;
        }
        return SetAction(state, npc, action, $"begins operating {mechanism.Label}", out message);
    }

    private static bool SetAction(
        GameState state,
        Npc npc,
        NpcAction action,
        string description,
        out string message)
    {
        npc.CurrentAction = action;
        message = $"{npc.Name} {description}.";
        Log(state, message);
        return true;
    }

    private static bool Fail(string failure, out string message)
    {
        message = failure;
        return false;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
