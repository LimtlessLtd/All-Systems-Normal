using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class SimulationEngineTests
{
    [Fact]
    public void Tick_AdvancesTimeAndIncreasesEverydayNeeds()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var hungerBefore = npc.Hunger;
        var fatigueBefore = npc.Fatigue;
        var hygieneBefore = npc.HygieneNeed;
        var recreationBefore = npc.RecreationNeed;
        var socialBefore = npc.SocialNeed;
        var intimacyBefore = npc.IntimacyNeed;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(10), state.Elapsed);
        Assert.True(npc.Hunger > hungerBefore);
        Assert.True(npc.Fatigue > fatigueBefore);
        Assert.True(npc.HygieneNeed > hygieneBefore);
        Assert.True(npc.RecreationNeed > recreationBefore);
        Assert.True(npc.SocialNeed > socialBefore);
        Assert.True(npc.IntimacyNeed > intimacyBefore);
    }

    [Fact]
    public void Tick_SleepingCrewMemberRecoversFatigue()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        npc.Fatigue = 50;
        npc.CurrentAction = new NpcAction(
            ActionKind.Sleep,
            null,
            "Sleeping.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.Fatigue < 50);
    }

    [Fact]
    public void Tick_ShoweringCrewMemberReducesHygieneNeed()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        npc.HygieneNeed = 80;
        npc.CurrentAction = new NpcAction(
            ActionKind.Shower,
            null,
            "Taking a shower.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.HygieneNeed < 80);
    }
}
