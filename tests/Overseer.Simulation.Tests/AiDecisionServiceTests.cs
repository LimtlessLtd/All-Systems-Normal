using Microsoft.Extensions.AI;
using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class AiDecisionServiceTests
{
    [Fact]
    public async Task OllamaDecision_ProducesAValidatedPersistentIntent()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "Move",
              "TargetId": "engineering",
              "Goal": "Check the engineering systems.",
              "Reason": "The station has been unstable and I want to inspect the machinery.",
              "Urgency": 67
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.Equal("engineering", intent.TargetId);
        Assert.Equal("Ollama", intent.Source);
        Assert.Equal(67, intent.Urgency);
    }

    [Fact]
    public async Task InvalidModelTarget_IsReducedToSafeIdle()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "Move",
              "TargetId": "secret-moon-base",
              "Goal": "Go somewhere imaginary.",
              "Reason": "I invented a location.",
              "Urgency": 99
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task ProviderFailure_FallsBackWithoutStoppingTheSimulation()
    {
        using var client = new StubChatClient(
            new InvalidOperationException("Ollama offline"));

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal("Fallback", intent.Source);
    }

    [Fact]
    public void Prompt_DoesNotLeakGlobalEventLogKnowledge()
    {
        var state = FacilitySeeder.CreateDefault();
        state.EventLog.Insert(0, "SECRET: Marcus sabotaged something where Sarah could not see it.");

        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var prompt = NpcPromptBuilder.Build(sarah, state);

        Assert.DoesNotContain("SECRET:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONNECTED DOORS YOU CAN DIRECTLY PERCEIVE", prompt);
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly string? _json;
        private readonly Exception? _exception;

        public StubChatClient(string json) => _json = json;
        public StubChatClient(Exception exception) => _exception = exception;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                return Task.FromException<ChatResponse>(_exception);
            }

            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, _json!)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : null;

        public void Dispose()
        {
        }
    }
}
