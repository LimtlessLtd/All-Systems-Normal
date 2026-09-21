using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class MemoryRetentionSystemTests
{
    private static readonly TimeSpan Now = TimeSpan.FromHours(48);

    [Fact]
    public void RecentMemory_OutranksAStaleButStrongerOne()
    {
        var stale = new Memory("Argument with Emma Voss.", Now - TimeSpan.FromHours(30), 0.6);
        var fresh = new Memory("Emma told me Marcus is missing.", Now - TimeSpan.FromHours(1), 0.45);

        Assert.True(MemorySalience.Score(fresh, Now) > MemorySalience.Score(stale, Now));
    }

    [Fact]
    public void DefiningMoments_OutlastTrivia()
    {
        var killing = new Memory("Witnessed Marcus attack Emma.", Now - TimeSpan.FromHours(10), 1.0);
        var smallTalk = new Memory("Emma spoke well of Felix.", Now - TimeSpan.FromHours(1), 0.3);

        Assert.True(MemorySalience.Score(killing, Now) > MemorySalience.Score(smallTalk, Now));
    }

    [Fact]
    public void Trim_KeepsTheMostSalientWithinTheCapInOrder()
    {
        var npc = FacilitySeeder.CreateDefault(stationSeed: 202).Crew[0];
        npc.Memories.Clear();

        for (var index = 0; index < 60; index++)
        {
            npc.Memories.Add(new Memory(
                $"Routine note {index}.",
                Now - TimeSpan.FromHours(20) + TimeSpan.FromMinutes(index),
                0.3));
        }

        var vivid = new Memory("Found a body in the Reactor.", Now - TimeSpan.FromHours(12), 0.95);
        npc.Memories.Insert(5, vivid);
        var original = npc.Memories.ToList();

        MemoryRetentionSystem.Trim(npc, Now);

        Assert.Equal(MemoryRetentionSystem.MaxMemoriesPerCrew, npc.Memories.Count);
        Assert.Contains(vivid, npc.Memories);
        Assert.Equal(original.Where(npc.Memories.Contains).ToList(), npc.Memories);
        Assert.DoesNotContain(npc.Memories, memory => memory.Description == "Routine note 0.");
    }

    [Fact]
    public void Trim_ForgetsFadedTriviaAfterADay()
    {
        var npc = FacilitySeeder.CreateDefault(stationSeed: 202).Crew[0];
        npc.Memories.Clear();
        var trivia = new Memory("I decided to: tidy the lockers", Now - TimeSpan.FromHours(40), 0.25);
        var recent = new Memory("I decided to: eat", Now - TimeSpan.FromHours(1), 0.25);
        npc.Memories.Add(trivia);
        npc.Memories.Add(recent);

        MemoryRetentionSystem.Trim(npc, Now);

        Assert.DoesNotContain(trivia, npc.Memories);
        Assert.Contains(recent, npc.Memories);
    }

    [Fact]
    public void Prompt_ShowsWhatIsOnTheirMindNow()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        state.Elapsed = Now;
        var npc = state.Crew[0];
        npc.Memories.Clear();

        for (var index = 0; index < 8; index++)
        {
            npc.Memories.Add(new Memory($"Old strong memory {index}.", Now - TimeSpan.FromHours(40), 0.8));
        }

        npc.Memories.Add(new Memory("Emma told me the Reactor hatch was welded.", Now - TimeSpan.FromMinutes(20), 0.5));

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains("Emma told me the Reactor hatch was welded.", prompt, StringComparison.Ordinal);
    }
}
