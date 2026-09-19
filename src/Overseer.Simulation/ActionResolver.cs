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
            ActionKind.Eat => TryEat(state, npc, action, out message),
            ActionKind.Rest => SetAction(state, npc, action, "starts resting", out message),
            ActionKind.Investigate => SetAction(state, npc, action, "starts investigating", out message),
            ActionKind.Repair => SetAction(state, npc, action, "starts a repair attempt", out message),
            ActionKind.Talk => SetAction(state, npc, action, "starts a conversation", out message),
            ActionKind.Socialize => SetAction(state, npc, action, "socialises", out message),
            ActionKind.Argue => SetAction(state, npc, action, "argues", out message),
            ActionKind.Attack => SetAction(state, npc, action, "attacks", out message),
            ActionKind.RequestHelp => SetAction(state, npc, action, "requests help", out message),
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
        npc.CurrentRoomId = targetRoom.Id;
        npc.CurrentAction = action;

        message =
            $"{npc.Name} crosses {door.Id} from {fromRoom.Name} to {targetRoom.Name}.";
        Log(state, message);
        return true;
    }

    private static bool TryEat(
        GameState state,
        Npc npc,
        NpcAction action,
        out string message)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        if (room.Type != RoomType.Kitchen)
        {
            message = $"{npc.Name} needs to be in the Kitchen to eat.";
            return false;
        }

        npc.Hunger = Math.Clamp(npc.Hunger - 25, 0, 100);
        npc.CurrentAction = action;

        message = $"{npc.Name} eats a meal.";
        Log(state, message);
        return true;
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
