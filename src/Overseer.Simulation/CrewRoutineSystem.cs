using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class CrewRoutineSystem
{
    private readonly ActionResolver _actions = new();
    private readonly NavigationSystem _navigation = new();

    private static readonly IReadOnlyDictionary<CrewRole, string[]> Routes =
        new Dictionary<CrewRole, string[]>
        {
            [CrewRole.Commander] = ["control", "corridor", "kitchen", "quarters"],
            [CrewRole.Engineer] = ["engineering", "reactor", "generator", "control"],
            [CrewRole.Security] = ["corridor", "airlock", "storage", "control"],
            [CrewRole.Doctor] = ["medical", "quarters", "kitchen", "corridor"],
            [CrewRole.Technician] = ["generator", "engineering", "storage", "control"],
            [CrewRole.Scientist] = ["reactor", "engineering", "control", "medical"]
        };

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0)
        {
            return;
        }

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive))
        {
            if ((minute + (int)npc.Role) % 3 != 0)
            {
                continue;
            }

            // Do not casually overwrite high-salience social or emergency actions.
            if (npc.CurrentAction.Kind is ActionKind.Attack or ActionKind.RequestHelp)
            {
                continue;
            }

            var targetRoomId = ChooseTargetRoom(npc, minute);

            if (npc.CurrentRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
            {
                HandleArrival(state, npc, targetRoomId);
                continue;
            }

            var path = _navigation.FindPath(
                state.Facility,
                npc.CurrentRoomId,
                targetRoomId);

            if (path.Count < 2)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    targetRoomId,
                    $"Route to {state.Facility.Rooms[targetRoomId].Name} is sealed.");
                continue;
            }

            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    path[1],
                    $"Heading for {state.Facility.Rooms[targetRoomId].Name}."),
                out _);
        }
    }

    private static string ChooseTargetRoom(Npc npc, int minute)
    {
        if (npc.Hunger >= 55)
        {
            return "kitchen";
        }

        if (npc.Fatigue >= 70)
        {
            return "quarters";
        }

        var route = Routes[npc.Role];
        var phase = ((minute / 3) + (int)npc.Role) % route.Length;
        return route[phase];
    }

    private void HandleArrival(GameState state, Npc npc, string roomId)
    {
        if (roomId == "kitchen" && npc.Hunger >= 55)
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.Eat, roomId, "Taking a scheduled meal."),
                out _);
            return;
        }

        if (roomId == "quarters" && npc.Fatigue >= 70)
        {
            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(ActionKind.Rest, roomId, "Recovering in crew quarters."),
                out _);
            return;
        }

        npc.CurrentAction = new NpcAction(
            ActionKind.Investigate,
            roomId,
            $"Performing routine {npc.Role.ToString().ToLowerInvariant()} duties.");
    }
}
