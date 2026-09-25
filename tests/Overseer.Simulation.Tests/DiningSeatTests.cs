using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #90: crew who choose to eat sit at a free chair. Chairs are
/// capacity-1; with every chair taken the rest eat standing, at a small
/// stress cost, while a seated meal is a small comfort.
/// </summary>
public sealed class DiningSeatTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);

    [Fact]
    public void EatersWalkToFreeChairs_AndTheRestEatStanding()
    {
        var (state, eaters) = KitchenWithEaters(5);
        var chairs = state.Facility.Rooms["kitchen"].Fixtures.Count(f => f.Type == FixtureType.Chair);
        Assert.Equal(4, chairs);

        Walk(state, eaters, minutes: 6);

        var seats = eaters.Select(npc => DiningSeatRules.SeatedAt(state, npc)).ToList();
        Assert.Equal(4, seats.Count(seat => seat is not null));
        Assert.Equal(4, seats.Where(seat => seat is not null).Distinct().Count());
        var standing = Assert.Single(eaters, npc => DiningSeatRules.SeatedAt(state, npc) is null);
        Assert.True(DiningSeatRules.IsEatingStanding(state, standing));
    }

    [Fact]
    public void ASeatedEaterKeepsTheirChairWhenSomeoneNewArrives()
    {
        var (state, eaters) = KitchenWithEaters(1);
        Walk(state, eaters, minutes: 6);
        var first = eaters[0];
        var chair = DiningSeatRules.SeatedAt(state, first);
        Assert.NotNull(chair);

        // A newcomer whose name sorts first still does not take the chair.
        var newcomer = state.Crew.First(npc => !eaters.Contains(npc));
        var spot = ClearSpots(state.Facility.Rooms["kitchen"], 2)[1];
        Place(newcomer, "kitchen", spot.X, spot.Y);
        eaters.Add(newcomer);
        Walk(state, eaters, minutes: 6);

        Assert.Same(chair, DiningSeatRules.SeatedAt(state, first));
        Assert.NotNull(DiningSeatRules.SeatedAt(state, newcomer));
        Assert.NotSame(chair, DiningSeatRules.SeatedAt(state, newcomer));
    }

    [Fact]
    public void SeatedMealsRelieveStress_StandingMealsAddALittle()
    {
        var (state, eaters) = KitchenWithEaters(5);
        state.Stores.Meals = 100;
        Walk(state, eaters, minutes: 6);
        var seated = eaters.First(npc => DiningSeatRules.SeatedAt(state, npc) is not null);
        var standing = eaters.Single(npc => DiningSeatRules.SeatedAt(state, npc) is null);
        seated.Stress = 40;
        standing.Stress = 40;
        var hungerSeated = seated.Hunger;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Contains(seated.StatLog, e => e.Cause == "eating seated" && e.Delta < 0);
        Assert.Contains(standing.StatLog, e => e.Cause == "no free seat to eat at" && e.Delta > 0);
        Assert.True(seated.Stress < standing.Stress);
        Assert.True(seated.Hunger < hungerSeated, "eating still relieves hunger");
    }

    [Fact]
    public void WalkingToAFreeChairIsNeitherComfortNorPenalty()
    {
        var (state, eaters) = KitchenWithEaters(1);
        state.Stores.Meals = 100;
        var npc = eaters[0];
        Assert.Null(DiningSeatRules.SeatedAt(state, npc));
        Assert.False(DiningSeatRules.IsEatingStanding(state, npc));

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.DoesNotContain(npc.StatLog, e => e.Cause is "eating seated" or "no free seat to eat at");
    }

    [Fact]
    public void NotEating_HasNoSeat()
    {
        var (state, eaters) = KitchenWithEaters(1);
        eaters[0].CurrentAction = new NpcAction(ActionKind.Work, null, "Working.");

        Assert.Null(DiningSeatRules.SeatFor(state, eaters[0]));
        Assert.False(DiningSeatRules.IsEatingStanding(state, eaters[0]));
    }

    [Fact]
    public void CognitionSeesHowManyChairsAreFreeHere_WhenFoodIsOnItsMind()
    {
        var (state, eaters) = KitchenWithEaters(5);
        var hungryArrival = eaters[0];

        var before = NpcPromptBuilder.Build(hungryArrival, state);
        Assert.Contains("SEATS: 4 of 4 seats here are free.", before);

        Walk(state, eaters, minutes: 6);
        var seated = eaters.First(npc => DiningSeatRules.SeatedAt(state, npc) is not null);
        var prompt = NpcPromptBuilder.Build(seated, state);
        Assert.Contains("SEATS: 0 of 4 seats here are free. You are sitting in one.", prompt);
        Assert.Contains("Whether to eat now or wait for a seat is up to you.", prompt);

        // Not eating and not hungry: no seat line.
        var other = state.Crew.First(npc => !eaters.Contains(npc));
        other.CurrentRoomId = "kitchen";
        other.Hunger = 5;
        other.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        Assert.DoesNotContain("SEATS:", NpcPromptBuilder.Build(other, state));

        // Hungry in the Control Room: its console chairs are not a place to eat.
        other.CurrentRoomId = "control";
        other.Hunger = 80;
        Assert.DoesNotContain("SEATS:", NpcPromptBuilder.Build(other, state));
    }

    [Fact]
    public void AMealChosenForTheLounge_IsCollectedInTheGalley_CarriedThere_AndEatenSeated()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Stores.Meals = 20;
        var npc = state.Crew.First(candidate => candidate.IsAlive);
        npc.CurrentRoomId = "control";
        npc.Movement = null;
        npc.Hunger = 70;
        npc.Stress = 40;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        npc.Intent = EatIntent(state, "lounge");

        var galleyMealsBefore = state.Stores.Meals;
        var collected = false;
        RunUntil(state, npc, () =>
        {
            collected |= npc.CarriedMealPortion > 0;
            return npc.CurrentRoomId == "lounge" && DiningSeatRules.SeatedAt(state, npc) is not null;
        });

        Assert.True(collected, "the meal was physically collected on the way");
        Assert.Contains(state.EventLog, line => line.Contains($"{npc.Name} takes a meal from the galley to eat in"));
        var portion = galleyMealsBefore - state.Stores.Meals;
        Assert.Equal(DiningSeatRules.CarriedMealSize, portion, 6);

        // Eating in the lounge draws only on what they carried.
        var galleyMeals = state.Stores.Meals;
        var hunger = npc.Hunger;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(galleyMeals, state.Stores.Meals);
        Assert.True(npc.Hunger < hunger);
        Assert.Contains(npc.StatLog, e => e.Cause == "eating seated");

        // When the carried meal is gone they stop; the galley is not raided remotely.
        for (var i = 0; i < 60 && npc.CurrentAction.Kind == ActionKind.Eat; i++)
        {
            new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.Equal(0, npc.CarriedMealPortion);
        Assert.Equal(ActionKind.Idle, npc.CurrentAction.Kind);
        Assert.Equal(galleyMeals, state.Stores.Meals);
    }

    [Fact]
    public void AMealChosenForMedical_IsCollectedInTheGalley_AndEatenAtABedside()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Stores.Meals = 20;
        var npc = state.Crew.First(candidate => candidate.IsAlive);
        npc.CurrentRoomId = "control";
        npc.Movement = null;
        npc.Hunger = 70;
        npc.Stress = 40;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        npc.Intent = EatIntent(state, "medical");

        var collected = false;
        RunUntil(state, npc, () =>
        {
            collected |= npc.CarriedMealPortion > 0;
            return npc.CurrentRoomId == "medical"
                && DiningSeatRules.SeatedAt(state, npc) is { Type: FixtureType.MedicalBed };
        });

        Assert.True(collected, "the bedside meal was physically collected from the galley first");
        var bedside = Assert.IsType<RoomFixture>(DiningSeatRules.SeatedAt(state, npc));
        Assert.Equal(FixtureType.MedicalBed, bedside.Type);

        var galleyMeals = state.Stores.Meals;
        var hunger = npc.Hunger;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(galleyMeals, state.Stores.Meals);
        Assert.True(npc.Hunger < hunger);
        Assert.Contains(npc.StatLog, entry => entry.Cause == "eating seated");
    }

    [Fact]
    public void OccupiedMedicalBed_IsNotAssignedAsABedsideDiningSeat()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var medical = state.Facility.Rooms["medical"];
        var beds = medical.Fixtures.Where(fixture => fixture.Type == FixtureType.MedicalBed).ToList();
        Assert.True(beds.Count >= 2);

        var patient = state.Crew[0];
        patient.CurrentRoomId = medical.Id;
        patient.PositionX = beds[0].X;
        patient.PositionY = beds[0].Y;
        patient.CurrentAction = new NpcAction(ActionKind.Rest, medical.Id, "Recovering in bed.");

        var eater = state.Crew[1];
        eater.CurrentRoomId = medical.Id;
        eater.PositionX = 50;
        eater.PositionY = 80;
        eater.CarriedMealPortion = DiningSeatRules.CarriedMealSize;
        eater.CurrentAction = new NpcAction(ActionKind.Eat, medical.Id, "Eating at bedside.");

        var assigned = Assert.IsType<RoomFixture>(DiningSeatRules.SeatFor(state, eater));
        Assert.NotSame(beds[0], assigned);
        Assert.Equal(FixtureType.MedicalBed, assigned.Type);
    }

    [Fact]
    public void TheRoutineLetsSomeoneFinishTheMealTheyCarried_InsteadOfSendingThemToTheGalley()
    {
        // Soak regression: a take-away eater in the lounge had no intent left,
        // so the hunger routine walked them straight back to the galley.
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Elapsed = TimeSpan.FromHours(4);
        var npc = state.Crew.First(candidate => candidate.IsAlive);
        Place(npc, "lounge", 50, 50);
        npc.Hunger = 60;
        npc.Intent = null;
        npc.RoutineUntil = TimeSpan.Zero;
        npc.CarriedMealPortion = DiningSeatRules.CarriedMealSize;

        new CrewRoutineSystem().Tick(state);

        Assert.Equal("lounge", npc.CurrentRoomId);
        Assert.Equal(ActionKind.Eat, npc.CurrentAction.Kind);
        Assert.Null(npc.PlannedDestinationRoomId);

        // With nothing carried, the routine still heads for the galley.
        npc.CarriedMealPortion = 0;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        npc.RoutineUntil = TimeSpan.Zero;
        new CrewRoutineSystem().Tick(state);
        Assert.Equal("kitchen", npc.PlannedDestinationRoomId);
    }

    [Fact]
    public void WithNoPreparedMealToTake_TheyEatInTheGalleyInstead()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Stores.Meals = 0;
        var npc = state.Crew.First(candidate => candidate.IsAlive);
        Place(npc, "kitchen", 50, 30);
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        npc.Intent = EatIntent(state, "lounge");

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(0, npc.CarriedMealPortion);
        Assert.Equal("kitchen", npc.CurrentRoomId);
        Assert.Equal(ActionKind.Eat, npc.CurrentAction.Kind);
    }

    [Fact]
    public void EatingAwayFromTheGalleyNeedsACarriedMeal()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = state.Crew.First(candidate => candidate.IsAlive);
        npc.CurrentRoomId = "lounge";
        var resolver = new ActionResolver();

        Assert.False(resolver.TryApply(state, npc.Id, new NpcAction(ActionKind.Eat, "lounge", "Eat."), out _));

        npc.CarriedMealPortion = 1;
        Assert.True(resolver.TryApply(state, npc.Id, new NpcAction(ActionKind.Eat, "lounge", "Eat."), out _));

        // Not every room with a chair is somewhere to eat.
        npc.CurrentRoomId = "control";
        Assert.False(resolver.TryApply(state, npc.Id, new NpcAction(ActionKind.Eat, "control", "Eat."), out _));
    }

    [Fact]
    public void AnUnsuitableEatTargetMeansTheGalley()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);

        Assert.Equal("kitchen", DiningSeatRules.DiningRoomFor(state, null));
        Assert.Equal("kitchen", DiningSeatRules.DiningRoomFor(state, "reactor"));
        Assert.Equal("kitchen", DiningSeatRules.DiningRoomFor(state, "no-such-room"));
        Assert.Equal("lounge", DiningSeatRules.DiningRoomFor(state, "LOUNGE"));
        Assert.Equal("quarters", DiningSeatRules.DiningRoomFor(state, "quarters"));
        Assert.Equal("medical", DiningSeatRules.DiningRoomFor(state, "medical"));
    }

    [Fact]
    public async Task BothFallbackMinds_TakeTheirMealToTheLounge_OnlyWhenTheGalleyIsFull()
    {
        var (state, eaters) = KitchenWithEaters(4);
        state.Stores.Meals = 50;
        var hungry = state.Crew.First(npc => !eaters.Contains(npc) && npc.IsAlive);
        hungry.CurrentRoomId = "control";
        hungry.Hunger = 80;

        // Galley chairs are free: both minds eat in the galley.
        Assert.Null(DiningSeatRules.FallbackDiningTarget(state, hungry));

        Walk(state, eaters, minutes: 6);
        Assert.All(eaters, npc => Assert.NotNull(DiningSeatRules.SeatedAt(state, npc)));

        // Every galley chair is taken: both minds carry it to the lounge.
        Assert.Equal("lounge", DiningSeatRules.FallbackDiningTarget(state, hungry));
        hungry.NeedsMindReconsideration = true;
        new BrowserMindSystem().Tick(state);
        var browser = hungry.Intent;
        var server = await new RuleBasedAiDecisionService().DecideAsync(hungry, state);
        Assert.NotNull(browser);
        Assert.Equal(ActionKind.Eat, browser.Action);
        Assert.Equal(ActionKind.Eat, server.Action);
        Assert.Equal("lounge", browser.TargetId);
        Assert.Equal("lounge", server.TargetId);

        // Someone already seated in the galley stays in their chair.
        Assert.Null(DiningSeatRules.FallbackDiningTarget(state, eaters[0]));

        // No prepared meals to carry: the galley it is.
        state.Stores.Meals = 0.5;
        Assert.Null(DiningSeatRules.FallbackDiningTarget(state, hungry));
    }

    [Fact]
    public void CognitionIsToldWhereElseItCouldEat()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Stores.Meals = 12;
        var npc = state.Crew.First(candidate => candidate.IsAlive);
        npc.CurrentRoomId = "control";
        npc.Hunger = 60;

        var prompt = NpcPromptBuilder.Build(npc, state);
        Assert.Contains("DINING: food is kept in the galley (12 prepared meals).", prompt);
        Assert.Contains("[lounge] 3 of 3 seats free", prompt);
        Assert.Contains("Crew Quarters [quarters] ", prompt);
        Assert.Contains("[medical] 2 of 2 seats free", prompt);
        Assert.Contains("For Eat, TargetId is null to eat in the galley", prompt);

        npc.Hunger = 5;
        Assert.DoesNotContain("DINING:", NpcPromptBuilder.Build(npc, state));
    }

    private static NpcIntent EatIntent(GameState state, string? target) =>
        new(ActionKind.Eat, target, "Eat.", "Hungry.", 75, "Test", state.Elapsed);

    private static void RunUntil(GameState state, Npc npc, Func<bool> done)
    {
        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();
        var engine = new SimulationEngine();
        for (var i = 0; i < 3 * 60 && !done(); i++)
        {
            intents.Tick(state);
            movement.Tick(state, Tick);
            engine.Tick(state, Tick);
        }

        Assert.True(done(), $"{npc.Name} is {npc.CurrentAction.Kind} in {npc.CurrentRoomId}");
    }

    private static (GameState State, List<Npc> Eaters) KitchenWithEaters(int count)
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var eaters = state.Crew
            .OrderByDescending(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();
        var spots = ClearSpots(state.Facility.Rooms["kitchen"], count);
        for (var i = 0; i < eaters.Count; i++)
        {
            Place(eaters[i], "kitchen", spots[i].X, spots[i].Y);
        }

        return (state, eaters);
    }

    /// <summary>
    /// Spots of open floor for <paramref name="count"/> arrivals, 8 apart and
    /// clear of every fixture, so nobody starts out already in a chair
    /// wherever the layout pass put the mess table.
    /// </summary>
    private static List<(double X, double Y)> ClearSpots(Room room, int count)
    {
        var spots = new List<(double X, double Y)>();
        for (var y = 10d; y <= 90 && spots.Count < count; y += 4)
        {
            for (var x = 10d; x <= 90 && spots.Count < count; x += 4)
            {
                if (room.Fixtures.All(fixture =>
                        Math.Abs(x - fixture.X) > (fixture.Width / 2) + 4
                        || Math.Abs(y - fixture.Y) > (fixture.Height / 2) + 4)
                    && spots.All(spot => Math.Abs(spot.X - x) >= 8 || Math.Abs(spot.Y - y) >= 8))
                {
                    spots.Add((x, y));
                }
            }
        }

        Assert.Equal(count, spots.Count);
        return spots;
    }

    private static void Place(Npc npc, string roomId, double x, double y)
    {
        npc.CurrentRoomId = roomId;
        npc.PositionX = x;
        npc.PositionY = y;
        npc.Movement = null;
        npc.Hunger = 70;
        npc.CurrentAction = new NpcAction(ActionKind.Eat, roomId, "Eating.");
    }

    private static void Walk(GameState state, List<Npc> eaters, int minutes)
    {
        var movement = new LocalMovementSystem();
        for (var i = 0; i < minutes * 3; i++)
        {
            foreach (var npc in eaters)
            {
                npc.CurrentAction = new NpcAction(ActionKind.Eat, "kitchen", "Eating.");
            }

            movement.Tick(state, Tick);
        }
    }
}
