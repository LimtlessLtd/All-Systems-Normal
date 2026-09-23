using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class FearConditioningSystemTests
{
    [Fact]
    public void SurvivingNearDeath_RecordsARoomTaggedTraumaticMemoryOnce()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.Health = FearConditioningSystem.NearDeathHealthThreshold;
        var system = new FearConditioningSystem();

        system.Tick(state);

        var memory = Assert.Single(npc.Memories, m => m.TraumaRoomId is not null);
        Assert.Equal(npc.CurrentRoomId, memory.TraumaRoomId);
        Assert.Contains(state.Facility.Rooms[npc.CurrentRoomId].Name, memory.Description, StringComparison.Ordinal);
        Assert.True(npc.NearDeathCrisisRecorded);

        // Staying at the same low health another tick must not spam a second
        // traumatic memory for the same ongoing crisis.
        system.Tick(state);
        Assert.Single(npc.Memories, m => m.TraumaRoomId is not null);
    }

    [Fact]
    public void RecoveringThenCrashingAgain_RecordsASeparateMemoryForTheNewCrisis()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.Health = FearConditioningSystem.NearDeathHealthThreshold;
        var system = new FearConditioningSystem();
        system.Tick(state);
        Assert.True(npc.NearDeathCrisisRecorded);

        npc.Health = 80;
        system.Tick(state);
        Assert.False(npc.NearDeathCrisisRecorded);

        npc.Health = FearConditioningSystem.NearDeathHealthThreshold;
        system.Tick(state);

        Assert.Equal(2, npc.Memories.Count(m => m.TraumaRoomId is not null));
    }

    [Fact]
    public void HealthAboveTheThreshold_NeverRecordsATraumaticMemory()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.Health = FearConditioningSystem.NearDeathHealthThreshold + 1;

        new FearConditioningSystem().Tick(state);

        Assert.DoesNotContain(npc.Memories, m => m.TraumaRoomId is not null);
    }

    [Fact]
    public void ADeadCrewMember_NeverRecordsATraumaticMemory()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.Health = 0;

        new FearConditioningSystem().Tick(state);

        Assert.DoesNotContain(npc.Memories, m => m.TraumaRoomId is not null);
    }
    [Fact]
    public void Prompt_ListsTheRoomWhereTheNpcNearlyDied()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.Health = FearConditioningSystem.NearDeathHealthThreshold;
        new FearConditioningSystem().Tick(state);
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        var block = TraumaBlock(NpcPromptBuilder.Build(npc, state));

        Assert.Contains($"- {room.Id} = {room.Name} | vivid memory | you are here now", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_TraumaFadesWithMemorySalience_AndCountsRepeatCloseCalls()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var otherRoom = state.Facility.Rooms.Values.First(r => r.Id != npc.CurrentRoomId);
        state.Elapsed = TimeSpan.FromHours(70);
        npc.Memories.Add(new Memory("Nearly died.", TimeSpan.FromHours(10), 0.85, TraumaRoomId: otherRoom.Id));
        npc.Memories.Add(new Memory("Nearly died.", TimeSpan.FromHours(12), 0.85, TraumaRoomId: otherRoom.Id));

        var block = TraumaBlock(NpcPromptBuilder.Build(npc, state));

        Assert.Contains($"- {otherRoom.Id} = {otherRoom.Name} | faint memory | 2 separate close calls", block, StringComparison.Ordinal);
        Assert.DoesNotContain("you are here now", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_WithoutTraumaMemories_SaysNone()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        var block = TraumaBlock(NpcPromptBuilder.Build(npc, state));

        Assert.Contains("- none", block, StringComparison.Ordinal);
    }

    private static string TraumaBlock(string prompt)
    {
        var start = prompt.IndexOf("PLACES WHERE YOU NEARLY DIED", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = prompt.IndexOf("\n\n", start, StringComparison.Ordinal);
        if (end < 0) end = prompt.IndexOf("\r\n\r\n", start, StringComparison.Ordinal);
        return end < 0 ? prompt[start..] : prompt[start..end];
    }
}
