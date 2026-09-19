using System.Text.Json;
using Microsoft.Extensions.AI;
using Overseer.Domain;

namespace Overseer.AI;

public sealed class OllamaAiDecisionService(
    IChatClient chatClient,
    RuleBasedAiDecisionService fallback) : IAiDecisionService
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly RuleBasedAiDecisionService _fallback = fallback;

    private static readonly HashSet<ActionKind> AllowedActions =
    [
        ActionKind.Idle,
        ActionKind.Move,
        ActionKind.Rest,
        ActionKind.Sleep,
        ActionKind.Eat,
        ActionKind.Recreate,
        ActionKind.Groom,
        ActionKind.Shower,
        ActionKind.UseToilet,
        ActionKind.Work,
        ActionKind.Investigate,
        ActionKind.Repair,
        ActionKind.Talk,
        ActionKind.Socialize,
        ActionKind.Argue,
        ActionKind.RequestHelp
    ];

    public async Task<NpcIntent> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = NpcPromptBuilder.Build(npc, state);

            var response = await _chatClient.GetResponseAsync<NpcMindDecision>(
                prompt,
                options: new ChatOptions
                {
                    Temperature = 0.7f,
                    MaxOutputTokens = 240
                },
                useJsonSchemaResponseFormat: true,
                cancellationToken: cancellationToken);

            NpcMindDecision? decision = null;

            if (!response.TryGetResult(out decision) || decision is null)
            {
                decision = TryParse(response.Text);
            }

            if (decision is null)
            {
                throw new InvalidOperationException(
                    "The model did not return a valid structured decision.");
            }

            return Validate(npc, state, decision);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await _fallback.DecideAsync(npc, state, cancellationToken);
        }
    }

    private static NpcMindDecision? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NpcMindDecision>(
                text,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NpcIntent Validate(
        Npc npc,
        GameState state,
        NpcMindDecision decision)
    {
        if (!Enum.TryParse<ActionKind>(decision.Action, true, out var action)
            || !AllowedActions.Contains(action))
        {
            action = ActionKind.Idle;
        }

        string? target = decision.TargetId?.Trim();

        if (action is ActionKind.Move or ActionKind.Investigate or ActionKind.Repair or ActionKind.Work)
        {
            if (target is null || !state.Facility.Rooms.ContainsKey(target))
            {
                action = ActionKind.Idle;
                target = null;
            }
        }
        else if (action is ActionKind.Talk
            or ActionKind.Socialize
            or ActionKind.Argue
            or ActionKind.RequestHelp)
        {
            var person = state.Crew.FirstOrDefault(other =>
                other.IsAlive
                && other.Id != npc.Id
                && other.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (person is null)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = person.Name;
            }
        }
        else
        {
            target = null;
        }

        var goal = Clean(decision.Goal, "Decide what to do next.");
        var reason = Clean(decision.Reason, "I need a moment to decide what matters.");

        return new NpcIntent(
            action,
            target,
            goal,
            reason,
            Math.Clamp(decision.Urgency, 0, 100),
            "Ollama",
            state.Elapsed);
    }

    private static string Clean(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var clean = value.Trim();
        return clean.Length <= 220 ? clean : clean[..220];
    }
}
