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
