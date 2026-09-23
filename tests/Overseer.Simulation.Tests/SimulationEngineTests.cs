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
        var bladderBefore = npc.BladderNeed;
        var recreationBefore = npc.RecreationNeed;
        var socialBefore = npc.SocialNeed;
        var intimacyBefore = npc.IntimacyNeed;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(10), state.Elapsed);
        Assert.True(npc.Hunger > hungerBefore);
        Assert.True(npc.Fatigue > fatigueBefore);
        Assert.True(npc.HygieneNeed > hygieneBefore);
        Assert.True(npc.BladderNeed > bladderBefore);
        Assert.True(npc.RecreationNeed > recreationBefore);
        Assert.True(npc.SocialNeed > socialBefore);
        Assert.True(npc.IntimacyNeed > intimacyBefore);
    }

    [Fact]
    public void Tick_SleepingCrewMemberRecoversFatigue()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        var quarters = state.Facility.Rooms["quarters"];
        var bed = quarters.Fixtures.First(fixture =>
            fixture.Type is FixtureType.Bed or FixtureType.MedicalBed);
        npc.CurrentRoomId = quarters.Id;
        npc.PositionX = bed.X;
        npc.PositionY = bed.Y;
        npc.Fatigue = 50;
        npc.CurrentAction = new NpcAction(
            ActionKind.Sleep,
            null,
            "Sleeping physically at a bed.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.Fatigue < 50);
    }

    [Fact]
    public void Tick_ComfortEatingWhileStressedAndNotHungryRelievesStress()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        npc.Hunger = 20;
        npc.Stress = 65;
        npc.CurrentAction = new NpcAction(ActionKind.Eat, null, "Eating for comfort.");
        var mealsBefore = state.Stores.Meals;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.Stress < 65);
        Assert.True(state.Stores.Meals < mealsBefore - (0.07 * 10));
    }

    [Fact]
    public void Tick_OrdinaryMealWhileNotStressedGivesNoComfortEatingBonus()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        npc.Hunger = 20;
        npc.Stress = 30;
        npc.CurrentAction = new NpcAction(ActionKind.Eat, null, "Having a meal.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        // No comfort-eating relief below the stress threshold: only the small
        // baseline stress decay every crew member gets regardless of action.
        Assert.True(npc.Stress > 28);
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
