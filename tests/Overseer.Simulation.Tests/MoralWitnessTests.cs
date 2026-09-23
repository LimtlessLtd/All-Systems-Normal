using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class MoralWitnessTests
{
    [Fact]
    public void VentingWithOthersInside_LeavesATaggedMemoryForEachWitness()
    {
        var (state, room, actor, first, second) = SmokyRoomWithThree();

        Assert.True(StationHazardSystem.TryExecuteCrewAction(state, actor, ActionKind.VentHazardRoom, room, out _));

        var firstMemory = Assert.Single(first.Memories, m => m.MoralActorName is not null);
        Assert.Equal(actor.Name, firstMemory.MoralActorName);
        Assert.Equal($"Witnessed {actor.Name} vent {room.Name} while I and {second.Name} were still inside.", firstMemory.Description);
        Assert.Single(second.Memories, m => m.MoralActorName == actor.Name);
        Assert.DoesNotContain(actor.Memories, m => m.MoralActorName is not null);
    }

    [Fact]
    public void VentingAlone_RecordsNoWitnessedDecision()
    {
        var (state, room, actor, first, second) = SmokyRoomWithThree();
        var elsewhere = state.Facility.Rooms.Keys.First(id => id != room.Id);
        first.CurrentRoomId = elsewhere;
        second.CurrentRoomId = elsewhere;

        Assert.True(StationHazardSystem.TryExecuteCrewAction(state, actor, ActionKind.VentHazardRoom, room, out _));

        Assert.All(state.Crew, npc => Assert.DoesNotContain(npc.Memories, m => m.MoralActorName is not null));
    }

    [Fact]
    public void Prompt_ListsWitnessedDecisionsForCognitionToJudge()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var memory = new Memory("Witnessed Marcus Reed attack Emma Voss.", state.Elapsed, 0.92, MoralActorName: "Marcus Reed");
        var npc = state.Crew[0];
        npc.Memories.Clear();
        npc.Memories.Add(memory);

        var prompt = NpcPromptBuilder.Build(npc, state);

        var start = prompt.IndexOf("OTHER PEOPLE'S DECISIONS YOU WITNESSED", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var block = prompt[start..prompt.IndexOf("PLACES WHERE YOU NEARLY DIED", start, StringComparison.Ordinal)];
        Assert.Contains("- Witnessed Marcus Reed attack Emma Voss.", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptWithoutWitnessedDecisions_SaysNone()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var npc = state.Crew[0];
        npc.Memories.Clear();

        var prompt = NpcPromptBuilder.Build(npc, state);

        var start = prompt.IndexOf("OTHER PEOPLE'S DECISIONS YOU WITNESSED", StringComparison.Ordinal);
        var block = prompt[start..prompt.IndexOf("PLACES WHERE YOU NEARLY DIED", start, StringComparison.Ordinal)];
        Assert.Contains("- none", block, StringComparison.Ordinal);
    }

    private static (GameState State, Room Room, Npc Actor, Npc First, Npc Second) SmokyRoomWithThree()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var actor = state.Crew[0];
        var first = state.Crew[1];
        var second = state.Crew[2];
        var room = state.Facility.Rooms[actor.CurrentRoomId];
        var elsewhere = state.Facility.Rooms.Keys.First(id => id != room.Id);
        foreach (var npc in state.Crew)
        {
            npc.Memories.Clear();
            npc.CurrentRoomId = elsewhere;
        }

        foreach (var npc in new[] { actor, first, second })
        {
            npc.CurrentRoomId = room.Id;
            npc.PositionX = actor.PositionX;
            npc.PositionY = actor.PositionY;
        }

        room.IsPowered = true;
        room.LightsOn = true;
        room.SmokePercent = 20;
        room.PressureKpa = 101;
        return (state, room, actor, first, second);
    }
}
