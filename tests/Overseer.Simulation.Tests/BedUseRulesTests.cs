using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class BedUseRulesTests
{
    [Fact]
    public void ConcurrentSleepersReceiveDistinctBedsUntilCapacityIsFull()
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(4242), stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep capacity regression.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var assignments = sleepers
            .Select(npc => BedUseRules.AssignedBed(state, npc))
            .ToList();

        var beds = quarters.Fixtures.Count(fixture =>
            fixture.Type is FixtureType.Bed or FixtureType.MedicalBed);
        Assert.True(sleepers.Count > beds, "Regression roster must exceed physical bed capacity.");
        Assert.Equal(beds, assignments.Count(fixture => fixture is not null));
        Assert.Equal(
            beds,
            assignments
                .Where(fixture => fixture is not null)
                .Select(fixture => fixture!.Label)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Equal(sleepers.Count - beds, assignments.Count(fixture => fixture is null));
    }

    [Fact]
    public void DuplicateBedLabels_DoNotCollapsePhysicalCapacity()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var bedIndexes = quarters.Fixtures
            .Select((fixture, index) => (fixture, index))
            .Where(pair => pair.fixture.Type == FixtureType.Bed)
            .Take(2)
            .ToList();
        Assert.Equal(2, bedIndexes.Count);

        var first = bedIndexes[0].fixture;
        var second = bedIndexes[1].fixture;
        quarters.Fixtures[bedIndexes[1].index] = second with { Label = first.Label };

        var sleepers = state.Crew.Take(2).ToList();
        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Duplicate-label capacity regression.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var assignments = sleepers
            .Select(npc => Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, npc)))
            .ToList();

        Assert.NotSame(assignments[0], assignments[1]);
        Assert.Equal(assignments[0].Label, assignments[1].Label);
        Assert.NotEqual((assignments[0].X, assignments[0].Y), (assignments[1].X, assignments[1].Y));
    }

    [Fact]
    public void SleepersPhysicallyWalkTowardDifferentAssignedBeds()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(2).ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep in a real bunk.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var firstBed = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[0]));
        var secondBed = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[1]));
        Assert.NotEqual(firstBed.Label, secondBed.Label);

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 20; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.NotEqual(
            (Math.Round(sleepers[0].PositionX, 2), Math.Round(sleepers[0].PositionY, 2)),
            (Math.Round(sleepers[1].PositionX, 2), Math.Round(sleepers[1].PositionY, 2)));
    }

    [Fact]
    public void SettledSleeperKeepsAssignedBedWhenPeerStopsSleeping()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(2)
            .OrderBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep in a stable bunk.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 20; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        var retained = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[1]));

        sleepers[0].CurrentAction = new NpcAction(ActionKind.Idle, quarters.Id, "Done resting.");

        var after = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[1]));
        Assert.Equal(retained.Label, after.Label);
    }

    [Fact]
    public void SleeperWithoutABedDoesNotReceiveRestorativeRecovery()
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(4242), stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep capacity regression.");
            npc.Fatigue = 80;
            npc.SleepDebtMinutes = 240;
        }

        var overflow = sleepers.First(npc => BedUseRules.AssignedBed(state, npc) is null);
        var fatigueBefore = overflow.Fatigue;
        var debtBefore = overflow.SleepDebtMinutes;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(overflow.Fatigue >= fatigueBefore);
        Assert.True(overflow.SleepDebtMinutes >= debtBefore);
    }

    [Fact]
    public void SleeperIsAsleepInTheirBedOnlyOnceTheyHaveReachedIt()
    {
        // Owner idea #96: the map draws a sleeper lying on the bed from this
        // state, so it must be true only for a still body at its own bed.
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(2).ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep in a real bunk.");
            npc.Intent = null;
            npc.Movement = null;
        }

        Assert.All(sleepers, npc => Assert.Null(BedUseRules.BedAsleepIn(state, npc)));

        var movement = new LocalMovementSystem();
        movement.Tick(state, TimeSpan.FromMinutes(1));
        Assert.All(sleepers, npc =>
        {
            Assert.True(npc.IsLocallyMoving);
            Assert.Null(BedUseRules.BedAsleepIn(state, npc));
        });

        for (var minute = 0; minute < 20; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        var beds = sleepers
            .Select(npc => Assert.IsType<RoomFixture>(BedUseRules.BedAsleepIn(state, npc)))
            .ToList();
        Assert.Equal(BedUseRules.AssignedBed(state, sleepers[0])!.Label, beds[0].Label);
        Assert.Equal(BedUseRules.AssignedBed(state, sleepers[1])!.Label, beds[1].Label);
        Assert.NotEqual(beds[0].Label, beds[1].Label);

        sleepers[0].CurrentAction = new NpcAction(ActionKind.Idle, quarters.Id, "Awake.");
        Assert.Null(BedUseRules.BedAsleepIn(state, sleepers[0]));
        Assert.NotNull(BedUseRules.BedAsleepIn(state, sleepers[1]));
    }

    [Fact]
    public void OverflowSleeperWithoutABedIsNeverDrawnInOne()
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(4242), stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];

        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep capacity regression.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 30; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        var overflow = state.Crew.Where(npc => BedUseRules.AssignedBed(state, npc) is null).ToList();
        Assert.NotEmpty(overflow);
        Assert.All(overflow, npc => Assert.Null(BedUseRules.BedAsleepIn(state, npc)));
        Assert.All(
            state.Crew.Where(npc => BedUseRules.BedAsleepIn(state, npc) is not null),
            npc => Assert.Equal(
                BedUseRules.AssignedBed(state, npc)!.Label,
                BedUseRules.BedAsleepIn(state, npc)!.Label));
    }

    [Fact]
    public void ASleeperIsNotSentOntoAMedicalBedSomeoneIsEatingAt()
    {
        // #95 edge from the #190 review: bed assignment ignored a seated
        // bedside eater, so a sleeper walked onto the bed they were eating at.
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var medical = state.Facility.Rooms["medical"];
        var firstBed = FirstMedicalBedInAssignmentOrder(medical);
        var (eater, sleeper) = EaterAndSleeperIn(state, medical, firstBed);

        Assert.Same(firstBed, DiningSeatRules.SeatedAt(state, eater));
        var assigned = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleeper));
        Assert.NotSame(firstBed, assigned);
        Assert.Equal(FixtureType.MedicalBed, assigned.Type);

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 10; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
            Assert.False(
                Math.Abs(sleeper.PositionX - firstBed.X) <= firstBed.Width / 2
                    && Math.Abs(sleeper.PositionY - firstBed.Y) <= firstBed.Height / 2,
                "the sleeper must not walk onto the bed the eater is using");
        }

        Assert.Same(assigned, BedUseRules.BedAsleepIn(state, sleeper));
        Assert.Same(firstBed, DiningSeatRules.SeatedAt(state, eater));
    }

    [Fact]
    public void AMedicalBedFreesForSleepersOnceTheBedsideMealEnds()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var medical = state.Facility.Rooms["medical"];
        var firstBed = FirstMedicalBedInAssignmentOrder(medical);
        var (eater, sleeper) = EaterAndSleeperIn(state, medical, firstBed);
        Assert.NotSame(firstBed, BedUseRules.AssignedBed(state, sleeper));

        eater.CurrentAction = new NpcAction(ActionKind.Idle, medical.Id, "Finished eating.");

        Assert.Same(firstBed, BedUseRules.AssignedBed(state, sleeper));
    }

    [Fact]
    public void WithEveryMedicalBedHoldingAnEater_ASleeperThereHasNoBed()
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(4242), stationSeed: 1337);
        var medical = state.Facility.Rooms["medical"];
        var beds = medical.Fixtures.Where(fixture => fixture.Type == FixtureType.MedicalBed).ToList();
        Assert.True(state.Crew.Count > beds.Count, "Regression roster must exceed the medical beds.");

        foreach (var (bed, eater) in beds.Zip(state.Crew))
        {
            Place(eater, medical, bed.X, bed.Y);
            eater.CarriedMealPortion = DiningSeatRules.CarriedMealSize;
            eater.CurrentAction = new NpcAction(ActionKind.Eat, medical.Id, "Eating at bedside.");
        }

        var sleeper = state.Crew[beds.Count];
        Place(sleeper, medical, 50, 85);
        sleeper.CurrentAction = new NpcAction(ActionKind.Sleep, medical.Id, "Sleeping in medical.");

        Assert.All(state.Crew.Take(beds.Count), eater => Assert.NotNull(DiningSeatRules.SeatedAt(state, eater)));
        Assert.Null(BedUseRules.AssignedBed(state, sleeper));
    }

    private static RoomFixture FirstMedicalBedInAssignmentOrder(Room medical)
    {
        var beds = medical.Fixtures
            .Where(fixture => fixture.Type == FixtureType.MedicalBed)
            .OrderBy(fixture => fixture.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(fixture => fixture.X)
            .ThenBy(fixture => fixture.Y)
            .ToList();
        Assert.True(beds.Count >= 2, "Regression needs at least two medical beds.");
        return beds[0];
    }

    private static (Npc Eater, Npc Sleeper) EaterAndSleeperIn(GameState state, Room medical, RoomFixture bed)
    {
        var eater = state.Crew[0];
        Place(eater, medical, bed.X, bed.Y);
        eater.CarriedMealPortion = DiningSeatRules.CarriedMealSize;
        eater.CurrentAction = new NpcAction(ActionKind.Eat, medical.Id, "Eating at bedside.");

        var sleeper = state.Crew[1];
        Place(sleeper, medical, 50, 85);
        sleeper.CurrentAction = new NpcAction(ActionKind.Sleep, medical.Id, "Sleeping in medical.");
        return (eater, sleeper);
    }

    private static void Place(Npc npc, Room room, double x, double y)
    {
        npc.CurrentRoomId = room.Id;
        npc.PositionX = x;
        npc.PositionY = y;
        npc.Intent = null;
        npc.Movement = null;
    }

    [Fact]
    public void MapDrawsSleepersInBedFromTheAuthoritativeBedState()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Overseer.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor.css"));

        Assert.Contains("BedUseRules.BedAsleepIn(Session.State, npc)?.X ?? npc.PositionX", home);
        Assert.Contains("BedUseRules.BedAsleepIn(Session.State, npc)?.Y ?? npc.PositionY", home);
        Assert.Contains("classes.Add(\"is-asleep-in-bed\")", home);
        Assert.Contains("--crew-bed-angle", home);
        Assert.Contains(".crew-token.is-asleep-in-bed .person-icon", css);
        Assert.Contains("rotate(var(--crew-bed-angle, -90deg))", css);
    }
}
