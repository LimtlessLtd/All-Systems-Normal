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
        Place(newcomer, "kitchen", 50, 30);
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
        Assert.Contains("SEATS: 4 of 4 chairs here are free.", before);

        Walk(state, eaters, minutes: 6);
        var seated = eaters.First(npc => DiningSeatRules.SeatedAt(state, npc) is not null);
        var prompt = NpcPromptBuilder.Build(seated, state);
        Assert.Contains("SEATS: 0 of 4 chairs here are free. You are sitting in one.", prompt);
        Assert.Contains("Whether to eat now, wait or eat elsewhere is up to you.", prompt);

        // Not eating and not hungry: no seat line.
        var other = state.Crew.First(npc => !eaters.Contains(npc));
        other.CurrentRoomId = "kitchen";
        other.Hunger = 5;
        other.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        Assert.DoesNotContain("SEATS:", NpcPromptBuilder.Build(other, state));
    }

    private static (GameState State, List<Npc> Eaters) KitchenWithEaters(int count)
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var eaters = state.Crew
            .OrderByDescending(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();
        for (var i = 0; i < eaters.Count; i++)
        {
            Place(eaters[i], "kitchen", 30 + (i * 8), 30);
        }

        return (state, eaters);
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
