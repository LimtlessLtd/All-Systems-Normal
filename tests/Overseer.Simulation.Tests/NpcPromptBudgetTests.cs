using Overseer.AI;
using Overseer.Domain;
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

        AssertFitsContextWindow(largestPrompt, "Fresh-station");
    }

    [Fact]
    public void AccumulatedMidGamePromptAndOutputBudget_FitTheRequestedContextWindow()
    {
        var largestPrompt = 0;

        foreach (var seed in Enumerable.Range(1, 10))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: 10_000 + seed);
            AddAccumulatedMidGameContext(state);

            foreach (var npc in state.Crew)
            {
                largestPrompt = Math.Max(
                    largestPrompt,
                    NpcPromptBuilder.Build(npc, state).Length);
            }
        }

        AssertFitsContextWindow(largestPrompt, "Accumulated mid-game");
    }

    private static void AddAccumulatedMidGameContext(GameState state)
    {
        state.Elapsed = TimeSpan.FromHours(6);

        foreach (var (npc, index) in state.Crew.Select((npc, index) => (npc, index)))
        {
            var other = state.Crew[(index + 1) % state.Crew.Count];

            for (var memoryIndex = 0; memoryIndex < 12; memoryIndex++)
            {
                npc.Memories.Add(new Memory(
                    $"Shift event {memoryIndex}: {other.Name} and I dealt with a noisy equipment problem, a disputed report, and changing work priorities.",
                    state.Elapsed - TimeSpan.FromMinutes(memoryIndex * 17),
                    Math.Max(0.3, 0.95 - memoryIndex * 0.04),
                    IsFailedAttempt: memoryIndex == 0,
                    IsSensitive: memoryIndex < 5,
                    TraumaRoomId: memoryIndex < 3 ? npc.CurrentRoomId : null,
                    MoralActorName: memoryIndex < 5 ? other.Name : null,
                    PanicClaimRoomId: memoryIndex < 3 ? npc.CurrentRoomId : null,
                    ObservedFireRoomId: memoryIndex == 0 ? npc.CurrentRoomId : null));
            }

            for (var beliefIndex = 0; beliefIndex < 5; beliefIndex++)
            {
                npc.Beliefs.Add(new Belief(
                    $"midgame-subject-{beliefIndex}",
                    $"I currently think observation {beliefIndex} matters, but I am not completely sure what it means.",
                    0.55 + beliefIndex * 0.08));
            }

            npc.PendingSuggestion = new NpcSuggestion(
                other.Name,
                "Check the noisy machinery with me before we decide what to tell everyone.",
                state.Elapsed - TimeSpan.FromMinutes(2));
        }

        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        state.CrewPacts.Add(new CrewPact
        {
            Id = "pact-midgame-budget",
            PromisorId = promisor.Id,
            PromiseeId = promisee.Id,
            Kind = CrewPactKind.Other,
            PromiseText = "I will cover the late maintenance round if you help me verify the report first.",
            CreatedAt = state.Elapsed - TimeSpan.FromHours(1),
            Deadline = state.Elapsed + TimeSpan.FromHours(2)
        });
        promisor.PendingPactProposal = new PactProposal(
            promisee.Id,
            promisee.Name,
            CrewPactKind.Other,
            "Keep the observation between us until we have checked it ourselves.",
            TriggerAt: null,
            Deadline: state.Elapsed + TimeSpan.FromHours(1),
            OfferedAt: state.Elapsed - TimeSpan.FromMinutes(3));

        var hazardRoom = state.Facility.Rooms[promisor.CurrentRoomId];
        hazardRoom.FireIntensity = 35;
        hazardRoom.SmokePercent = 20;

        state.Robots.Add(new StationRobot
        {
            Id = "robot-midgame-budget",
            Name = "Maintenance Robot",
            CurrentRoomId = promisor.CurrentRoomId,
            Policy = RobotPolicy.Hostile
        });
    }

    private static void AssertFitsContextWindow(int largestPrompt, string label)
    {
        var estimatedPromptTokens = (int)Math.Ceiling(
            largestPrompt / ConservativeCharsPerToken);

        Assert.True(
            estimatedPromptTokens + OllamaAiDecisionService.DecisionMaxOutputTokens
                <= OllamaAiDecisionService.ContextWindowTokens,
            $"{label} largest prompt is {largestPrompt} chars (~{estimatedPromptTokens} tokens); "
            + $"with {OllamaAiDecisionService.DecisionMaxOutputTokens} output tokens it "
            + $"exceeds num_ctx {OllamaAiDecisionService.ContextWindowTokens}.");
    }
}
