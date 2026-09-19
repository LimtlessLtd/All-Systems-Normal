using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class SimulationEngineTests
{
    [Fact]
    public void Tick_AdvancesTimeAndIncreasesBasicNeeds()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var hungerBefore = npc.Hunger;
        var fatigueBefore = npc.Fatigue;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(10), state.Elapsed);
        Assert.True(npc.Hunger > hungerBefore);
        Assert.True(npc.Fatigue > fatigueBefore);
    }

    [Fact]
    public void Tick_RestingCrewMemberRecoversFatigue()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        npc.Fatigue = 50;
        npc.CurrentAction = new NpcAction(
            ActionKind.Rest,
            null,
            "I need to recover.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.Fatigue < 50);
    }
}
