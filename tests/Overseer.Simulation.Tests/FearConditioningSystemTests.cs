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
}
