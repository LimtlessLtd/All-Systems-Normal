using System.Globalization;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ProceduralStationGenerationTests
{
    [Fact]
    public void SameSeedAndConstraints_ProduceEquivalentStation()
    {
        const int seed = 24681357;
        var constraints = ScenarioCatalog.SecureContinuity.StationConstraints!;

        var first = StationGenerator.Generate(seed, constraints);
        var second = StationGenerator.Generate(seed, constraints);

        Assert.Equal(Signature(first), Signature(second));
        Assert.Equal(first.Metadata.Identity, second.Metadata.Identity);
        Assert.Equal(first.Metadata.Archetype, second.Metadata.Archetype);

        var firstState = FacilitySeeder.CreateDefault(stationSeed: seed);
        var secondState = FacilitySeeder.CreateDefault(stationSeed: seed);
        Assert.Equal(firstState.UpkeepSeed, secondState.UpkeepSeed);
    }

    [Fact]
    public void DifferentSeeds_CreateMeaningfullyDifferentFacilities()
    {
        var constraints = ScenarioCatalog.SecureContinuity.StationConstraints!;
        var generated = Enumerable.Range(1, 20)
            .Select(seed => StationGenerator.Generate(seed * 7919, constraints))
            .ToList();

        var signatures = generated.Select(Signature).Distinct().Count();
        var archetypes = generated.Select(result => result.Metadata.Archetype).Distinct().Count();
        var corridorCounts = generated
            .Select(result => result.Facility.Rooms.Values.Count(room =>
                room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .Count();

        Assert.True(signatures >= 18, $"Only {signatures}/20 station signatures were distinct.");
        Assert.True(archetypes >= 4, $"Only {archetypes} station archetypes appeared.");
        Assert.True(corridorCounts >= 3, $"Only {corridorCounts} topology sizes appeared.");
    }

    [Fact]
    public void ManySeeds_SatisfyPhysicalAndReachabilityInvariants()
    {
        var navigation = new NavigationSystem();
        var constraints = ScenarioCatalog.SecureContinuity.StationConstraints!;

        for (var seed = 0; seed < 48; seed++)
        {
            var result = StationGenerator.Generate(100_000 + seed, constraints);
            var facility = result.Facility;

            Assert.Empty(StationGenerator.Validate(facility, constraints));

            foreach (var requiredId in new[]
                     {
                         "quarters", "kitchen", "lounge", "hydroponics", "medical",
                         "control", "washroom", "storage", "engineering", "generator",
                         "reactor", "airlock", "isolation"
                     })
            {
                Assert.True(
                    facility.Rooms.ContainsKey(requiredId),
                    $"Seed {seed}: required canonical room {requiredId} is missing.");
            }

            foreach (var room in facility.Rooms.Values.Where(room => room.Type != RoomType.Corridor))
            {
                var path = navigation.FindPathIgnoringDoorState(facility, room.Id, "corridor");
                Assert.NotEmpty(path);
                Assert.Equal(room.Id, path[0]);
                Assert.Equal("corridor", path[^1]);
            }

            foreach (var door in facility.Doors)
            {
                _ = StationGeometry.FindSharedPortal(
                    facility.Rooms[door.RoomAId],
                    facility.Rooms[door.RoomBId]);
            }
        }
    }

    [Fact]
    public void CampaignConstraints_OverrideProceduralPreferencesAndPlaceSecurity()
    {
        var constraints = new StationGenerationConstraints
        {
            ForcedArchetype = StationArchetype.Ring,
            ForcedPurpose = StationPurpose.Security,
            ForcedBudget = StationBudgetClass.Premium,
            ForcedSize = StationSizeClass.Large,
            ForcedExpansionHistory = StationExpansionHistory.PurposeBuilt,
            ForcedSecurityLevel = 96,
            RequiredAirlockCount = 2,
            RequiredRobotCount = 2,
            RequiredTurretCount = 2,
            RequireRedundantPaths = true,
            ReactorMustBeIsolated = true,
            MedicalMustBeNearHabitat = true,
            RequiredShutdownRoomId = "isolation"
        };

        constraints.RequiredRoomIds.Add("isolation");
        constraints.RequiredAirlockRoomIds.Add("airlock");
        constraints.RequiredRobotRoomIds.Add("engineering");
        constraints.RequiredRobotRoomIds.Add("control");
        constraints.RequiredTurretRoomIds.Add("corridor");
        constraints.RequiredTurretRoomIds.Add("control");
        constraints.InitiallyInaccessibleRoomIds.Add("isolation");
        constraints.RequiredAdjacency.Add(new StationAdjacencyConstraint("medical", "quarters"));
        constraints.RequiredSeparation.Add(new StationSeparationConstraint("reactor", "quarters", 24));
        constraints.EnvironmentOverrides["hydroponics"] = new StationRoomEnvironmentOverride
        {
            IsPowered = false,
            CameraOnline = false,
            TemperatureC = 26
        };

        foreach (var seed in new[] { 5, 17, 42, 99, 12345 })
        {
            var state = FacilitySeeder.CreateDefault(
                stationSeed: seed,
                stationConstraints: constraints);

            Assert.NotNull(state.StationGeneration);
            Assert.Equal(StationArchetype.Ring, state.StationGeneration!.Archetype);
            Assert.Equal(StationPurpose.Security, state.StationGeneration.Identity.Purpose);
            Assert.Equal(StationBudgetClass.Premium, state.StationGeneration.Identity.Budget);
            Assert.Equal(StationSizeClass.Large, state.StationGeneration.Identity.Size);
            Assert.Equal(StationExpansionHistory.PurposeBuilt, state.StationGeneration.Identity.ExpansionHistory);
            Assert.Equal(96, state.StationGeneration.Identity.SecurityLevel);
            Assert.True(state.StationGeneration.CampaignOverridesApplied);

            Assert.Equal(2, state.Facility.Rooms.Values.Count(room => room.Type == RoomType.Airlock));
            Assert.Equal(2, state.Robots.Count);
            Assert.Equal(
                new[] { "control", "engineering" },
                state.Robots.Select(robot => robot.CurrentRoomId).OrderBy(id => id).ToArray());
            Assert.Equal(2, state.Turrets.Count);
            Assert.Equal(
                new[] { "control", "corridor" },
                state.Turrets.Select(turret => turret.RoomId).OrderBy(id => id).ToArray());

            Assert.All(
                state.Facility.Doors.Where(door =>
                    door.RoomAId == "isolation" || door.RoomBId == "isolation"),
                door => Assert.False(door.IsPassable));

            var hydroponics = state.Facility.Rooms["hydroponics"];
            Assert.False(hydroponics.IsPowered);
            Assert.False(hydroponics.CameraOnline);
            Assert.Equal(26, hydroponics.TemperatureC);
        }
    }

    [Fact]
    public void CampaignCanForbidRoomsAndAddProceduralSetPieces()
    {
        var constraints = new StationGenerationConstraints
        {
            ForcedArchetype = StationArchetype.Retrofit,
            MinimumFunctionalRoomCount = 13
        };

        constraints.ForbiddenRoomIds.Add("lounge");
        constraints.AuthoredRooms.Add(
            new StationRoomDefinition(
                "xeno-lab",
                "Retrofitted Xenobiology Lab",
                RoomType.Medical,
                10,
                15,
                12,
                18));
        constraints.RequiredRoomIds.Add("xeno-lab");

        var result = StationGenerator.Generate(880055, constraints);

        Assert.False(result.Facility.Rooms.ContainsKey("lounge"));
        Assert.True(result.Facility.Rooms.ContainsKey("xeno-lab"));
        Assert.True(result.Facility.Rooms.ContainsKey("hall-xeno-lab"));
        Assert.Empty(StationGenerator.Validate(result.Facility, constraints));
    }

    [Fact]
    public void FullyAuthoredGeometry_UsesTheSameFacilityAndNavigationModel()
    {
        var constraints = new StationGenerationConstraints
        {
            FullyAuthoredGeometry = true,
            RequiredAirlockCount = 1
        };

        constraints.FixedRooms.Add(new StationAuthoredGeometryRoom(
            "corridor", "Authored Spine", RoomType.Corridor, 50, 50, 40, 6));
        constraints.FixedRooms.Add(new StationAuthoredGeometryRoom(
            "lab", "Authored Lab", RoomType.Medical, 50, 38, 20, 18));
        constraints.FixedRooms.Add(new StationAuthoredGeometryRoom(
            "airlock", "Authored Airlock", RoomType.Airlock, 24, 50, 12, 10));
        constraints.FixedConnections.Add(new StationAuthoredConnection("lab", "corridor"));
        constraints.FixedConnections.Add(new StationAuthoredConnection("airlock", "corridor"));

        var result = StationGenerator.Generate(123, constraints);
        var path = new NavigationSystem().FindPathIgnoringDoorState(
            result.Facility,
            "lab",
            "airlock");

        Assert.Equal(3, result.Facility.Rooms.Count);
        Assert.Equal(new[] { "lab", "corridor", "airlock" }, path);
        Assert.Empty(StationGenerator.Validate(result.Facility, constraints));
    }

    [Fact]
    public void StationIdentityMateriallyChangesSecurityAndInteriorCharacter()
    {
        var purposeBuilt = new StationGenerationConstraints
        {
            ForcedArchetype = StationArchetype.Linear,
            ForcedPurpose = StationPurpose.Research,
            ForcedExpansionHistory = StationExpansionHistory.PurposeBuilt,
            ForcedSecurityLevel = 20
        };

        var retrofit = new StationGenerationConstraints
        {
            ForcedArchetype = StationArchetype.Linear,
            ForcedPurpose = StationPurpose.Industrial,
            ForcedExpansionHistory = StationExpansionHistory.HeavilyRetrofitted,
            ForcedSecurityLevel = 90
        };

        var clean = FacilitySeeder.CreateDefault(
            stationSeed: 424242,
            stationConstraints: purposeBuilt);
        var hardened = FacilitySeeder.CreateDefault(
            stationSeed: 424242,
            stationConstraints: retrofit);

        Assert.True(
            hardened.Facility.Doors.Average(door => door.TechnicalDifficulty)
            > clean.Facility.Doors.Average(door => door.TechnicalDifficulty));

        var cleanGeneratedDetails = clean.Facility.Rooms.Values
            .SelectMany(room => room.Fixtures)
            .Count(fixture => fixture.Label.StartsWith("Generated ", StringComparison.Ordinal));
        var retrofitGeneratedDetails = hardened.Facility.Rooms.Values
            .SelectMany(room => room.Fixtures)
            .Count(fixture => fixture.Label.StartsWith("Generated ", StringComparison.Ordinal));

        Assert.True(retrofitGeneratedDetails > cleanGeneratedDetails);
        Assert.Contains(
            hardened.Facility.Rooms.Values.SelectMany(room => room.Fixtures),
            fixture => fixture.Label == "Security hardline");
    }

    [Fact]
    public void HumanAndRobotNavigation_RemainsValidAcrossGeneratedGeometry()
    {
        var navigation = new NavigationSystem();

        foreach (var seed in Enumerable.Range(2000, 24))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var npc in state.Crew)
            {
                Assert.True(state.Facility.Rooms.ContainsKey(npc.CurrentRoomId));
                Assert.NotEmpty(navigation.FindPathIgnoringDoorState(
                    state.Facility,
                    npc.CurrentRoomId,
                    "reactor"));
            }

            foreach (var robot in state.Robots)
            {
                Assert.True(state.Facility.Rooms.ContainsKey(robot.CurrentRoomId));
                Assert.NotEmpty(navigation.FindPathIgnoringDoorState(
                    state.Facility,
                    robot.CurrentRoomId,
                    "control"));
            }
        }
    }

    [Fact]
    public void PresentationProfile_UsesIdentityAndFitsManyProceduralShapes()
    {
        foreach (var seed in Enumerable.Range(0, 32).Select(index => 310_000 + index))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);
            var profile = StationPresentationSystem.Build(state);

            Assert.InRange(profile.FitScale, 0.84, 1.18);
            Assert.InRange(profile.FitOffsetX, -10, 10);
            Assert.InRange(profile.FitOffsetY, -10, 10);
            Assert.Contains("purpose-", profile.CssClasses);
            Assert.Contains("budget-", profile.CssClasses);
            Assert.Contains("expansion-", profile.CssClasses);
            Assert.Contains("hull-variant-", profile.CssClasses);
        }
    }

    [Fact]
    public void StationIdentity_ProducesDistinctPresentationCharacter()
    {
        var cleanResearch = new StationGenerationConstraints
        {
            ForcedPurpose = StationPurpose.Research,
            ForcedBudget = StationBudgetClass.Premium,
            ForcedExpansionHistory = StationExpansionHistory.PurposeBuilt,
            ForcedSecurityLevel = 35
        };

        var wornIndustrial = new StationGenerationConstraints
        {
            ForcedPurpose = StationPurpose.Industrial,
            ForcedBudget = StationBudgetClass.Frugal,
            ForcedExpansionHistory = StationExpansionHistory.HeavilyRetrofitted,
            ForcedSecurityLevel = 75
        };

        var clean = FacilitySeeder.CreateDefault(
            stationSeed: 64021,
            stationConstraints: cleanResearch);
        var worn = FacilitySeeder.CreateDefault(
            stationSeed: 64021,
            stationConstraints: wornIndustrial);

        var cleanProfile = StationPresentationSystem.Build(clean);
        var wornProfile = StationPresentationSystem.Build(worn);

        Assert.Equal("purpose-research", cleanProfile.PurposeClass);
        Assert.Equal("budget-premium", cleanProfile.BudgetClass);
        Assert.Equal("expansion-purpose-built", cleanProfile.ExpansionClass);

        Assert.Equal("purpose-industrial", wornProfile.PurposeClass);
        Assert.Equal("budget-frugal", wornProfile.BudgetClass);
        Assert.Equal("expansion-heavily-retrofitted", wornProfile.ExpansionClass);

        Assert.NotEqual(cleanProfile.CssClasses, wornProfile.CssClasses);
    }

    [Fact]
    public void FixtureDetails_RemainInsideOwningRoomAcrossManySeeds()
    {
        foreach (var seed in Enumerable.Range(0, 32).Select(index => 720_000 + index))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var room in state.Facility.Rooms.Values)
            {
                foreach (var fixture in room.Fixtures)
                {
                    Assert.True(fixture.Width > 0, $"Seed {seed}, {room.Id}: {fixture.Label} has invalid width.");
                    Assert.True(fixture.Height > 0, $"Seed {seed}, {room.Id}: {fixture.Label} has invalid height.");

                    Assert.InRange(
                        fixture.X - (fixture.Width / 2),
                        0,
                        100);
                    Assert.InRange(
                        fixture.X + (fixture.Width / 2),
                        0,
                        100);
                    Assert.InRange(
                        fixture.Y - (fixture.Height / 2),
                        0,
                        100);
                    Assert.InRange(
                        fixture.Y + (fixture.Height / 2),
                        0,
                        100);
                }
            }
        }
    }

    [Fact]
    public void PresentationProfile_DoesNotAutoFitLargePannableDeck()
    {
        var facility = new Facility();
        facility.Rooms["alpha"] = new Room
        {
            Id = "alpha",
            Name = "Alpha",
            Type = RoomType.ControlRoom,
            MapX = 18,
            MapY = 22,
            MapWidth = 12,
            MapHeight = 14
        };
        facility.Rooms["beta"] = new Room
        {
            Id = "beta",
            Name = "Beta",
            Type = RoomType.Engineering,
            MapX = 34,
            MapY = 25,
            MapWidth = 16,
            MapHeight = 18
        };

        var profile = StationPresentationSystem.Build(facility, metadata: null);

        Assert.Equal(1, profile.FitScale);
        Assert.Equal(0, profile.FitOffsetX);
        Assert.Equal(0, profile.FitOffsetY);
    }

    [Fact]
    public void GeneratedWallDetails_HugRealBulkheadsAcrossProceduralSeeds()
    {
        var wallMountedTypes = new[]
        {
            FixtureType.Pipe,
            FixtureType.Window,
            FixtureType.Vent,
            FixtureType.Screen,
            FixtureType.UtilityPanel
        };

        foreach (var seed in Enumerable.Range(0, 24).Select(index => 830_000 + index))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var room in state.Facility.Rooms.Values)
            {
                foreach (var fixture in room.Fixtures.Where(fixture =>
                             fixture.Label.StartsWith("Generated ", StringComparison.Ordinal)
                             && wallMountedTypes.Contains(fixture.Type)))
                {
                    var edgeDistance = new[]
                    {
                        fixture.X - (fixture.Width / 2),
                        100 - (fixture.X + (fixture.Width / 2)),
                        fixture.Y - (fixture.Height / 2),
                        100 - (fixture.Y + (fixture.Height / 2))
                    }.Min();

                    Assert.InRange(
                        edgeDistance,
                        2.99,
                        3.01);
                }
            }
        }
    }

    [Fact]
    public void GeneratedFunctionalRoomsMeetLargeDeckPresentationMinimum()
    {
        foreach (var seed in Enumerable.Range(0, 20).Select(index => 910_000 + index))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);

            foreach (var room in state.Facility.Rooms.Values.Where(room => room.Type != RoomType.Corridor))
            {
                Assert.True(
                    room.MapWidth >= 8.0,
                    $"Seed {seed}, {room.Id}: width {room.MapWidth:0.###}% renders below the 200px minimum.");
                Assert.True(
                    room.MapHeight >= 9.0,
                    $"Seed {seed}, {room.Id}: height {room.MapHeight:0.###}% renders below the 200px minimum.");
            }
        }
    }

    [Fact]
    public void RoomStatusCalloutsStayAttachedToOwningRoomEdgesAcrossSeeds()
    {
        foreach (var seed in Enumerable.Range(0, 16).Select(index => 920_000 + index))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);
            var callouts = StationRoomCalloutSystem.Build(state.Facility);
            var functional = state.Facility.Rooms.Values
                .Where(room => room.Type != RoomType.Corridor)
                .ToList();

            Assert.Equal(functional.Count, callouts.Count);

            foreach (var callout in callouts)
            {
                var room = state.Facility.Rooms[callout.RoomId];
                Assert.False(callout.IsExternal);
                Assert.Contains(callout.Side, new[] { "top", "bottom" });
                Assert.Equal(StationRoomCalloutSystem.ToDeck(room.MapX), callout.AnchorX, 6);
                Assert.Equal(callout.AnchorX, callout.LabelX, 6);

                var expectedEdgeY = StationRoomCalloutSystem.ToDeck(
                    callout.Side == "top"
                        ? room.MapY - (room.MapHeight / 2)
                        : room.MapY + (room.MapHeight / 2));

                Assert.Equal(expectedEdgeY, callout.AnchorY, 6);
                Assert.InRange(Math.Abs(callout.LabelY - callout.AnchorY), .8, 2.5);
            }
        }
    }

    private static string Signature(StationGenerationResult result)
    {
        var roomSignature = string.Join(
            "|",
            result.Facility.Rooms.Values
                .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase)
                .Select(room => string.Join(
                    ":",
                    room.Id,
                    room.Type,
                    F(room.MapX),
                    F(room.MapY),
                    F(room.MapWidth),
                    F(room.MapHeight))));

        var doorSignature = string.Join(
            "|",
            result.Facility.Doors
                .Select(door => string.Compare(
                        door.RoomAId,
                        door.RoomBId,
                        StringComparison.OrdinalIgnoreCase) <= 0
                    ? $"{door.RoomAId}>{door.RoomBId}"
                    : $"{door.RoomBId}>{door.RoomAId}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        return $"{result.Metadata.Archetype}::{result.Metadata.Identity}::{roomSignature}::{doorSignature}";
    }

    private static string F(double value) =>
        value.ToString("0.000", CultureInfo.InvariantCulture);
}
