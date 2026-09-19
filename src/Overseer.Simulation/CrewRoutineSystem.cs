using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class CrewRoutineSystem
{
    private readonly ActionResolver _actions = new();
    private readonly NavigationSystem _navigation = new();

    private static readonly IReadOnlyDictionary<CrewRole, string[]> WorkRoutes =
        new Dictionary<CrewRole, string[]>
        {
            [CrewRole.Commander] = ["control", "corridor", "control"],
            [CrewRole.Engineer] = ["engineering", "reactor", "generator"],
            [CrewRole.Security] = ["corridor", "airlock", "storage", "control"],
            [CrewRole.Doctor] = ["medical", "medical", "storage"],
            [CrewRole.Technician] = ["generator", "engineering", "storage"],
            [CrewRole.Scientist] = ["reactor", "medical", "control"]
        };

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0 || minute % 5 != 0)
        {
            return;
        }

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive))
        {
            if (npc.Intent is not null
                || npc.Movement is not null
                || state.Elapsed < npc.RoutineUntil)
            {
                continue;
            }

            if (npc.CurrentAction.Kind is ActionKind.Attack or ActionKind.RequestHelp)
            {
                continue;
            }

            var plan = ChoosePlan(state, npc, minute);

            if (npc.CurrentRoomId.Equals(
                    plan.TargetRoomId,
                    StringComparison.OrdinalIgnoreCase))
            {
                BeginActivity(state, npc, plan);
                continue;
            }

            var path = _navigation.FindPath(
                state.Facility,
                npc.CurrentRoomId,
                plan.TargetRoomId);

            if (path.Count < 2)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    plan.TargetRoomId,
                    $"Route to {state.Facility.Rooms[plan.TargetRoomId].Name} is sealed.");
                SetBubble(
                    npc,
                    "I can't get there.",
                    NpcBubbleKind.Thought,
                    state.Elapsed,
                    3);
                continue;
            }

            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    path[1],
                    plan.TravelReason),
                out _);

            SetBubble(
                npc,
                plan.TravelBubble,
                NpcBubbleKind.Thought,
                state.Elapsed,
                4);
        }
    }

    private static RoutinePlan ChoosePlan(
        GameState state,
        Npc npc,
        int minute)
    {
        if (npc.Hunger >= 55)
        {
            return new(
                "kitchen",
                ActionKind.Eat,
                null,
                "Going to get something to eat.",
                "I need some food.",
                "Finally, food.",
                18);
        }

        if (npc.Fatigue >= 72)
        {
            return new(
                "quarters",
                ActionKind.Sleep,
                null,
                "Going to crew quarters to sleep.",
                "I really need some sleep.",
                "I'm going to sleep.",
                90);
        }

        if (npc.HygieneNeed >= 65)
        {
            return new(
                "washroom",
                ActionKind.Shower,
                null,
                "Going to the washroom for a shower.",
                "I badly need a shower.",
                "A shower will help.",
                18);
        }

        if (npc.HygieneNeed >= 42)
        {
            return new(
                "washroom",
                ActionKind.Groom,
                null,
                "Going to freshen up.",
                "I should tidy myself up.",
                "Much better.",
                10);
        }

        if (npc.RecreationNeed >= 58)
        {
            return new(
                "lounge",
                ActionKind.Recreate,
                null,
                "Heading to the lounge to unwind.",
                "I need a break.",
                "Time to switch off for a bit.",
                30);
        }

        if (npc.SocialNeed >= 52)
        {
            var companion = BestCompanion(state, npc);

            if (companion is not null)
            {
                return new(
                    companion.CurrentRoomId,
                    ActionKind.Socialize,
                    companion.Name,
                    $"Going to find {companion.Name}.",
                    $"I should check in with {companion.Name}.",
                    $"Hey, {companion.Name}. Got a minute?",
                    15);
            }
        }

        var route = WorkRoutes[npc.Role];
        var phase = ((minute / 60) + (int)npc.Role) % route.Length;
        var target = route[phase];

        return new(
            target,
            ActionKind.Work,
            target,
            $"Heading to {state.Facility.Rooms[target].Name} for routine duties.",
            "Back to work.",
            DutyBubble(npc.Role),
            45);
    }

    private static Npc? BestCompanion(GameState state, Npc npc)
    {
        return state.Crew
            .Where(other => other.IsAlive && other.Id != npc.Id)
            .OrderByDescending(other =>
            {
                var relationship = npc.Relationships[other.Name];
                return relationship.Trust
                    + relationship.Affinity
                    - relationship.Resentment;
            })
            .ThenBy(other => other.Name)
            .FirstOrDefault();
    }

    private void BeginActivity(
        GameState state,
        Npc npc,
        RoutinePlan plan)
    {
        var targetId = plan.PersonTargetId ?? plan.TargetRoomId;

        if (!_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    plan.Action,
                    targetId,
                    plan.TravelReason),
                out _))
        {
            return;
        }

        npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(plan.DurationMinutes);

        SetBubble(
            npc,
            plan.ActivityBubble,
            plan.Action == ActionKind.Socialize
                ? NpcBubbleKind.Speech
                : NpcBubbleKind.Thought,
            state.Elapsed,
            plan.Action == ActionKind.Sleep ? 4 : 5);
    }

    private static string DutyBubble(CrewRole role) => role switch
    {
        CrewRole.Commander => "Let's see how the station is doing.",
        CrewRole.Engineer => "I should check the systems.",
        CrewRole.Security => "Time for another patrol.",
        CrewRole.Doctor => "I should check medical.",
        CrewRole.Technician => "There's always something to maintain.",
        CrewRole.Scientist => "I need to get back to my work.",
        _ => "Back to work."
    };

    private static void SetBubble(
        Npc npc,
        string text,
        NpcBubbleKind kind,
        TimeSpan now,
        int durationMinutes)
    {
        npc.Bubble = new NpcBubble(
            text,
            kind,
            now,
            now + TimeSpan.FromMinutes(durationMinutes));
    }

    private sealed record RoutinePlan(
        string TargetRoomId,
        ActionKind Action,
        string? PersonTargetId,
        string TravelReason,
        string TravelBubble,
        string ActivityBubble,
        int DurationMinutes);
}
