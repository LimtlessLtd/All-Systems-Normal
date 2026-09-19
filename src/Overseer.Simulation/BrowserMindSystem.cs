using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Lightweight deterministic cognition for the static GitHub Pages build.
/// It exercises the same persistent-intent pipeline as the LLM without
/// shipping model credentials or pretending the browser demo is LLM-backed.
/// </summary>
public sealed class BrowserMindSystem
{
    private readonly NavigationSystem _navigation = new();

    public void Tick(GameState state)
    {
        HandleEmergencyReconsiderations(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0 || minute % 6 != 0)
        {
            return;
        }

        var crew = state.Crew
            .Where(npc => npc.IsAlive)
            .OrderBy(npc => npc.Name)
            .ToList();

        if (crew.Count == 0)
        {
            return;
        }

        var npc = crew[(minute / 6) % crew.Count];

        if (npc.Intent is not null)
        {
            return;
        }

        SetIntent(state, npc, Decide(npc, state), NpcBubbleKind.Thought);
    }

    private void HandleEmergencyReconsiderations(GameState state)
    {
        foreach (var npc in state.Crew
                     .Where(npc => npc.IsAlive)
                     .OrderByDescending(npc =>
                         CrewEnvironmentSafety.RiskScore(
                             state.Facility.Rooms[npc.CurrentRoomId])))
        {
            var currentRoom = state.Facility.Rooms[npc.CurrentRoomId];

            if (!CrewEnvironmentSafety.IsDangerous(currentRoom)
                || IsAlreadyEscapingToSaferRoom(state, npc, currentRoom))
            {
                continue;
            }

            var saferRoom = FindSaferRoom(state, currentRoom);

            if (saferRoom is null
                && state.Elapsed - npc.LastThoughtAt < TimeSpan.FromMinutes(3)
                && npc.LastThought.Contains(
                    "cannot identify a safer room",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Emergency cognition pre-empts a low-priority routine before the
            // deterministic action/navigation layers validate the new goal.
            npc.Intent = null;
            npc.Movement = null;
            npc.RoutineUntil = TimeSpan.Zero;

            var intent = saferRoom is not null
                ? Create(
                    state,
                    ActionKind.Move,
                    saferRoom.Id,
                    $"Get to {saferRoom.Name} now.",
                    $"The environment in {currentRoom.Name} is dangerous; {saferRoom.Name} is safer.",
                    100)
                : Create(
                    state,
                    ActionKind.Idle,
                    null,
                    "Shelter and call for emergency help.",
                    "The environment is dangerous and I cannot identify a safer room.",
                    100);

            SetIntent(state, npc, intent, NpcBubbleKind.Alert);
        }
    }

    private NpcIntent Decide(Npc npc, GameState state)
    {
        var currentRoom = state.Facility.Rooms[npc.CurrentRoomId];

        if (CrewEnvironmentSafety.IsDangerous(currentRoom))
        {
            var saferRoom = FindSaferRoom(state, currentRoom);

            if (saferRoom is not null)
            {
                return Create(
                    state,
                    ActionKind.Move,
                    saferRoom.Id,
                    $"Get to {saferRoom.Name}.",
                    "The atmosphere or temperature here is becoming dangerous.",
                    96);
            }

            return Create(
                state,
                ActionKind.Idle,
                null,
                "Shelter and call for emergency help.",
                "The environment is dangerous and I cannot identify a safer room.",
                98);
        }

        if (npc.Hunger >= 58)
        {
            return Create(
                state,
                ActionKind.Eat,
                null,
                "Find something to eat.",
                "I am getting hungry and want a proper meal.",
                75);
        }

        if (npc.Fatigue >= 68)
        {
            return Create(
                state,
                ActionKind.Sleep,
                null,
                "Get some sleep.",
                "I am exhausted enough that I should sleep.",
                72);
        }

        if (npc.BladderNeed >= 72)
        {
            return Create(
                state,
                ActionKind.UseToilet,
                null,
                "Use the washroom.",
                "I really need the toilet.",
                82);
        }

        if (npc.HygieneNeed >= 60)
        {
            return Create(
                state,
                ActionKind.Shower,
                null,
                "Take a shower.",
                "I feel grimy and want to clean up.",
                64);
        }

        if (npc.RecreationNeed >= 58)
        {
            return Create(
                state,
                ActionKind.Recreate,
                null,
                "Take a proper break.",
                "I need time to unwind instead of working constantly.",
                55);
        }

        var tense = npc.Relationships.Values
            .OrderByDescending(r => r.Resentment)
            .FirstOrDefault();

        if (tense is { Resentment: >= 48 })
        {
            return Create(
                state,
                ActionKind.Argue,
                tense.PersonName,
                $"Confront {tense.PersonName}.",
                $"I am increasingly irritated with {tense.PersonName}.",
                62);
        }

        var trusted = npc.Relationships.Values
            .OrderByDescending(r => r.Trust + r.Affinity)
            .FirstOrDefault();

        if (trusted is not null
            && npc.Personality.Sociability >= 50
            && npc.SocialNeed >= 45)
        {
            return Create(
                state,
                ActionKind.Socialize,
                trusted.PersonName,
                $"Talk to {trusted.PersonName}.",
                $"I feel socially isolated and comfortable around {trusted.PersonName}.",
                45);
        }

        // Leaving this as Idle intentionally hands low-pressure time back to
        // CrewRoutineSystem, whose broad role routes make people circulate.
        return Create(
            state,
            ActionKind.Idle,
            null,
            "Stay alert and continue normal duties.",
            "Nothing feels urgent enough to interrupt my routine.",
            15);
    }

    private bool IsAlreadyEscapingToSaferRoom(
        GameState state,
        Npc npc,
        Room currentRoom)
    {
        if (npc.Intent is not { Action: ActionKind.Move, TargetId: { } targetId }
            || !state.Facility.Rooms.TryGetValue(targetId, out var targetRoom))
        {
            return false;
        }

        return CrewEnvironmentSafety.RiskScore(targetRoom)
            < CrewEnvironmentSafety.RiskScore(currentRoom);
    }

    private Room? FindSaferRoom(GameState state, Room currentRoom)
    {
        var currentRisk = CrewEnvironmentSafety.RiskScore(currentRoom);

        return state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor)
            .Select(room => new
            {
                Room = room,
                Risk = CrewEnvironmentSafety.RiskScore(room),
                Path = _navigation.FindPath(
                    state.Facility,
                    currentRoom.Id,
                    room.Id)
            })
            .Where(candidate =>
                candidate.Path.Count >= 2
                && candidate.Risk + 0.1 < currentRisk)
            .OrderBy(candidate =>
                CrewEnvironmentSafety.IsHabitable(candidate.Room) ? 0 : 1)
            .ThenBy(candidate => candidate.Risk)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Room.Id)
            .Select(candidate => candidate.Room)
            .FirstOrDefault();
    }

    private static void SetIntent(
        GameState state,
        Npc npc,
        NpcIntent intent,
        NpcBubbleKind bubbleKind)
    {
        npc.Intent = intent;
        npc.MindMode = "Browser demo";
        npc.LastThought = intent.Reason;
        npc.LastThoughtAt = state.Elapsed;
        npc.Bubble = new NpcBubble(
            intent.Goal,
            bubbleKind,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(
                bubbleKind == NpcBubbleKind.Alert ? 4 : 3));

        AudioCueSystem.Emit(
            state,
            bubbleKind == NpcBubbleKind.Alert
                ? AudioCueKind.Warning
                : AudioCueKind.Thought,
            npc.Id.ToString(),
            npc.CurrentRoomId);
    }

    private static NpcIntent Create(
        GameState state,
        ActionKind action,
        string? target,
        string goal,
        string reason,
        int urgency) =>
        new(action, target, goal, reason, urgency, "Browser demo", state.Elapsed);
}
