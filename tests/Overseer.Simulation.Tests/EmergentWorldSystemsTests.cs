using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class EmergentWorldSystemsTests
{
    [Fact]
    public void FreshSeededRoster_DefaultsToTwelveUniqueNonPrisoners()
    {
        var crew = SeededCrewRosterGenerator.Generate(912345);

        Assert.Equal(12, crew.Count);
        Assert.Equal(12, crew.Select(npc => npc.Id).Distinct().Count());
        Assert.Equal(12, crew.Select(npc => npc.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(crew, npc => npc.Role == CrewRole.Prisoner || npc.IsPrisoner);
    }

    [Fact]
    public void ScheduledSleepDebt_SlowsMovementAndReducesEffectiveSkill()
    {
        var npc = SeededCrewRosterGenerator.Generate(22, 1).Single();
        npc.Fatigue = 84;
        npc.SleepDebtMinutes = 240;

        // T+16h maps to 22:00 station local for day-shift crew.
        Assert.True(CrewDutySchedule.IsSleepWindow(npc, TimeSpan.FromHours(16)));
        Assert.True(CrewConditionRules.MovementMultiplier(npc) < 0.8);
        Assert.True(CrewConditionRules.EffectiveSkill(npc, 80) < 80);
        Assert.NotEqual("alert", CrewConditionRules.ImpairmentLabel(npc));
    }

    [Fact]
    public void RawCropConsumption_IsEmergencyFoodWithStressCost()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480041);
        var npc = state.Crew[0];

        npc.Hunger = 80;
        npc.Stress = 10;
        npc.Fatigue = 5;
        npc.Fear = 0;
        npc.HygieneNeed = 0;
        npc.RecreationNeed = 0;
        npc.SocialNeed = 0;
        npc.FoodLikes.Clear();
        npc.FoodDislikes.Clear();
        npc.FoodDislikes.Add(CropKind.Tomato);
        npc.CurrentAction = new NpcAction(ActionKind.Eat, null, "No prepared meal is available.");

        state.Stores.Meals = 0;
        foreach (var crop in Enum.GetValues<CropKind>())
            state.Stores.RawCrops[crop] = 0;
        state.Stores.RawCrops[CropKind.Tomato] = 5;

        var hungerBefore = npc.Hunger;
        var stressBefore = npc.Stress;
        var cropBefore = state.Stores.RawCrops[CropKind.Tomato];

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(npc.Hunger < hungerBefore);
        Assert.True(npc.Hunger > hungerBefore - 1, "Raw crops should relieve much less hunger than a prepared meal.");
        Assert.True(npc.Stress > stressBefore);
        Assert.True(state.Stores.RawCrops[CropKind.Tomato] < cropBefore);
        Assert.Equal("raw:Tomato", npc.CurrentAction.TargetId);
    }

    [Fact]
    public void Hydroponics_ExposeEveryRequestedCropType()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480042);
        var crops = state.CropBeds.Select(bed => bed.Crop).Distinct().OrderBy(crop => crop).ToArray();

        Assert.Equal(Enum.GetValues<CropKind>().OrderBy(crop => crop).ToArray(), crops);
        Assert.Equal(7, state.CropBeds.Count(bed => bed.RoomId == "hydroponics"));
    }

    [Fact]
    public void FireResponse_IsDeterministicWorldValidationRatherThanScriptedOutcome()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew.First(crew => crew.Role == CrewRole.Engineer);

        npc.CurrentRoomId = room.Id;
        npc.Fatigue = 5;
        npc.SleepDebtMinutes = 0;
        npc.Skills["Engineering"] = 80;
        room.FireIntensity = 60;
        room.SmokePercent = 30;

        var before = room.FireIntensity;
        var result = StationHazardSystem.TryExecuteCrewAction(
            state,
            npc,
            ActionKind.FightFire,
            room,
            out var message);

        Assert.True(result);
        Assert.True(room.FireIntensity < before);
        Assert.Contains(npc.Name, message);
        Assert.Contains(state.EventLog, entry =>
            entry.Contains("fire", StringComparison.OrdinalIgnoreCase)
            && entry.Contains(npc.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fire_ImmediatelyExtinguishesWithoutOxygenBeforeFurtherDamageOrHeat()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var room = state.Facility.Rooms["engineering"];
        room.FireIntensity = 60;
        room.OxygenPercent = 0;
        room.TemperatureC = 42;
        room.SmokePercent = 25;
        room.HullIntegrityPercent = 80;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(0, room.FireIntensity);
        Assert.Equal(42, room.TemperatureC);
        Assert.Equal(80, room.HullIntegrityPercent);
        Assert.Contains(state.EventLog, entry =>
            entry.Contains("oxygen starvation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fire_DecaysFasterAsOxygenFalls()
    {
        var oxygenRich = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var oxygenPoor = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var richRoom = oxygenRich.Facility.Rooms["engineering"];
        var poorRoom = oxygenPoor.Facility.Rooms["engineering"];

        richRoom.FireIntensity = 60;
        poorRoom.FireIntensity = 60;
        richRoom.OxygenPercent = 17;
        poorRoom.OxygenPercent = 5;

        new StationHazardSystem().Tick(oxygenRich, TimeSpan.FromMinutes(1));
        new StationHazardSystem().Tick(oxygenPoor, TimeSpan.FromMinutes(1));

        Assert.True(richRoom.FireIntensity < 60);
        Assert.True(poorRoom.FireIntensity < richRoom.FireIntensity);
    }

    [Fact]
    public void LifecycleAudit_KeepsOrdinaryBodiesAndExplainsSilentLateDeaths()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480044);
        var casualty = state.Crew[0];

        casualty.Health = 0;
        casualty.CauseOfDeath = null;
        casualty.IsPresent = false;

        new CrewLifecycleAuditSystem().Tick(state);

        Assert.False(casualty.IsAlive);
        Assert.True(casualty.IsPresent);
        Assert.False(string.IsNullOrWhiteSpace(casualty.CauseOfDeath));
        Assert.NotNull(casualty.LastDeathAnnouncementAt);
        Assert.Contains(state.EventLog, entry =>
            entry.Contains(casualty.Name, StringComparison.OrdinalIgnoreCase)
            && entry.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LifecycleAudit_RepairsImpossibleLivingDisappearances()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480045);
        var crew = state.Crew[0];

        crew.IsPresent = false;
        Assert.True(crew.IsAlive);

        new CrewLifecycleAuditSystem().Tick(state);

        Assert.True(crew.IsPresent);
        Assert.Contains(state.EventLog, entry =>
            entry.Contains("presence audit restored", StringComparison.OrdinalIgnoreCase)
            && entry.Contains(crew.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContainmentScenario_AddsFourDifferentDangerTiersToTwelveCrew()
    {
        var baseCrew = SeededCrewRosterGenerator.Generate(480046);
        var composed = PrisonerRosterSystem.Compose(baseCrew, ScenarioCatalog.ContainmentTransfer);
        var prisoners = composed.Where(npc => npc.IsPrisoner).ToList();

        Assert.Equal(16, composed.Count);
        Assert.Equal(4, prisoners.Count);
        Assert.Equal(
            Enum.GetValues<PrisonerDangerLevel>().OrderBy(level => level).ToArray(),
            prisoners.Select(npc => npc.PrisonerDangerLevel).OrderBy(level => level).ToArray());
        Assert.All(prisoners, npc =>
        {
            Assert.Equal(CrewRole.Prisoner, npc.Role);
            Assert.Equal("containment", npc.CurrentRoomId);
        });

        Assert.Contains(
            "containment",
            ScenarioCatalog.ContainmentTransfer.StationConstraints!.RequiredRoomIds);
    }

    [Fact]
    public void ContainmentConstraints_CanGenerateARealSecureCompartment()
    {
        StationGenerationResult? result = null;

        for (var seed = 480_100; seed < 480_180 && result is null; seed++)
        {
            try
            {
                result = StationGenerator.Generate(
                    seed,
                    ScenarioCatalog.ContainmentTransfer.StationConstraints!);
            }
            catch (StationGenerationException)
            {
                // Spatial packing is allowed to reject individual seeds.
            }
        }

        Assert.NotNull(result);
        Assert.True(result!.Facility.Rooms.TryGetValue("containment", out var room));
        Assert.Equal(RoomType.Containment, room!.Type);
        Assert.Empty(StationGenerator.Validate(
            result.Facility,
            ScenarioCatalog.ContainmentTransfer.StationConstraints));
    }

    [Fact]
    public void GeneratedRoomStatusPlates_HaveFixedGenerationOwnedSides()
    {
        var result = StationGenerator.Generate(
            480047,
            ScenarioCatalog.SecureContinuity.StationConstraints!);
        var callouts = StationRoomCalloutSystem.Build(result.Facility)
            .ToDictionary(callout => callout.RoomId, StringComparer.OrdinalIgnoreCase);

        foreach (var room in result.Facility.Rooms.Values.Where(room => room.Type != RoomType.Corridor))
        {
            Assert.NotNull(room.StatusPlateSide);
            Assert.Equal(
                room.StatusPlateSide == RoomStatusPlateSide.Top ? "top" : "bottom",
                callouts[room.Id].Side);
        }

        Assert.Empty(StationGenerator.Validate(
            result.Facility,
            ScenarioCatalog.SecureContinuity.StationConstraints));
    }

    [Fact]
    public void CombinedConflictPressure_CanEscalateWithoutOldExtremeThresholds()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480048);
        var aggressor = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var target = state.Crew.Single(npc => npc.Name == "Emma Voss");

        foreach (var other in state.Crew.Where(npc => npc.Id != aggressor.Id && npc.Id != target.Id))
            other.Health = 0;

        aggressor.CurrentRoomId = "storage";
        target.CurrentRoomId = "storage";
        aggressor.Stress = 72; // below old 78 hard gate
        aggressor.Fatigue = 88;
        aggressor.Hunger = 90;
        aggressor.Fear = 75;
        aggressor.IsPrisoner = true;
        aggressor.PrisonerDangerLevel = PrisonerDangerLevel.Extreme;
        aggressor.PrisonerViolenceBias = 24;
        aggressor.CurrentAction = new NpcAction(ActionKind.Argue, target.Name, "Argument escalating.");

        var relationship = aggressor.Relationships[target.Name];
        relationship.Resentment = 66; // below old 82 hard gate
        relationship.Trust = 8;

        var system = new SocialSimulationSystem();
        var attacked = false;

        for (var minute = 1; minute <= 600 && !attacked; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            aggressor.RoutineUntil = TimeSpan.Zero;
            target.RoutineUntil = TimeSpan.Zero;
            aggressor.CurrentAction = new NpcAction(ActionKind.Argue, target.Name, "Argument escalating.");
            system.Tick(state);
            attacked = state.EventLog.Any(entry =>
                entry.Contains(aggressor.Name, StringComparison.OrdinalIgnoreCase)
                && entry.Contains("attacks", StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(attacked, "Combined stress/personality/relationship/circumstance pressure should be able to trigger a spontaneous fight.");
    }

    [Fact]
    public void SharedConsole_ExposesRoutesRecentAlertsCropsAndDarkSpacePresentation()
    {
        var root = FindRepositoryRoot();
        var razor = File.ReadAllText(Path.Combine(root, "src/Overseer.Web.UI/Pages/Home.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src/Overseer.Web.UI/Pages/Home.razor.css"));

        Assert.Contains("selected-route-path", razor);
        Assert.Contains("PlannedDestinationRoomId", razor);
        Assert.Contains("Session.RecentAlerts", razor);
        Assert.Contains("InspectAlertAsync", razor);
        Assert.Contains("data-room-id", razor);
        Assert.Contains("data-device-id", razor);
        Assert.Contains("data-crop", razor);
        Assert.Contains("CrewConditionRules.ImpairmentLabel", razor);

        Assert.Contains("xlarge_web_0-jpg.webp", css);
        Assert.Contains("station-space-twinkle", css);
        Assert.Contains("selected-route-destination", css);
        Assert.Contains(".robot-token:not(.turret-token).selected::after", css);
        Assert.Contains(".room-node[data-room-id].has-fire::after", css);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Overseer.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Overseer.slnx from test output.");
    }
}
