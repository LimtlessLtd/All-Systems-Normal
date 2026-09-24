using Overseer.AI;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class NpcPromptBudgetTests
{
    // English prompt text averages roughly 4 characters per token; 3 keeps a
    // margin for IDs, numbers and punctuation, which tokenize less densely.
    private const double ConservativeCharsPerToken = 3.0;

    [Fact]
    public void DecisionPromptAndOutputBudget_FitTheRequestedContextWindow()
    {
        // Owner report (2026-09-24): decisions were cut short. The default
        // station's prompt had grown to ~33k characters, so prompt plus output
        // overflowed the 8192-token window and Ollama silently truncated it.
        var largestPrompt = 0;

        foreach (var seed in Enumerable.Range(1, 30))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);
            foreach (var npc in state.Crew)
            {
                largestPrompt = Math.Max(
                    largestPrompt,
                    NpcPromptBuilder.Build(npc, state).Length);
            }
        }

        var estimatedPromptTokens = (int)Math.Ceiling(
            largestPrompt / ConservativeCharsPerToken);

        Assert.True(
            estimatedPromptTokens + OllamaAiDecisionService.DecisionMaxOutputTokens
                <= OllamaAiDecisionService.ContextWindowTokens,
            $"Largest prompt is {largestPrompt} chars (~{estimatedPromptTokens} tokens); "
            + $"with {OllamaAiDecisionService.DecisionMaxOutputTokens} output tokens it "
            + $"exceeds num_ctx {OllamaAiDecisionService.ContextWindowTokens}.");
    }
}
