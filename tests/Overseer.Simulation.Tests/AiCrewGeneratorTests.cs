using Microsoft.Extensions.AI;
using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class AiCrewGeneratorTests
{
    [Fact]
    public async Task OllamaGeneratorCreatesEntireRosterWithOneToThreeMechanicalTraitsEach()
    {
        using var client = new StubChatClient(
            """
            {
              "Crew": [
                {
                  "Name": "Avery Knox",
                  "Role": "Commander",
                  "Empathy": 61, "Temper": 38, "Sociability": 70, "Courage": 77,
                  "Skills": [{"Name":"Leadership","Value":89},{"Name":"Operations","Value":78}],
                  "Traits": [{"Name":"Command Presence","Description":"Projects confidence when others hesitate.","Effects":[{"Kind":"Courage","Modifier":8},{"Kind":"Sociability","Modifier":4}]}]
                },
                {
                  "Name": "Inez Park",
                  "Role": "Engineer",
                  "Empathy": 57, "Temper": 45, "Sociability": 42, "Courage": 69,
                  "Skills": [{"Name":"Engineering","Value":91},{"Name":"Reactor","Value":75}],
                  "Traits": [
                    {"Name":"Patient Tinkerer","Description":"Stays with difficult machinery longer than most.","Effects":[{"Kind":"Repair","Modifier":11},{"Kind":"Technical","Modifier":7}]},
                    {"Name":"Conflict Averse","Description":"Dislikes direct confrontation.","Effects":[{"Kind":"Courage","Modifier":-5},{"Kind":"Temper","Modifier":-4}]}
                  ]
                },
                {
                  "Name": "Cal Rowan",
                  "Role": "Security",
                  "Empathy": 43, "Temper": 68, "Sociability": 50, "Courage": 86,
                  "Skills": [{"Name":"Security","Value":92},{"Name":"Athletics","Value":88}],
                  "Traits": [{"Name":"Heavy Handed","Description":"Trusts physical solutions under pressure.","Effects":[{"Kind":"Force","Modifier":13},{"Kind":"Temper","Modifier":5}]}]
                },
                {
                  "Name": "Mara Bell",
                  "Role": "Doctor",
                  "Empathy": 92, "Temper": 21, "Sociability": 76, "Courage": 59,
                  "Skills": [{"Name":"Medicine","Value":96},{"Name":"Psychology","Value":84}],
                  "Traits": [{"Name":"Protective","Description":"Other people's danger is hard to ignore.","Effects":[{"Kind":"Empathy","Modifier":12},{"Kind":"Courage","Modifier":4}]}]
                },
                {
                  "Name": "Theo Grant",
                  "Role": "Technician",
                  "Empathy": 55, "Temper": 52, "Sociability": 64, "Courage": 71,
                  "Skills": [{"Name":"Electrical","Value":90},{"Name":"Engineering","Value":73}],
                  "Traits": [
                    {"Name":"Improviser","Description":"Finds unconventional fixes quickly.","Effects":[{"Kind":"Repair","Modifier":12},{"Kind":"Technical","Modifier":6}]},
                    {"Name":"Nosy","Description":"Notices when system behaviour does not add up.","Effects":[{"Kind":"SuspicionSensitivity","Modifier":8}]},
                    {"Name":"Restless","Description":"Does poorly under prolonged confinement.","Effects":[{"Kind":"StressResistance","Modifier":-5},{"Kind":"Sociability","Modifier":3}]}
                  ]
                },
                {
                  "Name": "Sana Velez",
                  "Role": "Scientist",
                  "Empathy": 70, "Temper": 35, "Sociability": 54, "Courage": 56,
                  "Skills": [{"Name":"Research","Value":94},{"Name":"Reactor","Value":72}],
                  "Traits": [{"Name":"Sceptical","Description":"Questions convenient explanations.","Effects":[{"Kind":"SuspicionSensitivity","Modifier":10},{"Kind":"Technical","Modifier":3}]}]
                }
              ]
            }
            """);

        var generator = new OllamaCrewGenerator(
            client,
            new RuleBasedCrewGenerator());

        var crew = await generator.GenerateAsync();

        Assert.Equal(6, crew.Count);
        Assert.Equal(6, crew.Select(npc => npc.Role).Distinct().Count());
        Assert.Equal(6, crew.Select(npc => npc.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(crew, npc =>
        {
            Assert.Equal("AI generated", npc.GenerationSource);
            Assert.InRange(npc.Traits.Count, 1, 3);
            Assert.All(npc.Traits, trait =>
            {
                Assert.NotEmpty(trait.Name);
                Assert.NotEmpty(trait.Description);
                Assert.InRange(trait.Effects.Count, 1, 3);
                Assert.All(trait.Effects, effect =>
                    Assert.InRange(effect.Modifier, -15, 15));
            });
        });

        var state = FacilitySeeder.CreateDefault(crew);

        Assert.All(state.Crew, npc =>
            Assert.Equal(5, npc.Relationships.Count));
        Assert.All(state.Crew, npc =>
            Assert.DoesNotContain(
                npc.Name,
                npc.Relationships.Keys,
                StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvalidGeneratedRosterFallsBackSafely()
    {
        using var client = new StubChatClient(
            """
            { "Crew": [] }
            """);

        var generator = new OllamaCrewGenerator(
            client,
            new RuleBasedCrewGenerator());

        var crew = await generator.GenerateAsync();

        Assert.Equal(6, crew.Count);
        Assert.All(crew, npc =>
            Assert.Equal("Fallback generator", npc.GenerationSource));
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly string _json;

        public StubChatClient(string json) => _json = json;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, _json)));

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
