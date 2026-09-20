using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewProvisioningSystemTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>A station running everything, as the live session does.</summary>
    private sealed class Station
    {
        private readonly SimulationEngine _simulation = new();
        private readonly EnvironmentSystem _environment = new();
        private readonly StationUpkeepSystem _upkeep = new();
        private readonly CrewProvisioningSystem _provisioning = new();
        private readonly CrewMaintenanceSystem _maintenance = new();
        private readonly CrewRoutineSystem _routines = new();
        private readonly IntentExecutionSystem _intents = new();
        private readonly LocalMovementSystem _movement = new();
        private readonly BrowserMindSystem _mind = new();
        private readonly ConversationPacingSystem _pacing = new();

        public void Run(GameState state, int minutes)
        {
            for (var i = 0; i < minutes; i++)
            {
                _upkeep.Tick(state, Minute);
                _environment.Tick(state, Minute);
                _simulation.Tick(state, Minute);
                _mind.Tick(state);
                _intents.Tick(state);
                _provisioning.Tick(state, Minute);
                _maintenance.Tick(state);
                _pacing.Tick(state);
                _routines.Tick(state);
                _movement.Tick(state, Minute);
            }
        }
    }

    [Fact]
    public void TheHydroponicsBayIsPlantedWithStaggeredBeds()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);

        Assert.NotEmpty(state.CropBeds);
        Assert.All(state.CropBeds, bed => Assert.Equal("hydroponics", bed.RoomId));

        // A rolling harvest rather than everything ripening at once.
        Assert.True(state.CropBeds.Select(b => Math.Round(b.Growth)).Distinct().Count() > 1);
    }

    [Fact]
    public void CropsGrowWhenWateredFedAndLit()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var bed = state.CropBeds[0];
        bed.Growth = 10;
        bed.Water = 100;
        bed.Nutrients = 100;

        var system = new CrewProvisioningSystem();

        for (var i = 0; i < 5; i++)
        {
            system.Tick(state, Hour);
        }

        Assert.True(bed.Growth > 10);
    }

    [Fact]
    public void CuttingTheLightsInHydroponicsStopsTheCropAndStartsKillingIt()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var bed = state.CropBeds[0];
        bed.Growth = 50;
        bed.Water = 100;
        bed.Nutrients = 100;

        // Overseer does not have to touch the plants to ruin them.
        state.Facility.Rooms["hydroponics"].LightsOn = false;

        var system = new CrewProvisioningSystem();

        for (var i = 0; i < 6; i++)
        {
            system.Tick(state, Hour);
        }

        Assert.True(bed.Growth < 50);
    }

    [Fact]
    public void ABedWithNoWaterStopsGrowing()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var bed = state.CropBeds[0];
        bed.Growth = 40;
        bed.Water = 0;
        bed.Nutrients = 100;

        var system = new CrewProvisioningSystem();
        system.Tick(state, Hour);

        Assert.True(bed.Growth <= 40);
    }

    [Fact]
    public void HarvestingAMatureBedAddsProduceAndResetsIt()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var bed = state.CropBeds[0];
        bed.Growth = 100;
        bed.Water = 100;
        bed.Nutrients = 100;

        var growBeds = state.Devices.Values.Single(device =>
            device.Kind == StationSystemKind.GrowBeds
            && device.RoomId == "hydroponics");
        growBeds.Condition = 100;

        var worker = state.Crew.Single(npc => npc.Name == "Emma Voss");
        worker.CurrentRoomId = "hydroponics";
        worker.Hunger = 0;
        worker.Intent = null;
        worker.CurrentAction = new NpcAction(ActionKind.Idle, null, "Ready for crop duty.");

        foreach (var other in state.Crew.Where(npc => npc.Id != worker.Id))
        {
            other.Intent = new NpcIntent(
                ActionKind.Rest,
                null,
                "Protected test activity.",
                "Keep harvest ownership deterministic.",
                100,
                "Test",
                state.Elapsed);
        }

        var system = new CrewProvisioningSystem();
        var before = state.Stores.Produce;

        system.Tick(state, Minute);
        Assert.Equal(ActionKind.Harvest, worker.ProvisioningJob);

        system.Tick(state, Minute);
        Assert.NotNull(worker.ProvisioningCompletesAt);

        state.Elapsed = worker.ProvisioningCompletesAt!.Value;
        system.Tick(state, Minute);

        Assert.True(state.Stores.Produce > before);
        Assert.Equal(0, bed.Growth);
    }

    [Fact]
    public void EatingWithNoMealsInStoreDoesNotRelieveHunger()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = state.Crew[0];

        npc.Hunger = 80;
        npc.CurrentAction = new NpcAction(ActionKind.Eat, null, "Eating.");
        state.Stores.Meals = 0;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        // Hunger used to fall the instant somebody decided to eat, which made
        // the whole supply chain decorative.
        Assert.True(npc.Hunger >= 80);
    }

    [Fact]
    public void EatingDrawsDownTheGalleyStock()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = state.Crew[0];

        npc.Hunger = 80;
        npc.CurrentAction = new NpcAction(ActionKind.Eat, null, "Eating.");
        state.Stores.Meals = 20;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.Hunger < 80);
        Assert.True(state.Stores.Meals < 20);
    }

    [Fact]
    public void TheStationFeedsItselfOverAFullDay()
    {
        foreach (var seed in new[] { 1, 7, 99 })
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: seed);
            new Station().Run(state, 1440);

            Assert.Equal(6, state.Crew.Count(npc => npc.IsAlive));

            var hunger = state.Crew.Where(n => n.IsAlive).Average(n => n.Hunger);

            Assert.True(
                hunger < 80,
                $"seed {seed}: the crew went hungry (average {hunger:0}).");
        }
    }

    [Fact]
    public void TheCrewCrossTheStationAndReachTheGalley()
    {
        // Cold hallways once read as DANGER, so crew bounced back and forth
        // across every doorway at emergency urgency and never arrived anywhere.
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var visitedKitchen = false;

        var station = new Station();

        for (var block = 0; block < 24 && !visitedKitchen; block++)
        {
            station.Run(state, 60);
            visitedKitchen = state.Crew.Any(npc => npc.CurrentRoomId == "kitchen");
        }

        Assert.True(visitedKitchen, "No crew member ever reached the kitchen.");
    }

    [Fact]
    public void AnOrdinaryColdHallwayIsNotAnEmergency()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var hallway = state.Facility.Rooms["hall-kitchen"];

        hallway.TemperatureC = 12;

        Assert.False(
            CrewEnvironmentSafety.IsDangerous(hallway),
            "A chilly corridor must not trigger an evacuation.");

        // It is still not somewhere you would choose to be.
        Assert.False(CrewEnvironmentSafety.IsHabitable(hallway));
        Assert.True(CrewEnvironmentSafety.RiskScore(hallway) > 0);
    }

    [Fact]
    public void GenuineExtremesAreStillDangerous()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var room = state.Facility.Rooms["medical"];

        room.TemperatureC = 2;
        Assert.True(CrewEnvironmentSafety.IsDangerous(room));

        room.TemperatureC = 44;
        Assert.True(CrewEnvironmentSafety.IsDangerous(room));

        room.TemperatureC = 21;
        room.OxygenPercent = 15;
        Assert.True(CrewEnvironmentSafety.IsDangerous(room));
    }
}
