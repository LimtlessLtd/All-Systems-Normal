using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class StationGenerationException : InvalidOperationException
{
    public StationGenerationException(string message, IReadOnlyList<string> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed class StationGenerationResult
{
    public required Facility Facility { get; init; }
    public required StationGenerationMetadata Metadata { get; init; }
}

/// <summary>
/// Deterministic seeded physical-station generation. The pipeline is deliberately
/// split into identity, topology, requirements, packing, connectors, state
/// overrides and validation so campaign constraints remain a hard layer above
/// procedural preferences.
/// </summary>
public static class StationGenerator
{
    private const double CanvasMin = 2;
    private const double CanvasMax = 98;
    private const double OverlapTolerance = 0.000001;
    private const double StatusPlateReserveHeight = 2.4;
    private const double StatusPlateReserveWidth = 10.4;

    private sealed record RoomProfile(
        string Id,
        string Name,
        RoomType Type,
        double MinWidth,
        double MaxWidth,
        double MinHeight,
        double MaxHeight,
        int Importance = 50);

    private sealed record PlacementInfo(
        string RoomId,
        string CorridorId,
        double X,
        double Y);

    private sealed record PlacementCandidate(
        Room Room,
        Room Hallway,
        string CorridorId,
        double Score);

    private enum AttachmentSide
    {
        North,
        South,
        East,
        West
    }

    private static readonly IReadOnlyDictionary<string, RoomProfile> CanonicalProfiles =
        new Dictionary<string, RoomProfile>(StringComparer.OrdinalIgnoreCase)
        {
            // Preserve the proven minimum footprints so crowded deterministic
            // seeds remain packable, while lifting maxima so ordinary stations
            // trend larger and less fixture-dense than before.
            ["quarters"] = new("quarters", "Crew Quarters", RoomType.CrewQuarters, 12, 20, 14, 23, 100),
            ["kitchen"] = new("kitchen", "Kitchen", RoomType.Kitchen, 9, 15, 11, 18, 70),
            ["lounge"] = new("lounge", "Recreation Lounge", RoomType.Recreation, 10, 18, 12, 21, 55),
            ["hydroponics"] = new("hydroponics", "Hydroponics Bay", RoomType.Hydroponics, 11, 20, 14, 24, 70),
            ["medical"] = new("medical", "Medical", RoomType.Medical, 9, 16, 11, 19, 80),
            ["control"] = new("control", "Control Room", RoomType.ControlRoom, 11, 19, 12, 21, 100),
            ["washroom"] = new("washroom", "Washroom", RoomType.Washroom, 8, 13, 10, 17, 45),
            ["storage"] = new("storage", "Storage", RoomType.Storage, 9, 17, 11, 21, 55),
            ["engineering"] = new("engineering", "Engineering", RoomType.Engineering, 12, 21, 14, 24, 100),
            ["generator"] = new("generator", "Generator", RoomType.Generator, 11, 19, 14, 23, 90),
            ["reactor"] = new("reactor", "Reactor", RoomType.Reactor, 14, 24, 16, 28, 100),
            ["airlock"] = new("airlock", "Airlock", RoomType.Airlock, 8, 12, 10, 14, 100),
            ["containment"] = new("containment", "Secure Containment", RoomType.Containment, 14, 22, 16, 24, 90),
            ["isolation"] = new("isolation", "Overseer Isolation", RoomType.ControlRoom, 8, 12, 10, 16, 100)
        };

    public static StationGenerationResult Generate(
        int seed,
        StationGenerationConstraints? campaignConstraints = null)
    {
        var constraints = campaignConstraints ?? new StationGenerationConstraints();
        ValidateConstraintDefinition(constraints);

        var identityRandom = new SeededRandom(MixSeed(seed, 0x51A7));
        var identity = CreateIdentity(identityRandom, constraints);
        var archetype = ChooseArchetype(identityRandom, identity, constraints);
        var requirements = BuildRoomRequirements(identity, constraints);
        var diagnostics = new List<string>
        {
            $"seed={seed}",
            $"identity={identity.Purpose}/{identity.Size}/{identity.Budget}, age={identity.AgeYears}, expansion={identity.ExpansionHistory}",
            $"archetype={archetype}",
            $"requirements={requirements.Count} functional rooms"
        };

        IReadOnlyList<string> lastErrors = [];

        // Keep the first 24 attempts identical to the established generator,
        // then continue deterministic retries for unusually crowded seeds.
        // This improves robustness without weakening any geometry validation or
        // changing successful seeds that already pack inside the original budget.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var random = new SeededRandom(MixSeed(seed, 0x7001 + attempt));
            Facility facility;

            try
            {
                facility = constraints.FullyAuthoredGeometry
                    ? BuildFullyAuthored(constraints)
                    : BuildProceduralFacility(random, identity, archetype, requirements, constraints);
            }
            catch (InvalidOperationException ex)
                when (!constraints.FullyAuthoredGeometry && ex is not StationGenerationException)
            {
                lastErrors = [$"Packing attempt {attempt + 1}: {ex.Message}"];
                continue;
            }

            ApplyInitialDoorConstraints(facility, constraints);

            var errors = Validate(
                facility,
                constraints,
                requirements,
                requirePlayableDefault: !constraints.FullyAuthoredGeometry);

            if (errors.Count == 0)
            {
                var metadata = new StationGenerationMetadata
                {
                    Seed = seed,
                    Archetype = archetype,
                    Identity = identity,
                    CampaignOverridesApplied = HasCampaignOverrides(constraints)
                };
                metadata.Diagnostics.AddRange(diagnostics);
                metadata.Diagnostics.Add($"packing-attempt={attempt + 1}");
                metadata.Diagnostics.Add($"rooms={facility.Rooms.Count}, doors={facility.Doors.Count}");
                metadata.Diagnostics.Add($"corridor-cycle={HasCorridorCycle(facility)}, chokepoints={CountCorridorArticulationPoints(facility)}");

                return new StationGenerationResult
                {
                    Facility = facility,
                    Metadata = metadata
                };
            }

            lastErrors = errors;
        }

        diagnostics.AddRange(lastErrors.Select(error => $"FAILED: {error}"));
        throw new StationGenerationException(
            $"Station generation failed for seed {seed}: {string.Join("; ", lastErrors)}",
            diagnostics);
    }

    public static void ApplyEnvironmentOverrides(
        Facility facility,
        StationGenerationConstraints? constraints)
    {
        if (constraints is null)
        {
            return;
        }

        foreach (var (roomId, roomOverride) in constraints.EnvironmentOverrides)
        {
            if (!facility.Rooms.TryGetValue(roomId, out var room))
            {
                continue;
            }

            if (roomOverride.IsPowered is { } powered) room.IsPowered = powered;
            if (roomOverride.CameraOnline is { } camera) room.CameraOnline = camera;
            if (roomOverride.LightsOn is { } lights) room.LightsOn = lights;
            if (roomOverride.TemperatureC is { } temperature)
            {
                room.TemperatureC = temperature;
                room.TemperatureSetpointC = temperature;
            }
            if (roomOverride.OxygenPercent is { } oxygen) room.OxygenPercent = oxygen;
            if (roomOverride.CarbonDioxidePercent is { } carbonDioxide) room.CarbonDioxidePercent = carbonDioxide;
            if (roomOverride.PressureKpa is { } pressure) room.PressureKpa = pressure;
            if (roomOverride.VentilationEnabled is { } ventilation) room.VentilationEnabled = ventilation;
            if (roomOverride.HasTemperatureControl is { } hasTemperature) room.HasTemperatureControl = hasTemperature;
            if (roomOverride.IsTemperatureAiControllable is { } tempAi) room.IsTemperatureAiControllable = tempAi;
            if (roomOverride.HasVentilationControl is { } hasVentilation) room.HasVentilationControl = hasVentilation;
            if (roomOverride.IsVentilationAiControllable is { } ventAi) room.IsVentilationAiControllable = ventAi;
        }
    }

    public static IReadOnlyList<string> Validate(
        Facility facility,
        StationGenerationConstraints? constraints = null)
    {
        var requirementProfiles = BuildRoomRequirements(
            new StationIdentity(
                StationPurpose.Mixed,
                20,
                StationBudgetClass.Standard,
                StationSizeClass.Standard,
                12,
                50,
                50,
                70,
                StationExpansionHistory.LightlyExpanded),
            constraints ?? new StationGenerationConstraints());

        return Validate(
            facility,
            constraints ?? new StationGenerationConstraints(),
            requirementProfiles,
            requirePlayableDefault: false);
    }

    private static Facility BuildProceduralFacility(
        SeededRandom random,
        StationIdentity identity,
        StationArchetype archetype,
        IReadOnlyList<RoomProfile> requirements,
        StationGenerationConstraints constraints)
    {
        var facility = new Facility();
        var thickness = CorridorThickness(identity, random);

        BuildTopology(facility, random, identity, archetype, thickness);
        AutoConnectCorridors(facility);

        var placements = new Dictionary<string, PlacementInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in OrderRequirements(requirements, constraints))
        {
            PlaceFunctionalRoom(
                facility,
                random,
                identity,
                profile,
                constraints,
                placements);
        }

        return facility;
    }

    private static Facility BuildFullyAuthored(StationGenerationConstraints constraints)
    {
        if (constraints.FixedRooms.Count == 0)
        {
            throw new StationGenerationException(
                "Fully authored station generation requires FixedRooms.",
                ["FixedRooms was empty."]);
        }

        var facility = new Facility();

        foreach (var authored in constraints.FixedRooms)
        {
            if (facility.Rooms.ContainsKey(authored.Id))
            {
                throw new StationGenerationException(
                    $"Duplicate authored room id '{authored.Id}'.",
                    [$"Duplicate authored room id '{authored.Id}'."]);
            }

            facility.Rooms.Add(authored.Id, new Room
            {
                Id = authored.Id,
                Name = authored.Name,
                Type = authored.Type,
                MapX = authored.MapX,
                MapY = authored.MapY,
                MapWidth = authored.MapWidth,
                MapHeight = authored.MapHeight
            });
        }

        foreach (var connection in constraints.FixedConnections)
        {
            if (!facility.Rooms.ContainsKey(connection.FirstRoomId)
                || !facility.Rooms.ContainsKey(connection.SecondRoomId))
            {
                throw new StationGenerationException(
                    "Authored connection references a missing room.",
                    [$"{connection.FirstRoomId}<->{connection.SecondRoomId} references a missing room."]);
            }

            _ = StationGeometry.FindSharedPortal(
                facility.Rooms[connection.FirstRoomId],
                facility.Rooms[connection.SecondRoomId]);

            Connect(facility, connection.FirstRoomId, connection.SecondRoomId);
        }

        return facility;
    }

    private static StationIdentity CreateIdentity(
        SeededRandom random,
        StationGenerationConstraints constraints)
    {
        var purpose = constraints.ForcedPurpose
            ?? random.Choose(Enum.GetValues<StationPurpose>());
        var budget = constraints.ForcedBudget
            ?? random.Choose(Enum.GetValues<StationBudgetClass>());
        var size = constraints.ForcedSize
            ?? random.Choose(Enum.GetValues<StationSizeClass>());
        var expansion = constraints.ForcedExpansionHistory
            ?? random.Choose(Enum.GetValues<StationExpansionHistory>());

        var age = expansion switch
        {
            StationExpansionHistory.PurposeBuilt => random.NextInt(1, 25),
            StationExpansionHistory.LightlyExpanded => random.NextInt(12, 65),
            _ => random.NextInt(35, 121)
        };

        var industrialIntensity = purpose switch
        {
            StationPurpose.Mining or StationPurpose.Industrial => random.NextInt(72, 101),
            StationPurpose.Logistics => random.NextInt(55, 86),
            StationPurpose.Research => random.NextInt(25, 61),
            _ => random.NextInt(30, 76)
        };

        var security = constraints.ForcedSecurityLevel
            ?? (purpose == StationPurpose.Security
                ? random.NextInt(75, 101)
                : random.NextInt(20, 86));

        var maintenance = Math.Clamp(
            92
            - (age / 2)
            + (budget == StationBudgetClass.Premium ? 12 : 0)
            - (budget == StationBudgetClass.Frugal ? 10 : 0)
            + random.NextInt(-12, 13),
            15,
            100);

        var crewCapacity = constraints.PlannedCrewCount ?? (size switch
        {
            StationSizeClass.Compact => random.NextInt(6, 13),
            StationSizeClass.Large => random.NextInt(20, 49),
            _ => random.NextInt(10, 25)
        });

        return new StationIdentity(
            purpose,
            age,
            budget,
            size,
            crewCapacity,
            industrialIntensity,
            Math.Clamp(security, 0, 100),
            maintenance,
            expansion);
    }

    private static StationArchetype ChooseArchetype(
        SeededRandom random,
        StationIdentity identity,
        StationGenerationConstraints constraints)
    {
        if (constraints.ForcedArchetype is { } forced)
        {
            return forced;
        }

        StationArchetype[] candidates;

        if (constraints.RequireRedundantPaths == true)
        {
            candidates =
            [
                StationArchetype.Ring,
                StationArchetype.MultiSpine,
                StationArchetype.CentralHub
            ];
        }
        else if (constraints.ForbidRedundantPaths == true)
        {
            candidates =
            [
                StationArchetype.Linear,
                StationArchetype.Branching,
                StationArchetype.AsymmetricIndustrial,
                StationArchetype.Compact,
                StationArchetype.Retrofit,
                StationArchetype.Sprawling
            ];
        }
        else if (identity.ExpansionHistory == StationExpansionHistory.HeavilyRetrofitted)
        {
            candidates =
            [
                StationArchetype.Retrofit,
                StationArchetype.AsymmetricIndustrial,
                StationArchetype.Branching,
                StationArchetype.Sprawling,
                StationArchetype.Linear
            ];
        }
        else if (identity.Budget == StationBudgetClass.Premium
                 && identity.AgeYears < 30)
        {
            candidates =
            [
                StationArchetype.Ring,
                StationArchetype.MultiSpine,
                StationArchetype.CentralHub,
                StationArchetype.Sprawling
            ];
        }
        else if (identity.Size == StationSizeClass.Compact)
        {
            candidates =
            [
                StationArchetype.Compact,
                StationArchetype.CentralHub,
                StationArchetype.Linear,
                StationArchetype.Branching
            ];
        }
        else if (identity.IndustrialIntensity >= 70)
        {
            candidates =
            [
                StationArchetype.AsymmetricIndustrial,
                StationArchetype.Branching,
                StationArchetype.Retrofit,
                StationArchetype.MultiSpine
            ];
        }
        else
        {
            candidates = Enum.GetValues<StationArchetype>();
        }

        return random.Choose(candidates);
    }

    private static List<RoomProfile> BuildRoomRequirements(
        StationIdentity identity,
        StationGenerationConstraints constraints)
    {
        var profiles = CanonicalProfiles.Values
            .Where(profile => !constraints.ForbiddenRoomIds.Contains(profile.Id))
            .Where(profile => !profile.Id.Equals("containment", StringComparison.OrdinalIgnoreCase)
                || constraints.RequiredRoomIds.Contains("containment"))
            .ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var authored in constraints.AuthoredRooms)
        {
            if (constraints.ForbiddenRoomIds.Contains(authored.Id))
            {
                continue;
            }

            profiles[authored.Id] = new RoomProfile(
                authored.Id,
                authored.Name,
                authored.Type,
                authored.MinWidth,
                authored.MaxWidth,
                authored.MinHeight,
                authored.MaxHeight,
                85);
        }

        foreach (var requiredId in constraints.RequiredRoomIds)
        {
            if (constraints.ForbiddenRoomIds.Contains(requiredId))
            {
                throw new StationGenerationException(
                    $"Room '{requiredId}' is both required and forbidden.",
                    [$"Constraint conflict: '{requiredId}' required + forbidden."]);
            }

            if (!profiles.ContainsKey(requiredId))
            {
                if (CanonicalProfiles.TryGetValue(requiredId, out var canonical))
                {
                    profiles[requiredId] = canonical;
                }
                else
                {
                    throw new StationGenerationException(
                        $"Required room '{requiredId}' has no room definition.",
                        [$"Add an AuthoredRooms definition for required room '{requiredId}'."]);
                }
            }
        }

        foreach (var airlockRoomId in constraints.RequiredAirlockRoomIds)
        {
            if (constraints.ForbiddenRoomIds.Contains(airlockRoomId))
            {
                throw new StationGenerationException(
                    $"Airlock '{airlockRoomId}' is both required and forbidden.",
                    [$"Constraint conflict: '{airlockRoomId}' required airlock + forbidden."]);
            }

            if (!profiles.TryGetValue(airlockRoomId, out var profile))
            {
                profiles[airlockRoomId] = new RoomProfile(
                    airlockRoomId,
                    airlockRoomId.Equals("airlock", StringComparison.OrdinalIgnoreCase)
                        ? "Airlock"
                        : $"Airlock {airlockRoomId}",
                    RoomType.Airlock,
                    7,
                    12,
                    9,
                    15,
                    90);
            }
            else if (profile.Type != RoomType.Airlock)
            {
                throw new StationGenerationException(
                    $"Required airlock room '{airlockRoomId}' is defined as {profile.Type}.",
                    [$"Room '{airlockRoomId}' must have RoomType.Airlock."]);
            }
        }

        if (!string.IsNullOrWhiteSpace(constraints.RequiredShutdownRoomId)
            && !profiles.ContainsKey(constraints.RequiredShutdownRoomId))
        {
            if (CanonicalProfiles.TryGetValue(constraints.RequiredShutdownRoomId, out var shutdownRoom))
            {
                profiles[shutdownRoom.Id] = shutdownRoom;
            }
            else
            {
                throw new StationGenerationException(
                    $"Shutdown room '{constraints.RequiredShutdownRoomId}' has no room definition.",
                    [$"RequiredShutdownRoomId '{constraints.RequiredShutdownRoomId}' is undefined."]);
            }
        }

        var desiredAirlocks = Math.Max(
            constraints.RequiredAirlockRoomIds.Count,
            Math.Max(0, constraints.RequiredAirlockCount ?? 1));
        var currentAirlocks = profiles.Values.Count(profile => profile.Type == RoomType.Airlock);
        for (var index = currentAirlocks + 1; index <= desiredAirlocks; index++)
        {
            var id = $"airlock-{index}";
            profiles[id] = new RoomProfile(
                id,
                $"Auxiliary Airlock {index}",
                RoomType.Airlock,
                7,
                11,
                9,
                14,
                80);
        }

        var minimum = constraints.MinimumFunctionalRoomCount ?? profiles.Count;
        var auxiliaryIndex = 1;
        while (profiles.Count < minimum)
        {
            var id = $"auxiliary-{auxiliaryIndex++}";
            if (profiles.ContainsKey(id))
            {
                continue;
            }

            var type = identity.Purpose switch
            {
                StationPurpose.Industrial or StationPurpose.Mining => RoomType.Storage,
                StationPurpose.Research => RoomType.Medical,
                StationPurpose.Habitat => RoomType.Recreation,
                StationPurpose.Security => RoomType.ControlRoom,
                _ => RoomType.Storage
            };

            profiles[id] = new RoomProfile(
                id,
                $"Auxiliary {type} {auxiliaryIndex - 1}",
                type,
                8,
                14,
                10,
                18,
                25);
        }

        if (constraints.MaximumFunctionalRoomCount is { } maximum
            && profiles.Count > maximum)
        {
            var protectedIds = new HashSet<string>(
                constraints.RequiredRoomIds,
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(constraints.RequiredShutdownRoomId))
            {
                protectedIds.Add(constraints.RequiredShutdownRoomId);
            }

            foreach (var candidate in profiles.Values
                         .Where(profile => !protectedIds.Contains(profile.Id))
                         .OrderBy(profile => profile.Importance)
                         .ThenBy(profile => profile.Id)
                         .ToList())
            {
                if (profiles.Count <= maximum)
                {
                    break;
                }

                profiles.Remove(candidate.Id);
            }

            if (profiles.Count > maximum)
            {
                throw new StationGenerationException(
                    "Maximum room count conflicts with required rooms.",
                    [$"Could not reduce {profiles.Count} required/protected rooms to maximum {maximum}."]);
            }
        }

        return profiles.Values.ToList();
    }

    private static IEnumerable<RoomProfile> OrderRequirements(
        IReadOnlyList<RoomProfile> requirements,
        StationGenerationConstraints constraints)
    {
        var constrainedIds = constraints.RequiredAdjacency
            .SelectMany(pair => new[] { pair.FirstRoomId, pair.SecondRoomId })
            .Concat(constraints.RequiredSeparation.SelectMany(pair => new[] { pair.FirstRoomId, pair.SecondRoomId }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requirements
            .OrderByDescending(profile => constrainedIds.Contains(profile.Id))
            .ThenByDescending(profile => profile.Id.Equals("quarters", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(profile => profile.Id.Equals("reactor", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(profile => profile.Importance)
            .ThenBy(profile => profile.Id);
    }

    private static double CorridorThickness(
        StationIdentity identity,
        SeededRandom random)
    {
        var (baseWidth, minimum, maximum) = identity.Budget switch
        {
            StationBudgetClass.Frugal => (3.7, 3.5, 4.0),
            StationBudgetClass.Premium => (4.7, 4.2, 5.0),
            _ => (4.1, 3.7, 4.5)
        };

        if (identity.Size == StationSizeClass.Compact)
        {
            baseWidth -= 0.2;
        }

        // Keep the station's topology thickness inside the same physical range
        // that functional access tunnels historically occupied. Access tunnels
        // now copy this actual cross-section exactly, so alignment improves
        // without making room packing materially denser than before.
        return Math.Clamp(baseWidth + random.NextDouble(-0.2, 0.3), minimum, maximum);
    }

    private static void BuildTopology(
        Facility facility,
        SeededRandom random,
        StationIdentity identity,
        StationArchetype archetype,
        double thickness)
    {
        switch (archetype)
        {
            case StationArchetype.Linear:
                BuildLinear(facility, random, thickness);
                break;
            case StationArchetype.CentralHub:
                BuildHub(facility, random, thickness);
                break;
            case StationArchetype.Branching:
                BuildBranching(facility, random, thickness);
                break;
            case StationArchetype.Ring:
                BuildRing(facility, random, thickness);
                break;
            case StationArchetype.AsymmetricIndustrial:
                BuildAsymmetricIndustrial(facility, random, thickness);
                break;
            case StationArchetype.Compact:
                BuildCompact(facility, random, thickness);
                break;
            case StationArchetype.Sprawling:
                BuildSprawling(facility, random, thickness);
                break;
            case StationArchetype.MultiSpine:
                BuildMultiSpine(facility, random, thickness);
                break;
            case StationArchetype.Retrofit:
                BuildRetrofit(facility, random, thickness);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(archetype));
        }

        if (identity.ExpansionHistory == StationExpansionHistory.HeavilyRetrofitted
            && archetype is not StationArchetype.Retrofit
            && archetype is not StationArchetype.Ring
            && random.NextDouble() < 0.65)
        {
            AddRetrofitStub(facility, random, thickness);
        }
    }

    private static void BuildLinear(Facility facility, SeededRandom random, double t)
    {
        var y = random.NextDouble(45, 55);
        AddHorizontal(facility, "corridor", "Primary Spine", 10, 90, y, t);

        if (random.NextDouble() < 0.45)
        {
            var x = random.NextDouble(34, 66);
            var north = random.NextDouble() < 0.5;
            if (north)
            {
                AddVertical(facility, "corridor-service", "Service Spur", x, 24, y - (t / 2), t);
            }
            else
            {
                AddVertical(facility, "corridor-service", "Service Spur", x, y + (t / 2), 76, t);
            }
        }
    }

    private static void BuildHub(Facility facility, SeededRandom random, double t)
    {
        var x = random.NextDouble(46, 54);
        var y = random.NextDouble(46, 54);
        AddSquare(facility, "corridor", "Central Hub", x, y, t);
        var half = t / 2;
        AddHorizontal(facility, "corridor-west", "West Concourse", 10, x - half, y, t);
        AddHorizontal(facility, "corridor-east", "East Concourse", x + half, 90, y, t);
        AddVertical(facility, "corridor-north", "North Concourse", x, 10, y - half, t);
        AddVertical(facility, "corridor-south", "South Concourse", x, y + half, 90, t);
    }

    private static void BuildBranching(Facility facility, SeededRandom random, double t)
    {
        var y = random.NextDouble(46, 53);
        AddHorizontal(facility, "corridor", "Main Trunk", 9, 91, y, t);

        var firstX = random.NextDouble(28, 40);
        AddSquare(facility, "corridor-branch-a-junction", "Branch Junction A", firstX, y + t, t);
        AddVertical(
            facility,
            "corridor-branch-a",
            "South Branch",
            firstX,
            y + (1.5 * t),
            91,
            t);

        var secondX = random.NextDouble(62, 76);
        AddSquare(facility, "corridor-branch-b-junction", "Branch Junction B", secondX, y - t, t);
        AddVertical(
            facility,
            "corridor-branch-b",
            "North Branch",
            secondX,
            9,
            y - (1.5 * t),
            t);
    }

    private static void BuildRing(Facility facility, SeededRandom random, double t)
    {
        var inset = random.NextDouble(23, 28);
        var left = inset;
        var right = 100 - inset;
        var top = inset + random.NextDouble(-2, 2);
        var bottom = 100 - inset + random.NextDouble(-2, 2);
        var half = t / 2;

        AddSquare(facility, "corridor-ring-nw", "Northwest Junction", left, top, t);
        AddSquare(facility, "corridor-ring-ne", "Northeast Junction", right, top, t);
        AddSquare(facility, "corridor-ring-se", "Southeast Junction", right, bottom, t);
        AddSquare(facility, "corridor-ring-sw", "Southwest Junction", left, bottom, t);

        AddHorizontal(facility, "corridor", "North Ring", left + half, right - half, top, t);
        AddHorizontal(facility, "corridor-ring-south", "South Ring", left + half, right - half, bottom, t);
        AddVertical(facility, "corridor-ring-west", "West Ring", left, top + half, bottom - half, t);
        AddVertical(facility, "corridor-ring-east", "East Ring", right, top + half, bottom - half, t);
    }

    private static void BuildAsymmetricIndustrial(Facility facility, SeededRandom random, double t)
    {
        var y = random.NextDouble(30, 39);
        AddHorizontal(facility, "corridor", "Habitat Spine", 9, 67, y, t);
        AddVertical(facility, "corridor-industrial-drop", "Industrial Drop", 67 + (t / 2), y - (t / 2), 90, t);

        var branchY = random.NextDouble(66, 78);
        var dropRight = 67 + t;
        AddHorizontal(facility, "corridor-industrial", "Industrial Gallery", dropRight, 94, branchY, t);

        var spurX = random.NextDouble(30, 46);
        AddVertical(facility, "corridor-service", "Service Spur", spurX, y + (t / 2), 90, t);
    }

    private static void BuildCompact(Facility facility, SeededRandom random, double t)
    {
        var x = random.NextDouble(48, 52);
        var y = random.NextDouble(48, 52);
        AddHorizontal(facility, "corridor", "Compact Spine", 22, 78, y, t);
        AddVertical(facility, "corridor-north", "Upper Access", x, 22, y - (t / 2), t);
        AddVertical(facility, "corridor-south", "Lower Access", x, y + (t / 2), 78, t);
    }

    private static void BuildSprawling(Facility facility, SeededRandom random, double t)
    {
        var y = random.NextDouble(45, 52);
        const double eastJunction = 82;
        AddHorizontal(facility, "corridor", "Long Concourse", 10, eastJunction, y, t);

        var northX = random.NextDouble(24, 36);
        AddVertical(facility, "corridor-north-wing", "North Wing", northX, 14, y - (t / 2), t);

        var southX = random.NextDouble(60, 73);
        AddVertical(facility, "corridor-south-wing", "South Wing", southX, y + (t / 2), 86, t);

        AddVertical(
            facility,
            "corridor-east-wing",
            "East Wing",
            eastJunction + (t / 2),
            26,
            74,
            t);
    }

    private static void BuildMultiSpine(Facility facility, SeededRandom random, double t)
    {
        var top = random.NextDouble(29, 34);
        var bottom = random.NextDouble(66, 71);
        var left = 18d;
        var right = 82d;
        var half = t / 2;

        AddHorizontal(facility, "corridor", "Upper Spine", left, right, top, t);
        AddHorizontal(facility, "corridor-lower", "Lower Spine", left, right, bottom, t);
        AddVertical(facility, "corridor-west-link", "West Link", left - half, top, bottom, t);
        AddVertical(facility, "corridor-east-link", "East Link", right + half, top, bottom, t);
    }

    private static void BuildRetrofit(Facility facility, SeededRandom random, double t)
    {
        var y = random.NextDouble(45, 54);
        AddHorizontal(facility, "corridor", "Original Spine", 12, 69, y, t);

        AddVertical(facility, "corridor-retrofit-drop", "Retrofit Drop", 69 + (t / 2), y - (t / 2), 90, t);
        var dropRight = 69 + t;
        var galleryY = random.NextDouble(70, 80);
        AddHorizontal(facility, "corridor-retrofit-gallery", "Added Industrial Gallery", dropRight, 95, galleryY, t);

        var oldSpurX = random.NextDouble(27, 39);
        AddVertical(facility, "corridor-old-spur", "Old Service Spur", oldSpurX, 12, y - (t / 2), t);
    }

    private static void AddRetrofitStub(Facility facility, SeededRandom random, double t)
    {
        var corridors = facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(room => room.Id)
            .ToList();

        foreach (var target in random.Shuffle(corridors))
        {
            var bounds = StationGeometry.Bounds(target);
            Room? candidate = null;

            if (target.MapWidth >= target.MapHeight)
            {
                if (bounds.Width <= 4)
                {
                    continue;
                }

                var x = random.NextDouble(bounds.Left + 2, bounds.Right - 2);
                var top = bounds.Bottom;
                var bottom = Math.Min(CanvasMax, top + random.NextDouble(12, 25));
                if (bottom - top >= 8)
                {
                    candidate = CreateRoom(
                        "corridor-retrofit-stub",
                        "Retrofit Service Passage",
                        RoomType.Corridor,
                        x,
                        (top + bottom) / 2,
                        t,
                        bottom - top);
                }
            }
            else
            {
                if (bounds.Height <= 4)
                {
                    continue;
                }

                var y = random.NextDouble(bounds.Top + 2, bounds.Bottom - 2);
                var left = bounds.Right;
                var right = Math.Min(CanvasMax, left + random.NextDouble(12, 25));
                if (right - left >= 8)
                {
                    candidate = CreateRoom(
                        "corridor-retrofit-stub",
                        "Retrofit Service Passage",
                        RoomType.Corridor,
                        (left + right) / 2,
                        y,
                        right - left,
                        t);
                }
            }

            if (candidate is null
                || !InsideCanvas(candidate)
                || facility.Rooms.Values.Any(existing =>
                    !existing.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase)
                    && StationGeometry.InteriorOverlapArea(candidate, existing) > OverlapTolerance)
                || !TryFindSharedPortal(candidate, target, out _))
            {
                continue;
            }

            facility.Rooms.Add(candidate.Id, candidate);
            return;
        }
    }

    private static void PlaceFunctionalRoom(
        Facility facility,
        SeededRandom random,
        StationIdentity identity,
        RoomProfile profile,
        StationGenerationConstraints constraints,
        Dictionary<string, PlacementInfo> placements)
    {
        var corridors = facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(room => room.Id)
            .ToList();

        PlacementCandidate? best = null;

        for (var attempt = 0; attempt < 760; attempt++)
        {
            var corridor = random.Choose(corridors);
            var side = ChooseAttachmentSide(random, corridor);
            var candidate = CreatePlacementCandidate(
                facility,
                random,
                identity,
                profile,
                corridor,
                side,
                constraints,
                placements,
                attempt);

            if (candidate is null)
            {
                continue;
            }

            if (best is null || candidate.Score > best.Score)
            {
                best = candidate;
            }

            if (attempt > 140 && best.Score >= 145)
            {
                break;
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException(
                $"Unable to spatially pack room '{profile.Id}'.");
        }

        facility.Rooms.Add(best.Room.Id, best.Room);
        facility.Rooms.Add(best.Hallway.Id, best.Hallway);
        Connect(facility, best.Room.Id, best.Hallway.Id);
        Connect(facility, best.Hallway.Id, best.CorridorId);

        placements[profile.Id] = new PlacementInfo(
            profile.Id,
            best.CorridorId,
            best.Room.MapX,
            best.Room.MapY);
    }

    private static PlacementCandidate? CreatePlacementCandidate(
        Facility facility,
        SeededRandom random,
        StationIdentity identity,
        RoomProfile profile,
        Room corridor,
        AttachmentSide side,
        StationGenerationConstraints constraints,
        IReadOnlyDictionary<string, PlacementInfo> placements,
        int attempt)
    {
        var scale = identity.Size switch
        {
            StationSizeClass.Compact => 0.88,
            StationSizeClass.Large => 1.12,
            _ => 1.00
        };

        if (profile.Id.Equals("quarters", StringComparison.OrdinalIgnoreCase))
        {
            scale *= Math.Clamp(identity.CrewCapacity / 16d, 0.86, 1.22);
        }

        if (profile.Type == RoomType.Hydroponics)
        {
            var crewScale = Math.Sqrt(Math.Max(1, identity.CrewCapacity) / 12d);
            var policyScale = Math.Sqrt(Math.Max(0.1, constraints.HydroponicsCapacityMultiplier));
            scale *= Math.Clamp(crewScale * policyScale, 0.82, 1.38);
        }

        if (profile.Type is RoomType.Engineering or RoomType.Generator or RoomType.Reactor or RoomType.Storage)
        {
            scale *= 0.9 + (identity.IndustrialIntensity / 500d);
        }

        // Prefer the deliberately larger profiles, but progressively relax them
        // on crowded deterministic seeds. Status-plate reservations are hard
        // geometry now, so late packing needs enough headroom to avoid turning
        // valid legacy seeds into generation failures.
        var shrink = attempt < 260
            ? 1d
            : Math.Clamp(1d - ((attempt - 260) / 820d), 0.76, 1d);

        var width = random.NextDouble(profile.MinWidth, profile.MaxWidth) * scale * shrink;
        var height = random.NextDouble(profile.MinHeight, profile.MaxHeight) * scale * shrink;

        if (random.NextDouble() < 0.22
            && profile.Type is not RoomType.Airlock)
        {
            (width, height) = (height, width);
        }

        // The default 100% map camera renders the authoritative 0..100 deck
        // inside a 2560x2240px physical layer. These minima therefore guarantee
        // every generated functional room is at least ~200px on both axes.
        width = Math.Clamp(width, 8.0, 27);
        // Room status telemetry owns real generation space. Keep normal rooms
        // larger than the old generator, while allowing a bounded late fallback
        // for compact/retrofit seeds where the reserved plate strip is the
        // difference between a valid layout and no layout at all.
        height = Math.Clamp(height, 9.0, 30);

        // Access tunnels inherit the exact cross-axis thickness of the
        // corridor they attach to. Independent random passage widths produced
        // visible 4-5px steps at corridor ends and misaligned physical portals.
        var passageWidth = corridor.MapWidth >= corridor.MapHeight
            ? corridor.MapHeight
            : corridor.MapWidth;

        var retrofitFactor = identity.ExpansionHistory switch
        {
            StationExpansionHistory.PurposeBuilt => 0.8,
            StationExpansionHistory.HeavilyRetrofitted => 1.45,
            _ => 1.0
        };

        var hallLength = Math.Clamp(
            random.NextDouble(2.6, 5.7) * retrofitFactor,
            2.4,
            8.5);

        if (profile.Type == RoomType.Airlock)
        {
            hallLength = Math.Max(hallLength, 3.5);
        }

        var corridorBounds = StationGeometry.Bounds(corridor);
        double roomX;
        double roomY;
        double hallX;
        double hallY;
        double hallWidth;
        double hallHeight;

        switch (side)
        {
            case AttachmentSide.North:
            {
                if (corridorBounds.Width < passageWidth + 0.2)
                {
                    return null;
                }

                var anchor = random.NextDouble(
                    corridorBounds.Left + (passageWidth / 2),
                    corridorBounds.Right - (passageWidth / 2));
                var roomBottom = corridorBounds.Top - hallLength;
                roomX = anchor;
                roomY = roomBottom - (height / 2);
                hallX = anchor;
                hallY = corridorBounds.Top - (hallLength / 2);
                hallWidth = passageWidth;
                hallHeight = hallLength;
                break;
            }

            case AttachmentSide.South:
            {
                if (corridorBounds.Width < passageWidth + 0.2)
                {
                    return null;
                }

                var anchor = random.NextDouble(
                    corridorBounds.Left + (passageWidth / 2),
                    corridorBounds.Right - (passageWidth / 2));
                var roomTop = corridorBounds.Bottom + hallLength;
                roomX = anchor;
                roomY = roomTop + (height / 2);
                hallX = anchor;
                hallY = corridorBounds.Bottom + (hallLength / 2);
                hallWidth = passageWidth;
                hallHeight = hallLength;
                break;
            }

            case AttachmentSide.West:
            {
                if (corridorBounds.Height < passageWidth + 0.2)
                {
                    return null;
                }

                var anchor = random.NextDouble(
                    corridorBounds.Top + (passageWidth / 2),
                    corridorBounds.Bottom - (passageWidth / 2));
                var roomRight = corridorBounds.Left - hallLength;
                roomX = roomRight - (width / 2);
                roomY = anchor;
                hallX = corridorBounds.Left - (hallLength / 2);
                hallY = anchor;
                hallWidth = hallLength;
                hallHeight = passageWidth;
                break;
            }

            case AttachmentSide.East:
            {
                if (corridorBounds.Height < passageWidth + 0.2)
                {
                    return null;
                }

                var anchor = random.NextDouble(
                    corridorBounds.Top + (passageWidth / 2),
                    corridorBounds.Bottom - (passageWidth / 2));
                var roomLeft = corridorBounds.Right + hallLength;
                roomX = roomLeft + (width / 2);
                roomY = anchor;
                hallX = corridorBounds.Right + (hallLength / 2);
                hallY = anchor;
                hallWidth = hallLength;
                hallHeight = passageWidth;
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(side));
        }

        var statusSide = side switch
        {
            AttachmentSide.North => RoomStatusPlateSide.Top,
            AttachmentSide.South => RoomStatusPlateSide.Bottom,
            _ => roomY < 50 ? RoomStatusPlateSide.Top : RoomStatusPlateSide.Bottom
        };

        var room = new Room
        {
            Id = profile.Id,
            Name = profile.Name,
            Type = profile.Type,
            MapX = roomX,
            MapY = roomY,
            MapWidth = width,
            MapHeight = height,
            StatusPlateSide = statusSide
        };

        var hallway = new Room
        {
            Id = $"hall-{profile.Id}",
            Name = $"{profile.Name} Access",
            Type = RoomType.Corridor,
            MapX = hallX,
            MapY = hallY,
            MapWidth = hallWidth,
            MapHeight = hallHeight
        };

        var statusPlate = CreateStatusPlateEnvelope(room);
        if (!InsideCanvas(room) || !InsideCanvas(hallway) || !InsideCanvas(statusPlate))
        {
            return null;
        }

        foreach (var existing in facility.Rooms.Values)
        {
            if (StationGeometry.InteriorOverlapArea(room, existing) > OverlapTolerance)
            {
                return null;
            }

            if (!existing.Id.Equals(corridor.Id, StringComparison.OrdinalIgnoreCase)
                && StationGeometry.InteriorOverlapArea(hallway, existing) > OverlapTolerance)
            {
                return null;
            }

            if (StationGeometry.InteriorOverlapArea(statusPlate, existing) > OverlapTolerance)
            {
                return null;
            }

            if (existing.StatusPlateSide is not null)
            {
                var existingPlate = CreateStatusPlateEnvelope(existing);
                if (StationGeometry.InteriorOverlapArea(room, existingPlate) > OverlapTolerance
                    || StationGeometry.InteriorOverlapArea(hallway, existingPlate) > OverlapTolerance
                    || StationGeometry.InteriorOverlapArea(statusPlate, existingPlate) > OverlapTolerance)
                {
                    return null;
                }
            }
        }

        if (StationGeometry.InteriorOverlapArea(room, hallway) > OverlapTolerance
            || StationGeometry.InteriorOverlapArea(statusPlate, hallway) > OverlapTolerance)
        {
            return null;
        }

        if (!TryFindSharedPortal(room, hallway, out _)
            || !TryFindSharedPortal(hallway, corridor, out _))
        {
            return null;
        }

        var score = random.NextDouble(0, 4);
        score += LayoutIdentityScore(identity, profile, room);

        if (profile.Id.Equals("reactor", StringComparison.OrdinalIgnoreCase)
            && constraints.ReactorMustBeIsolated)
        {
            score += CorridorDegree(facility, corridor.Id) <= 1 ? 70 : 0;
            foreach (var placement in placements.Values)
            {
                if (placement.RoomId is "quarters" or "kitchen" or "lounge" or "medical")
                {
                    score += Distance(room.MapX, room.MapY, placement.X, placement.Y) * 1.5;
                }
            }
        }

        if (profile.Id.Equals("medical", StringComparison.OrdinalIgnoreCase)
            && constraints.MedicalMustBeNearHabitat
            && placements.TryGetValue("quarters", out var quarters))
        {
            var distance = Distance(room.MapX, room.MapY, quarters.X, quarters.Y);
            score += Math.Max(0, 140 - (distance * 5));
            if (quarters.CorridorId.Equals(corridor.Id, StringComparison.OrdinalIgnoreCase))
            {
                score += 55;
            }
        }

        foreach (var adjacency in constraints.RequiredAdjacency)
        {
            var otherId = adjacency.FirstRoomId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)
                ? adjacency.SecondRoomId
                : adjacency.SecondRoomId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)
                    ? adjacency.FirstRoomId
                    : null;

            if (otherId is not null && placements.TryGetValue(otherId, out var other))
            {
                score += other.CorridorId.Equals(corridor.Id, StringComparison.OrdinalIgnoreCase)
                    ? 100
                    : 0;
                score += Math.Max(0, 80 - (Distance(room.MapX, room.MapY, other.X, other.Y) * 2));
            }
        }

        foreach (var separation in constraints.RequiredSeparation)
        {
            var otherId = separation.FirstRoomId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)
                ? separation.SecondRoomId
                : separation.SecondRoomId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)
                    ? separation.FirstRoomId
                    : null;

            if (otherId is not null && placements.TryGetValue(otherId, out var other))
            {
                score += Distance(room.MapX, room.MapY, other.X, other.Y) * 2.5;
            }
        }

        return new PlacementCandidate(room, hallway, corridor.Id, score);
    }

    private static Room CreateStatusPlateEnvelope(Room room)
    {
        var side = room.StatusPlateSide ?? RoomStatusPlateSide.Top;
        var edgeY = side == RoomStatusPlateSide.Top
            ? room.MapY - (room.MapHeight / 2)
            : room.MapY + (room.MapHeight / 2);
        var centerY = edgeY + (side == RoomStatusPlateSide.Top
            ? -(StatusPlateReserveHeight / 2)
            : StatusPlateReserveHeight / 2);

        return new Room
        {
            Id = $"status-plate:{room.Id}",
            Name = $"{room.Name} Status Plate Reserve",
            Type = RoomType.Corridor,
            MapX = room.MapX,
            MapY = centerY,
            MapWidth = Math.Min(StatusPlateReserveWidth, room.MapWidth),
            MapHeight = StatusPlateReserveHeight
        };
    }

    private static double LayoutIdentityScore(
        StationIdentity identity,
        RoomProfile profile,
        Room room)
    {
        var score = 0d;
        var industrial = profile.Type is RoomType.Engineering
            or RoomType.Generator
            or RoomType.Reactor
            or RoomType.Storage;

        if (industrial)
        {
            score += identity.IndustrialIntensity * (room.MapX / 100d) * 0.35;
        }
        else if (profile.Type is RoomType.CrewQuarters
                 or RoomType.Kitchen
                 or RoomType.Recreation
                 or RoomType.Medical)
        {
            score += (100 - identity.IndustrialIntensity) * ((100 - room.MapX) / 100d) * 0.25;
        }

        if (identity.ExpansionHistory == StationExpansionHistory.HeavilyRetrofitted)
        {
            score += Math.Abs(room.MapY - 50) * 0.25;
        }

        return score;
    }

    private static AttachmentSide ChooseAttachmentSide(
        SeededRandom random,
        Room corridor)
    {
        if (corridor.MapWidth > corridor.MapHeight * 1.4)
        {
            return random.NextDouble() < 0.88
                ? random.Choose(new[] { AttachmentSide.North, AttachmentSide.South })
                : random.Choose(new[] { AttachmentSide.East, AttachmentSide.West });
        }

        if (corridor.MapHeight > corridor.MapWidth * 1.4)
        {
            return random.NextDouble() < 0.88
                ? random.Choose(new[] { AttachmentSide.East, AttachmentSide.West })
                : random.Choose(new[] { AttachmentSide.North, AttachmentSide.South });
        }

        return random.Choose(Enum.GetValues<AttachmentSide>());
    }

    private static void ApplyInitialDoorConstraints(
        Facility facility,
        StationGenerationConstraints constraints)
    {
        foreach (var roomId in constraints.InitiallyInaccessibleRoomIds)
        {
            foreach (var door in facility.Doors.Where(door =>
                         door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
                         || door.RoomBId.Equals(roomId, StringComparison.OrdinalIgnoreCase)))
            {
                door.IsOpen = false;
                door.IsLocked = true;
            }
        }

        foreach (var roomId in constraints.InitiallyAccessibleRoomIds)
        {
            foreach (var door in facility.Doors.Where(door =>
                         door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
                         || door.RoomBId.Equals(roomId, StringComparison.OrdinalIgnoreCase)))
            {
                door.IsLocked = false;
                door.IsOpen = true;
            }
        }
    }

    private static List<string> Validate(
        Facility facility,
        StationGenerationConstraints constraints,
        IReadOnlyList<RoomProfile> requirements,
        bool requirePlayableDefault)
    {
        var errors = new List<string>();

        foreach (var room in facility.Rooms.Values)
        {
            if (!InsideCanvas(room))
            {
                errors.Add($"{room.Id} lies outside the 0..100 station canvas.");
            }

            if (room.MapWidth < 2 || room.MapHeight < 2)
            {
                errors.Add($"{room.Id} is microscopically small.");
            }
        }

        var rooms = facility.Rooms.Values.ToList();
        for (var firstIndex = 0; firstIndex < rooms.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < rooms.Count; secondIndex++)
            {
                var overlap = StationGeometry.InteriorOverlapArea(
                    rooms[firstIndex],
                    rooms[secondIndex]);

                if (overlap > OverlapTolerance)
                {
                    errors.Add($"{rooms[firstIndex].Id} overlaps {rooms[secondIndex].Id} by {overlap:0.###}.");
                }
            }
        }

        var reservedPlates = rooms
            .Where(room => room.Type != RoomType.Corridor && room.StatusPlateSide is not null)
            .Select(room => (Owner: room, Envelope: CreateStatusPlateEnvelope(room)))
            .ToList();

        foreach (var plate in reservedPlates)
        {
            if (!InsideCanvas(plate.Envelope))
                errors.Add($"{plate.Owner.Id} status plate reserve lies outside the station canvas.");

            foreach (var other in rooms.Where(room => room.Id != plate.Owner.Id))
            {
                var overlap = StationGeometry.InteriorOverlapArea(plate.Envelope, other);
                if (overlap > OverlapTolerance)
                {
                    errors.Add(
                        $"{plate.Owner.Id} status plate reserve overlaps {other.Id} by {overlap:0.###}.");
                }
            }
        }

        for (var firstIndex = 0; firstIndex < reservedPlates.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < reservedPlates.Count; secondIndex++)
            {
                var overlap = StationGeometry.InteriorOverlapArea(
                    reservedPlates[firstIndex].Envelope,
                    reservedPlates[secondIndex].Envelope);
                if (overlap > OverlapTolerance)
                {
                    errors.Add(
                        $"{reservedPlates[firstIndex].Owner.Id} and {reservedPlates[secondIndex].Owner.Id} status plate reserves overlap by {overlap:0.###}.");
                }
            }
        }

        foreach (var door in facility.Doors)
        {
            if (!facility.Rooms.TryGetValue(door.RoomAId, out var first)
                || !facility.Rooms.TryGetValue(door.RoomBId, out var second))
            {
                errors.Add($"{door.Id} references a missing room.");
                continue;
            }

            if (!TryFindSharedPortal(first, second, out _))
            {
                errors.Add($"{door.Id} is not on a real shared boundary.");
            }
        }

        foreach (var required in requirements)
        {
            if (!facility.Rooms.ContainsKey(required.Id)
                && !constraints.FullyAuthoredGeometry)
            {
                errors.Add($"Required room '{required.Id}' is missing.");
            }
        }

        foreach (var requiredId in constraints.RequiredRoomIds)
        {
            if (!facility.Rooms.ContainsKey(requiredId))
            {
                errors.Add($"Campaign-required room '{requiredId}' is missing.");
            }
        }

        foreach (var forbiddenId in constraints.ForbiddenRoomIds)
        {
            if (facility.Rooms.ContainsKey(forbiddenId))
            {
                errors.Add($"Campaign-forbidden room '{forbiddenId}' exists.");
            }
        }

        var functionalRooms = facility.Rooms.Values
            .Where(room => room.Type != RoomType.Corridor)
            .ToList();

        if (!constraints.FullyAuthoredGeometry)
        {
            var functionalArea = functionalRooms.Sum(room => room.MapWidth * room.MapHeight);
            var circulationArea = facility.Rooms.Values
                .Where(room => room.Type == RoomType.Corridor)
                .Sum(room => room.MapWidth * room.MapHeight);

            if (functionalArea <= circulationArea)
            {
                errors.Add(
                    $"Functional room area {functionalArea:0} must dominate circulation area {circulationArea:0}.");
            }
        }

        if (constraints.MinimumFunctionalRoomCount is { } minimum
            && functionalRooms.Count < minimum)
        {
            errors.Add($"Functional room count {functionalRooms.Count} is below minimum {minimum}.");
        }

        if (constraints.MaximumFunctionalRoomCount is { } maximum
            && functionalRooms.Count > maximum)
        {
            errors.Add($"Functional room count {functionalRooms.Count} exceeds maximum {maximum}.");
        }

        if (constraints.RequiredAirlockCount is { } requiredAirlocks)
        {
            var airlockCount = functionalRooms.Count(room => room.Type == RoomType.Airlock);
            if (airlockCount != requiredAirlocks)
            {
                errors.Add($"Airlock count {airlockCount} does not equal required {requiredAirlocks}.");
            }
        }

        foreach (var roomId in constraints.RequiredAirlockRoomIds)
        {
            if (!facility.Rooms.TryGetValue(roomId, out var room)
                || room.Type != RoomType.Airlock)
            {
                errors.Add($"Required airlock placement '{roomId}' is missing or is not an airlock.");
            }
        }

        foreach (var roomId in constraints.RequiredRobotRoomIds)
        {
            if (!facility.Rooms.ContainsKey(roomId))
            {
                errors.Add($"Required robot placement room '{roomId}' does not exist.");
            }
        }

        foreach (var roomId in constraints.RequiredTurretRoomIds)
        {
            if (!facility.Rooms.ContainsKey(roomId))
            {
                errors.Add($"Required turret placement room '{roomId}' does not exist.");
            }
        }

        if (requirePlayableDefault && !facility.Rooms.ContainsKey("corridor"))
        {
            errors.Add("Playable station is missing canonical primary corridor 'corridor'.");
        }

        if (facility.Rooms.ContainsKey("corridor"))
        {
            var navigation = new NavigationSystem();
            foreach (var room in functionalRooms)
            {
                if (constraints.FullyAuthoredGeometry)
                {
                    break;
                }

                if (navigation.FindPathIgnoringDoorState(facility, room.Id, "corridor").Count == 0)
                {
                    errors.Add($"{room.Id} is structurally unreachable from the station network.");
                }
            }
        }

        foreach (var adjacency in constraints.RequiredAdjacency)
        {
            var pathLength = StructuralPathLength(
                facility,
                adjacency.FirstRoomId,
                adjacency.SecondRoomId);

            if (pathLength is null || pathLength.Value > 5)
            {
                errors.Add(
                    $"Required adjacency {adjacency.FirstRoomId}<->{adjacency.SecondRoomId} is not locally adjacent (path={pathLength?.ToString() ?? "none"}).");
            }
        }

        foreach (var separation in constraints.RequiredSeparation)
        {
            if (!facility.Rooms.TryGetValue(separation.FirstRoomId, out var first)
                || !facility.Rooms.TryGetValue(separation.SecondRoomId, out var second))
            {
                errors.Add($"Required separation references a missing room.");
                continue;
            }

            var distance = Distance(first.MapX, first.MapY, second.MapX, second.MapY);
            if (distance + 0.001 < separation.MinimumMapDistance)
            {
                errors.Add(
                    $"Required separation {separation.FirstRoomId}<->{separation.SecondRoomId} is {distance:0.0}, below {separation.MinimumMapDistance:0.0}.");
            }
        }

        if (constraints.ReactorMustBeIsolated
            && facility.Rooms.TryGetValue("reactor", out var reactor)
            && facility.Rooms.TryGetValue("quarters", out var quarters)
            && Distance(reactor.MapX, reactor.MapY, quarters.MapX, quarters.MapY) < 24)
        {
            errors.Add("Reactor isolation constraint failed: reactor is too close to habitat.");
        }

        if (constraints.MedicalMustBeNearHabitat
            && facility.Rooms.TryGetValue("medical", out var medical)
            && facility.Rooms.TryGetValue("quarters", out var habitat)
            && Distance(medical.MapX, medical.MapY, habitat.MapX, habitat.MapY) > 34)
        {
            errors.Add("Medical-near-habitat constraint failed.");
        }

        if (!string.IsNullOrWhiteSpace(constraints.RequiredShutdownRoomId)
            && !facility.Rooms.ContainsKey(constraints.RequiredShutdownRoomId))
        {
            errors.Add($"Mandatory shutdown room '{constraints.RequiredShutdownRoomId}' is missing.");
        }

        var hasCycle = HasCorridorCycle(facility);
        if (constraints.RequireRedundantPaths == true && !hasCycle)
        {
            errors.Add("Campaign requires redundant corridor routing, but generated corridor topology has no cycle.");
        }

        if (constraints.ForbidRedundantPaths == true && hasCycle)
        {
            errors.Add("Campaign forbids redundant corridor routing, but generated corridor topology contains a cycle.");
        }

        if (constraints.RequiredChokepointCount is { } chokepoints
            && CountCorridorArticulationPoints(facility) < chokepoints)
        {
            errors.Add(
                $"Campaign requires {chokepoints} corridor chokepoints; only {CountCorridorArticulationPoints(facility)} were generated.");
        }

        foreach (var inaccessible in constraints.InitiallyInaccessibleRoomIds)
        {
            if (!facility.Rooms.ContainsKey(inaccessible))
            {
                errors.Add($"Initially inaccessible room '{inaccessible}' does not exist.");
                continue;
            }

            var attachedDoors = facility.Doors.Where(door =>
                    door.RoomAId.Equals(inaccessible, StringComparison.OrdinalIgnoreCase)
                    || door.RoomBId.Equals(inaccessible, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (attachedDoors.Count == 0 || attachedDoors.Any(door => door.IsPassable))
            {
                errors.Add($"Initially inaccessible room '{inaccessible}' is not sealed.");
            }
        }

        foreach (var accessible in constraints.InitiallyAccessibleRoomIds)
        {
            if (!facility.Rooms.ContainsKey(accessible))
            {
                errors.Add($"Initially accessible room '{accessible}' does not exist.");
                continue;
            }

            if (facility.Doors.Where(door =>
                    door.RoomAId.Equals(accessible, StringComparison.OrdinalIgnoreCase)
                    || door.RoomBId.Equals(accessible, StringComparison.OrdinalIgnoreCase))
                .All(door => !door.IsPassable))
            {
                errors.Add($"Initially accessible room '{accessible}' has no passable entry.");
            }
        }

        return errors;
    }

    private static int? StructuralPathLength(
        Facility facility,
        string startRoomId,
        string targetRoomId)
    {
        var path = new NavigationSystem()
            .FindPathIgnoringDoorState(facility, startRoomId, targetRoomId);

        return path.Count == 0 ? null : path.Count - 1;
    }

    private static bool HasCorridorCycle(Facility facility)
    {
        var corridorIds = facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            .Select(room => room.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (corridorIds.Count < 3)
        {
            return false;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in corridorIds)
        {
            if (visited.Contains(root))
            {
                continue;
            }

            if (HasCycleDepthFirst(root, parent: null, corridorIds, facility, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCycleDepthFirst(
        string current,
        string? parent,
        HashSet<string> corridorIds,
        Facility facility,
        HashSet<string> visited)
    {
        visited.Add(current);

        foreach (var neighbour in CorridorNeighbours(facility, current)
                     .Where(corridorIds.Contains))
        {
            if (parent is not null
                && neighbour.Equals(parent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (visited.Contains(neighbour))
            {
                return true;
            }

            if (HasCycleDepthFirst(neighbour, current, corridorIds, facility, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountCorridorArticulationPoints(Facility facility)
    {
        var nodes = facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            .Select(room => room.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (nodes.Count <= 2)
        {
            return nodes.Count == 2 ? 1 : 0;
        }

        var originalComponents = CountComponents(facility, nodes, excluded: null);
        var count = 0;

        foreach (var node in nodes)
        {
            if (CountComponents(facility, nodes, node) > originalComponents)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountComponents(
        Facility facility,
        HashSet<string> nodes,
        string? excluded)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = 0;

        foreach (var node in nodes)
        {
            if (node.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                || visited.Contains(node))
            {
                continue;
            }

            components++;
            var queue = new Queue<string>();
            queue.Enqueue(node);
            visited.Add(node);

            while (queue.TryDequeue(out var current))
            {
                foreach (var neighbour in CorridorNeighbours(facility, current))
                {
                    if (!nodes.Contains(neighbour)
                        || neighbour.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                        || !visited.Add(neighbour))
                    {
                        continue;
                    }

                    queue.Enqueue(neighbour);
                }
            }
        }

        return components;
    }

    private static IEnumerable<string> CorridorNeighbours(
        Facility facility,
        string roomId)
    {
        foreach (var door in facility.Doors)
        {
            if (door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase))
            {
                yield return door.RoomBId;
            }
            else if (door.RoomBId.Equals(roomId, StringComparison.OrdinalIgnoreCase))
            {
                yield return door.RoomAId;
            }
        }
    }

    private static int CorridorDegree(Facility facility, string corridorId) =>
        CorridorNeighbours(facility, corridorId)
            .Count(neighbour => facility.Rooms.TryGetValue(neighbour, out var room)
                && room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase));

    private static void AutoConnectCorridors(Facility facility)
    {
        var corridors = facility.Rooms.Values
            .Where(room => room.Type == RoomType.Corridor
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (var firstIndex = 0; firstIndex < corridors.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < corridors.Count; secondIndex++)
            {
                var first = corridors[firstIndex];
                var second = corridors[secondIndex];

                if (StationGeometry.InteriorOverlapArea(first, second) > OverlapTolerance)
                {
                    continue;
                }

                if (TryFindSharedPortal(first, second, out _))
                {
                    Connect(facility, first.Id, second.Id);
                }
            }
        }
    }

    private static void Connect(
        Facility facility,
        string firstRoomId,
        string secondRoomId)
    {
        if (facility.FindDoorBetween(firstRoomId, secondRoomId) is not null)
        {
            return;
        }

        facility.Doors.Add(new Door
        {
            Id = $"door-{firstRoomId}-{secondRoomId}",
            RoomAId = firstRoomId,
            RoomBId = secondRoomId
        });
    }

    private static void AddHorizontal(
        Facility facility,
        string id,
        string name,
        double left,
        double right,
        double y,
        double thickness)
    {
        AddRoom(
            facility,
            id,
            name,
            RoomType.Corridor,
            (left + right) / 2,
            y,
            right - left,
            thickness);
    }

    private static void AddVertical(
        Facility facility,
        string id,
        string name,
        double x,
        double top,
        double bottom,
        double thickness)
    {
        AddRoom(
            facility,
            id,
            name,
            RoomType.Corridor,
            x,
            (top + bottom) / 2,
            thickness,
            bottom - top);
    }

    private static void AddSquare(
        Facility facility,
        string id,
        string name,
        double x,
        double y,
        double size)
    {
        AddRoom(facility, id, name, RoomType.Corridor, x, y, size, size);
    }

    private static void AddRoom(
        Facility facility,
        string id,
        string name,
        RoomType type,
        double x,
        double y,
        double width,
        double height)
    {
        facility.Rooms.Add(id, CreateRoom(id, name, type, x, y, width, height));
    }

    private static Room CreateRoom(
        string id,
        string name,
        RoomType type,
        double x,
        double y,
        double width,
        double height) =>
        new()
        {
            Id = id,
            Name = name,
            Type = type,
            MapX = x,
            MapY = y,
            MapWidth = width,
            MapHeight = height
        };

    private static bool InsideCanvas(Room room)
    {
        var bounds = StationGeometry.Bounds(room);
        return bounds.Left >= CanvasMin
            && bounds.Right <= CanvasMax
            && bounds.Top >= CanvasMin
            && bounds.Bottom <= CanvasMax;
    }

    private static bool TryFindSharedPortal(
        Room first,
        Room second,
        out StationPortal portal)
    {
        try
        {
            portal = StationGeometry.FindSharedPortal(first, second);
            return true;
        }
        catch (InvalidOperationException)
        {
            portal = default;
            return false;
        }
    }

    private static double Distance(
        double firstX,
        double firstY,
        double secondX,
        double secondY)
    {
        var dx = firstX - secondX;
        var dy = firstY - secondY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool HasCampaignOverrides(StationGenerationConstraints constraints) =>
        constraints.ForcedArchetype is not null
        || constraints.ForcedPurpose is not null
        || constraints.ForcedBudget is not null
        || constraints.ForcedSize is not null
        || constraints.ForcedExpansionHistory is not null
        || constraints.ForcedSecurityLevel is not null
        || constraints.RequiredRoomIds.Count > 0
        || constraints.ForbiddenRoomIds.Count > 0
        || constraints.AuthoredRooms.Count > 0
        || constraints.RequiredAdjacency.Count > 0
        || constraints.RequiredSeparation.Count > 0
        || constraints.MinimumFunctionalRoomCount is not null
        || constraints.MaximumFunctionalRoomCount is not null
        || constraints.RequiredAirlockCount is not null
        || constraints.RequiredAirlockRoomIds.Count > 0
        || constraints.RequiredTurretCount is not null
        || constraints.RequiredTurretRoomIds.Count > 0
        || constraints.RequiredRobotCount is not null
        || constraints.RequiredRobotRoomIds.Count > 0
        || constraints.PlannedCrewCount is not null
        || Math.Abs(constraints.HydroponicsCapacityMultiplier - 1) > 0.0001
        || constraints.RequireRedundantPaths is not null
        || constraints.ForbidRedundantPaths is not null
        || constraints.RequiredChokepointCount is not null
        || constraints.ReactorMustBeIsolated
        || constraints.MedicalMustBeNearHabitat
        || constraints.RequiredShutdownRoomId is not null
        || constraints.InitiallyAccessibleRoomIds.Count > 0
        || constraints.InitiallyInaccessibleRoomIds.Count > 0
        || constraints.EnvironmentOverrides.Count > 0
        || constraints.FullyAuthoredGeometry;

    private static void ValidateConstraintDefinition(
        StationGenerationConstraints constraints)
    {
        if (constraints.RequireRedundantPaths == true
            && constraints.ForbidRedundantPaths == true)
        {
            throw new StationGenerationException(
                "Station constraints both require and forbid redundant paths.",
                ["RequireRedundantPaths and ForbidRedundantPaths cannot both be true."]);
        }

        if (constraints.PlannedCrewCount is <= 0)
        {
            throw new StationGenerationException(
                "Planned crew count must be positive.",
                [$"PlannedCrewCount={constraints.PlannedCrewCount}"]);
        }

        if (constraints.HydroponicsCapacityMultiplier <= 0)
        {
            throw new StationGenerationException(
                "Hydroponics capacity multiplier must be positive.",
                [$"HydroponicsCapacityMultiplier={constraints.HydroponicsCapacityMultiplier}"]);
        }

        if (constraints.MinimumFunctionalRoomCount is { } minimum
            && constraints.MaximumFunctionalRoomCount is { } maximum
            && minimum > maximum)
        {
            throw new StationGenerationException(
                "Station minimum room count exceeds maximum room count.",
                [$"minimum={minimum}, maximum={maximum}"]);
        }

        foreach (var required in constraints.RequiredRoomIds)
        {
            if (constraints.ForbiddenRoomIds.Contains(required))
            {
                throw new StationGenerationException(
                    $"Room '{required}' is both required and forbidden.",
                    [$"Constraint conflict for room '{required}'."]);
            }
        }
    }

    private static int MixSeed(int seed, int salt)
    {
        unchecked
        {
            var value = (uint)seed;
            value ^= (uint)salt + 0x9E3779B9u + (value << 6) + (value >> 2);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private sealed class SeededRandom
    {
        private uint _state;

        public SeededRandom(int seed)
        {
            _state = unchecked((uint)seed);
            if (_state == 0)
            {
                _state = 0x6D2B79F5u;
            }
        }

        public double NextDouble() =>
            NextUInt() / (double)uint.MaxValue;

        public double NextDouble(double minimum, double maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            return minimum + ((maximum - minimum) * NextDouble());
        }

        public int NextInt(int minimumInclusive, int maximumExclusive)
        {
            if (maximumExclusive <= minimumInclusive)
            {
                return minimumInclusive;
            }

            return minimumInclusive
                + (int)(NextUInt() % (uint)(maximumExclusive - minimumInclusive));
        }

        public T Choose<T>(IReadOnlyList<T> values)
        {
            if (values.Count == 0)
            {
                throw new InvalidOperationException("Cannot choose from an empty collection.");
            }

            return values[NextInt(0, values.Count)];
        }

        public IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> values)
        {
            var copy = values.ToList();

            for (var index = copy.Count - 1; index > 0; index--)
            {
                var swap = NextInt(0, index + 1);
                (copy[index], copy[swap]) = (copy[swap], copy[index]);
            }

            return copy;
        }

        private uint NextUInt()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }
    }
}
