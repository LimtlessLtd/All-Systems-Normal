using Overseer.Domain;

namespace Overseer.AI;

public sealed class RuleBasedAiDecisionService : IAiDecisionService
{
    public Task<NpcIntent> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default)
    {
        NpcIntent intent;
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        if (CrewEnvironmentSafety.IsDangerous(room))
        {
            var saferRoom = FindSaferRoom(state, room);

            intent = saferRoom is not null
                ? Create(
                    npc,
                    state,
                    ActionKind.Move,
                    saferRoom.Id,
                    $"Get to {saferRoom.Name}.",
                    "The atmosphere or temperature here is becoming dangerous.",
                    96)
                : Create(
                    npc,
                    state,
                    ActionKind.Idle,
                    null,
                    "Shelter and call for emergency help.",
                    "The environment is dangerous and I cannot identify a safer reachable room.",
                    98);
        }
        else if (npc.Hunger >= 62)
        {
            intent = Create(npc, state, ActionKind.Eat, null,
                "Get something to eat.",
                "I am hungry enough that food is becoming difficult to ignore.",
                80);
        }
        else if (npc.Fatigue >= 72)
        {
            intent = Create(npc, state, ActionKind.Sleep, null,
                "Get some sleep.",
                "I am too tired to keep working effectively.",
                78);
        }
        else if (npc.BladderNeed >= 72)
        {
            intent = Create(npc, state, ActionKind.UseToilet, null,
                "Use the washroom.",
                "I need the toilet and should deal with that now.",
                84);
        }
        else if (npc.HygieneNeed >= 65)
        {
            intent = Create(npc, state, ActionKind.Shower, null,
                "Take a shower.",
                "I need to clean up before I can comfortably focus.",
                66);
        }
        else if (npc.RecreationNeed >= 62)
        {
            intent = Create(npc, state, ActionKind.Recreate, null,
                "Take a break.",
                "I need some recreation before I burn out.",
                52);
        }
        else
        {
            var worstRelationship = npc.Relationships.Values
                .OrderByDescending(r => r.Resentment)
                .FirstOrDefault();

            if (worstRelationship is { Resentment: >= 55 })
            {
                intent = Create(npc, state, ActionKind.Argue, worstRelationship.PersonName,
                    $"Confront {worstRelationship.PersonName}.",
                    $"My resentment toward {worstRelationship.PersonName} has been building.",
                    65);
            }
            else
            {
                var bestRelationship = npc.Relationships.Values
                    .OrderByDescending(r => r.Trust + r.Affinity)
                    .FirstOrDefault();

                if (bestRelationship is not null
                    && npc.Personality.Sociability >= 55
                    && npc.SocialNeed >= 45)
                {
                    intent = Create(npc, state, ActionKind.Socialize, bestRelationship.PersonName,
                        $"Spend time with {bestRelationship.PersonName}.",
                        $"I trust {bestRelationship.PersonName} and would rather not be alone.",
                        38);
                }
                else
                {
                    intent = Create(npc, state, ActionKind.Idle, null,
                        "Keep an eye on things.",
                        "Nothing feels urgent enough to justify changing what I am doing.",
                        20);
                }
            }
        }

        return Task.FromResult(intent);
    }

    private static Room? FindSaferRoom(GameState state, Room currentRoom)
    {
        var currentRisk = CrewEnvironmentSafety.RiskScore(currentRoom);
        var reachable = ReachableRooms(state.Facility, currentRoom.Id);

        return state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor
                && reachable.Contains(room.Id)
                && CrewEnvironmentSafety.RiskScore(room) + 0.1 < currentRisk)
            .OrderBy(room => CrewEnvironmentSafety.IsHabitable(room) ? 0 : 1)
            .ThenBy(CrewEnvironmentSafety.RiskScore)
            .ThenBy(room => Math.Abs(room.MapX - currentRoom.MapX) + Math.Abs(room.MapY - currentRoom.MapY))
            .ThenBy(room => room.Id)
            .FirstOrDefault();
    }

    private static HashSet<string> ReachableRooms(Facility facility, string startRoomId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            startRoomId
        };
        var queue = new Queue<string>();
        queue.Enqueue(startRoomId);

        while (queue.TryDequeue(out var current))
        {
            foreach (var door in facility.Doors.Where(door =>
                         door.IsPassable
                         && (door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase)
                             || door.RoomBId.Equals(current, StringComparison.OrdinalIgnoreCase))))
            {
                var next = door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase)
                    ? door.RoomBId
                    : door.RoomAId;

                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }

    private static NpcIntent Create(
        Npc npc,
        GameState state,
        ActionKind action,
        string? targetId,
        string goal,
        string reason,
        int urgency) =>
        new(
            action,
            targetId,
            goal,
            reason,
            urgency,
            "Fallback",
            state.Elapsed);
}
