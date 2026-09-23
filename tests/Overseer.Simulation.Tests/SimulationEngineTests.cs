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
    public void Tick_PhysicalBedUseAccumulatesIntoDeterministicPreference()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var quarters = state.Facility.Rooms["quarters"];
        var beds = quarters.Fixtures
            .Where(fixture => fixture.Type == FixtureType.Bed)
            .Take(2)
            .ToList();

        Assert.True(beds.Count >= 2);

        npc.CurrentRoomId = quarters.Id;
        npc.CurrentAction = new NpcAction(
            ActionKind.Sleep,
            null,
            "Sleeping physically at a bed.");

        npc.PositionX = beds[0].X;
        npc.PositionY = beds[0].Y;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        npc.PositionX = beds[1].X;
        npc.PositionY = beds[1].Y;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(20));

        var firstKey = PersonalSpaceSystem.FixtureKey(quarters.Id, beds[0]);
        var secondKey = PersonalSpaceSystem.FixtureKey(quarters.Id, beds[1]);

        Assert.Equal(10, npc.FixtureUseMinutes[firstKey], 3);
        Assert.Equal(20, npc.FixtureUseMinutes[secondKey], 3);
        Assert.Equal(secondKey, PersonalSpaceSystem.PreferredBedKey(npc));
    }

    [Fact]
    public void Tick_SleepFlagAwayFromBedDoesNotCreatePreference()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var quarters = state.Facility.Rooms["quarters"];

        npc.CurrentRoomId = quarters.Id;
        npc.PositionX = 50;
        npc.PositionY = 50;
        npc.CurrentAction = new NpcAction(
            ActionKind.Sleep,
            null,
            "Trying to sleep away from a bed.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.Empty(npc.FixtureUseMinutes);
        Assert.Null(PersonalSpaceSystem.PreferredBedKey(npc));
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

    [Fact]
    public void DefaultStation_HasOneContestedToiletFixture()
    {
        var state = FacilitySeeder.CreateDefault();

        var toilets = state.Facility.Rooms["washroom"].Fixtures
            .Where(fixture => fixture.Type == FixtureType.Toilet)
            .ToList();

        Assert.Single(toilets);
    }

    [Fact]
    public void Tick_UseToiletActionAwayFromFixtureDoesNotRelieveBladderNeed()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.CurrentRoomId = "washroom";
        npc.PositionX = 95;
        npc.PositionY = 95;
        npc.BladderNeed = 80;
        npc.CurrentAction = new NpcAction(
            ActionKind.UseToilet,
            "washroom",
            "Heading for the toilet.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(npc.BladderNeed > 80);
    }

    [Fact]
    public void Tick_OneToiletAllowsOnlyOneSimultaneousUser()
    {
        var state = FacilitySeeder.CreateDefault();
        var washroom = state.Facility.Rooms["washroom"];
        Assert.Single(washroom.Fixtures, fixture =>
            fixture.Type == FixtureType.Toilet);
        var contenders = state.Crew
            .Take(2)
            .OrderBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var occupant = contenders[0];
        var waiting = contenders[1];

        foreach (var npc in contenders)
        {
            npc.CurrentRoomId = washroom.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.BladderNeed = 80;
            npc.CurrentAction = new NpcAction(
                ActionKind.UseToilet,
                washroom.Id,
                "Trying to use the toilet.");
        }

        // Use the same production local-movement contract to reach the
        // collision-safe interaction point. Local routing advances one detour
        // segment per tick, so walk it normally rather than assuming one giant
        // delta can skip the route.
        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 30; minute++)
            movement.Tick(state, TimeSpan.FromMinutes(1));

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(occupant.BladderNeed < 80);
        Assert.True(waiting.BladderNeed > 80);
    }
}
