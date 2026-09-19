using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Lightweight deterministic cognition for the static GitHub Pages build.
/// It exercises the same persistent-intent pipeline as the LLM without
/// shipping model credentials or pretending the browser demo is LLM-backed.
/// </summary>
public sealed class BrowserMindSystem
{
    public void Tick(GameState state)
    {
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

        var intent = Decide(npc, state);
        npc.Intent = intent;
        npc.MindMode = "Browser demo";
        npc.LastThought = intent.Reason;
        npc.LastThoughtAt = state.Elapsed;
        npc.Bubble = new NpcBubble(
            intent.Goal,
            NpcBubbleKind.Thought,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(3));

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Thought,
            npc.Id.ToString(),
            npc.CurrentRoomId);
    }

    private static NpcIntent Decide(Npc npc, GameState state)
    {
        var currentRoom = state.Facility.Rooms[npc.CurrentRoomId];
        if (IsEnvironmentDangerous(currentRoom))
        {
            var saferRoom = FindSaferRoom(state, currentRoom);
            if (saferRoom is not null)
            {
                return Create(state, ActionKind.Move, saferRoom.Id,
                    $"Get to {saferRoom.Name}.",
                    "The atmosphere or temperature here is becoming dangerous.",
                    96);
            }

            return Create(state, ActionKind.RequestHelp, null,
                "Get emergency help.",
                "The environment is dangerous and I cannot identify a safer room.",
                98);
        }

        if (npc.Hunger >= 58)
        {
            return Create(state, ActionKind.Eat, null,
                "Find something to eat.",
                "I am getting hungry and want a proper meal.",
                75);
        }

        if (npc.Fatigue >= 68)
        {
            return Create(state, ActionKind.Sleep, null,
                "Get some sleep.",
                "I am exhausted enough that I should sleep.",
                72);
        }

        if (npc.BladderNeed >= 72)
        {
            return Create(state, ActionKind.UseToilet, null,
                "Use the washroom.",
                "I really need the toilet.",
                82);
        }

        if (npc.HygieneNeed >= 60)
        {
            return Create(state, ActionKind.Shower, null,
                "Take a shower.",
                "I feel grimy and want to clean up.",
                64);
        }

        if (npc.RecreationNeed >= 58)
        {
            return Create(state, ActionKind.Recreate, null,
                "Take a proper break.",
                "I need time to unwind instead of working constantly.",
                55);
        }

        var tense = npc.Relationships.Values
            .OrderByDescending(r => r.Resentment)
            .FirstOrDefault();

        if (tense is { Resentment: >= 48 })
        {
            return Create(state, ActionKind.Argue, tense.PersonName,
                $"Confront {tense.PersonName}.",
                $"I am increasingly irritated with {tense.PersonName}.",
                62);
        }

        var trusted = npc.Relationships.Values
            .OrderByDescending(r => r.Trust + r.Affinity)
            .FirstOrDefault();

        if (trusted is not null && npc.Personality.Sociability >= 50)
        {
            return Create(state, ActionKind.Socialize, trusted.PersonName,
                $"Talk to {trusted.PersonName}.",
                $"I feel comfortable around {trusted.PersonName}.",
                35);
        }

        return Create(state, ActionKind.Idle, null,
            "Stay alert.",
            "Nothing feels urgent right now.",
            15);
    }

    private static bool IsEnvironmentDangerous(Room room) =>
        room.OxygenPercent < 18
        || room.CarbonDioxidePercent > 2
        || room.PressureKpa < 85
        || room.TemperatureC is < 10 or > 34;

    private static Room? FindSaferRoom(GameState state, Room currentRoom) =>
        state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor
                && room.IsPowered
                && room.OxygenPercent >= 19
                && room.CarbonDioxidePercent < 1
                && room.PressureKpa >= 90
                && room.TemperatureC is >= 16 and <= 28)
            .OrderBy(room => Math.Abs(room.MapX - currentRoom.MapX) + Math.Abs(room.MapY - currentRoom.MapY))
            .ThenBy(room => room.Id)
            .FirstOrDefault();

    private static NpcIntent Create(
        GameState state,
        ActionKind action,
        string? target,
        string goal,
        string reason,
        int urgency) =>
        new(action, target, goal, reason, urgency, "Browser demo", state.Elapsed);
}
