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
    }

    private static NpcIntent Decide(Npc npc, GameState state)
    {
        if (npc.Hunger >= 58)
        {
            return Create(state, ActionKind.Eat, null,
                "Find something to eat.",
                "I am getting hungry and want a proper meal.",
                75);
        }

        if (npc.Fatigue >= 68)
        {
            return Create(state, ActionKind.Rest, null,
                "Get some sleep.",
                "I am exhausted enough that I should rest.",
                72);
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

    private static NpcIntent Create(
        GameState state,
        ActionKind action,
        string? target,
        string goal,
        string reason,
        int urgency) =>
        new(action, target, goal, reason, urgency, "Browser demo", state.Elapsed);
}
