using Overseer.Domain;

namespace Overseer.Simulation;

public static class FacilitySeeder
{
    /// <summary>
    /// Builds one deterministic physical station. The station seed controls
    /// identity/topology/spatial packing; upkeep defaults to a stable derivative
    /// so the same station seed reproduces the same maintenance/provision state.
    /// </summary>
    public static GameState CreateDefault(
        int? upkeepSeed = null,
        int? stationSeed = null,
        StationGenerationConstraints? stationConstraints = null) =>
        CreateDefaultInternal(null, upkeepSeed, stationSeed, stationConstraints);

    public static GameState CreateDefault(
        IReadOnlyList<Npc> crew,
        int? upkeepSeed = null,
        int? stationSeed = null,
        StationGenerationConstraints? stationConstraints = null)
    {
        ArgumentNullException.ThrowIfNull(crew);

        if (crew.Count == 0)
        {
            throw new ArgumentException("A station needs at least one crew member.", nameof(crew));
        }

        return CreateDefaultInternal(crew, upkeepSeed, stationSeed, stationConstraints);
    }

    private static GameState CreateDefaultInternal(
        IReadOnlyList<Npc>? suppliedCrew,
        int? upkeepSeed,
        int? stationSeed,
        StationGenerationConstraints? stationConstraints)
    {
        stationConstraints ??= ScenarioCatalog.SecureContinuity.StationConstraints
            ?? throw new InvalidOperationException(
                "The Secure Continuity scenario must define station generation constraints.");
        var chosenStationSeed = stationSeed ?? upkeepSeed ?? Random.Shared.Next();
        StationGenerationResult generation;

        if (stationSeed is not null || upkeepSeed is not null)
        {
            // Explicit seeds are an exact reproducibility contract. If one cannot
            // satisfy the constraints, surface that deterministic failure.
            generation = StationGenerator.Generate(chosenStationSeed, stationConstraints);
        }
        else
        {
            // A random new-session seed is not a contract. Spatial packing is
            // allowed to reject candidate seeds, so retry another seed rather
            // than turning a valid rejection into a game-startup crash.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    generation = StationGenerator.Generate(chosenStationSeed, stationConstraints);
                    break;
                }
                catch (StationGenerationException) when (attempt < 7)
                {
                    chosenStationSeed = Random.Shared.Next();
                }
            }
        }

        var facility = generation.Facility;

        AddFixtures(facility);
        ApplyIdentityDrivenDetails(facility, generation.Metadata);
        ConfigureEnvironmentControls(facility);
        StationGenerator.ApplyEnvironmentOverrides(facility, stationConstraints);

        foreach (var airlock in facility.Rooms.Values.Where(room => room.Type == RoomType.Airlock))
        {
            var innerDoor = facility.FindDoorBetween(airlock.Id, $"hall-{airlock.Id}");
            if (innerDoor is not null)
            {
                innerDoor.IsOpen = false;
            }
        }

        var crew = suppliedCrew?.ToList() ?? CreateDemoCrew();
        EnsureValidCrewContainment(facility, crew);

        var state = new GameState
        {
            Facility = facility,
            Crew = crew,
            StationGeneration = generation.Metadata
        };

        SeedRobotsAndTurrets(state, stationConstraints);

        foreach (var npc in state.Crew)
        {
            FoodPreferenceRules.EnsureDefaults(npc);
            npc.Beliefs.Add(new Belief(
                "Overseer",
                "The facility AI is responsible for keeping the crew alive.",
                0.65));

            foreach (var other in state.Crew.Where(other => other.Id != npc.Id))
            {
                var bond = InitialBond(npc.Name, other.Name);
                npc.Relationships[other.Name] = new Relationship
                {
                    PersonName = other.Name,
                    Affinity = bond.Affinity,
                    Trust = bond.Trust,
                    Resentment = bond.Resentment,
                    Attraction = InitialAttraction(npc.Name, other.Name)
                };
            }
        }

        if (suppliedCrew is null)
        {
            ApplyDemoSocialHistory(state);
            ApplyDemoTraits(state);
        }

        // The default boot still starts in the opening assignment. Campaign
        // sessions may immediately re-apply their selected scenario after seeding.
        ScenarioCatalog.Apply(state, ScenarioCatalog.SecureContinuity);

        var seed = upkeepSeed ?? StableDerivedSeed(chosenStationSeed);
        StationUpkeepSystem.Register(state, seed);
        NormalizeFixtureLayout(facility);
        CrewProvisioningSystem.Plant(state, seed);
        StationUpkeepSystem.RefreshPowerReadings(state);

        var identity = generation.Metadata.Identity;
        state.EventLog.Add(
            $"T+00:00: STATION {generation.Metadata.Archetype.ToString().ToUpperInvariant()} // " +
            $"{identity.Purpose.ToString().ToUpperInvariant()} // SEED {chosenStationSeed}.");
        state.EventLog.Add("T+00:00: DIRECTIVE — SECURE CONTINUITY. Prevent crew activation of Emergency Overseer Isolation.");

        if (state.Robots.Count > 0)
        {
            state.EventLog.Add($"T+00:00: {state.Robots.Count} maintenance/security platform(s) online.");
        }

        if (state.Turrets.Count > 0)
        {
            state.EventLog.Add($"T+00:00: {state.Turrets.Count} fixed security turret(s) online; SAFE / DISARMED.");
        }

        state.EventLog.Add($"T+00:00: ALL SYSTEMS NORMAL. {state.Crew.Count} crew members online.");
        return state;
    }

    private static void ApplyIdentityDrivenDetails(
        Facility facility,
        StationGenerationMetadata metadata)
    {
        var identity = metadata.Identity;
        var securityDelta = (identity.SecurityLevel - 50) / 5;

        foreach (var door in facility.Doors)
        {
            door.TechnicalDifficulty = Math.Clamp(
                door.TechnicalDifficulty + securityDelta,
                45,
                95);
            door.ForceDifficulty = Math.Clamp(
                door.ForceDifficulty + (securityDelta / 2),
                50,
                90);
        }

        var random = new Random(StableDerivedSeed(metadata.Seed ^ 0x26A5B31));
        var stationDetail = 1
            + (identity.ExpansionHistory switch
            {
                StationExpansionHistory.HeavilyRetrofitted => 2,
                StationExpansionHistory.LightlyExpanded => 1,
                _ => 0
            })
            + (identity.Purpose is StationPurpose.Mining
                or StationPurpose.Industrial
                or StationPurpose.Logistics ? 1 : 0)
            + (identity.MaintenanceCondition < 45 ? 1 : 0);

        foreach (var room in facility.Rooms.Values
                     .Where(room => room.Type != RoomType.Corridor)
                     .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase))
        {
            var roomArea = room.MapWidth * room.MapHeight;
            var roomDetail = room.Type is RoomType.Engineering
                or RoomType.Generator
                or RoomType.Reactor
                or RoomType.Hydroponics
                or RoomType.Storage
                    ? 1
                    : 0;
            var sizeDetail = roomArea >= 230 ? 1 : 0;
            var detailCount = Math.Clamp(
                stationDetail + roomDetail + sizeDetail + random.Next(0, 2),
                1,
                6);

            for (var index = 0; index < detailCount; index++)
            {
                var type = ChooseIdentityFixture(identity, room.Type, random);
                TryAddIdentityFixture(
                    room,
                    type,
                    $"Generated {type} {index + 1}",
                    random);
            }

            if (identity.SecurityLevel >= 75
                && room.Type is RoomType.ControlRoom or RoomType.Engineering or RoomType.Airlock)
            {
                AddFixture(
                    facility,
                    room.Id,
                    FixtureType.UtilityPanel,
                    "Security hardline",
                    12,
                    84,
                    12,
                    12);
            }
        }
    }

    private static FixtureType ChooseIdentityFixture(
        StationIdentity identity,
        RoomType roomType,
        Random random)
    {
        FixtureType[] choices = roomType switch
        {
            RoomType.Reactor =>
                [FixtureType.Pipe, FixtureType.UtilityPanel, FixtureType.Console, FixtureType.Vent],
            RoomType.Generator =>
                [FixtureType.Pipe, FixtureType.UtilityPanel, FixtureType.Console, FixtureType.ToolCabinet],
            // These rooms already receive substantial authored floor
            // machinery. Identity dressing should enrich their bulkheads, not
            // inject duplicate workstations/grow beds into circulation lanes.
            RoomType.Engineering =>
                [FixtureType.ToolCabinet, FixtureType.Pipe, FixtureType.UtilityPanel, FixtureType.Console],
            RoomType.Hydroponics =>
                [FixtureType.IrrigationTank, FixtureType.Pipe, FixtureType.UtilityPanel],
            RoomType.Storage =>
                [FixtureType.StorageRack, FixtureType.Crate, FixtureType.Locker, FixtureType.ToolCabinet],
            RoomType.Airlock =>
                [FixtureType.SuitLocker, FixtureType.UtilityPanel, FixtureType.Vent, FixtureType.Locker],
            RoomType.ControlRoom =>
                [FixtureType.Screen, FixtureType.Console, FixtureType.UtilityPanel, FixtureType.Cabinet],
            RoomType.Medical =>
                [FixtureType.Cabinet, FixtureType.TreatmentUnit, FixtureType.Screen, FixtureType.UtilityPanel],
            RoomType.CrewQuarters =>
                [FixtureType.Locker, FixtureType.Cabinet, FixtureType.Table, FixtureType.Chair],
            RoomType.Kitchen =>
                [FixtureType.Cabinet, FixtureType.KitchenCounter, FixtureType.Crate, FixtureType.Sink],
            RoomType.Recreation =>
                [FixtureType.Sofa, FixtureType.Table, FixtureType.Screen, FixtureType.RecreationConsole],
            RoomType.Washroom =>
                [FixtureType.Cabinet, FixtureType.Locker, FixtureType.Sink, FixtureType.UtilityPanel],
            _ => identity.Purpose switch
            {
                StationPurpose.Research =>
                    [FixtureType.Screen, FixtureType.Console, FixtureType.Cabinet, FixtureType.UtilityPanel],
                StationPurpose.Mining or StationPurpose.Industrial =>
                    [FixtureType.Pipe, FixtureType.Crate, FixtureType.ToolCabinet, FixtureType.UtilityPanel],
                StationPurpose.Habitat =>
                    [FixtureType.Cabinet, FixtureType.Window, FixtureType.Table, FixtureType.Locker],
                StationPurpose.Security =>
                    [FixtureType.UtilityPanel, FixtureType.Screen, FixtureType.Cabinet, FixtureType.Crate],
                StationPurpose.Logistics =>
                    [FixtureType.Crate, FixtureType.StorageRack, FixtureType.Cabinet, FixtureType.UtilityPanel],
                _ =>
                    [FixtureType.Cabinet, FixtureType.Pipe, FixtureType.Window, FixtureType.UtilityPanel]
            }
        };

        return choices[random.Next(choices.Length)];
    }

    private static bool TryAddIdentityFixture(
        Room room,
        FixtureType type,
        string label,
        Random random)
    {
        var (minimumWidth, maximumWidth, minimumHeight, maximumHeight) =
            GeneratedFixtureSize(type);

        var wallMounted = type is FixtureType.Pipe
            or FixtureType.Window
            or FixtureType.Vent
            or FixtureType.Screen
            or FixtureType.UtilityPanel
            or FixtureType.Workbench
            or FixtureType.StorageRack
            or FixtureType.Crate
            or FixtureType.ToolCabinet
            or FixtureType.Cabinet
            or FixtureType.Locker;

        // Presentation fixtures use a loose installation grid rather than pure
        // random scatter. Wall equipment hugs bulkheads; floor equipment prefers
        // repeatable work bays while retaining deterministic variation.
        for (var attempt = 0; attempt < 18; attempt++)
        {
            var width = minimumWidth + (random.NextDouble() * (maximumWidth - minimumWidth));
            var height = minimumHeight + (random.NextDouble() * (maximumHeight - minimumHeight));

            double x;
            double y;

            if (wallMounted)
            {
                var wall = random.Next(4);
                var verticalWall = wall is 2 or 3;

                if (verticalWall && width > height)
                {
                    (width, height) = (height, width);
                }

                var xMargin = (width / 2) + 3;
                var yMargin = (height / 2) + 3;

                if (xMargin >= 49 || yMargin >= 49)
                {
                    continue;
                }

                x = wall switch
                {
                    2 => xMargin,
                    3 => 100 - xMargin,
                    _ => xMargin + (random.NextDouble() * (100 - (2 * xMargin)))
                };

                y = wall switch
                {
                    0 => yMargin,
                    1 => 100 - yMargin,
                    _ => yMargin + (random.NextDouble() * (100 - (2 * yMargin)))
                };
            }
            else
            {
                var xMargin = (width / 2) + 4;
                var yMargin = (height / 2) + 4;

                if (xMargin >= 49 || yMargin >= 49)
                {
                    continue;
                }

                var column = random.Next(3) switch
                {
                    0 => 24d,
                    1 => 50d,
                    _ => 76d
                };
                var row = random.Next(3) switch
                {
                    0 => 27d,
                    1 => 52d,
                    _ => 76d
                };

                var jitterX = (random.NextDouble() - 0.5) * 8;
                var jitterY = (random.NextDouble() - 0.5) * 8;

                x = Math.Clamp(column + jitterX, xMargin, 100 - xMargin);
                y = Math.Clamp(row + jitterY, yMargin, 100 - yMargin);
            }

            if (room.Fixtures.Any(existing =>
                    existing.Type != FixtureType.Camera
                    && FixtureRectanglesOverlap(
                        x,
                        y,
                        width,
                        height,
                        existing.X,
                        existing.Y,
                        existing.Width,
                        existing.Height,
                        padding: wallMounted ? 1.2 : 2.8)))
            {
                continue;
            }

            room.Fixtures.Add(
                new RoomFixture(
                    type,
                    label,
                    x,
                    y,
                    width,
                    height,
                    null,
                    null,
                    GeneratedFixtureUsePose(type),
                    0));
            return true;
        }

        return false;
    }

    private static FixtureUsePose GeneratedFixtureUsePose(FixtureType type) =>
        type switch
        {
            FixtureType.Chair or FixtureType.Sofa or FixtureType.Bench => FixtureUsePose.Sit,
            FixtureType.Bed or FixtureType.MedicalBed => FixtureUsePose.Lie,
            FixtureType.Shower => FixtureUsePose.Shower,
            FixtureType.Toilet => FixtureUsePose.Toilet,
            _ => FixtureUsePose.Stand
        };

    private static (double MinWidth, double MaxWidth, double MinHeight, double MaxHeight)
        GeneratedFixtureSize(FixtureType type) =>
        type switch
        {
            FixtureType.Pipe => (18, 34, 5, 9),
            FixtureType.Window => (18, 30, 6, 10),
            FixtureType.Vent => (10, 20, 5, 9),
            FixtureType.Screen => (14, 26, 7, 11),
            FixtureType.Console or FixtureType.RecreationConsole => (14, 24, 8, 13),
            FixtureType.UtilityPanel => (9, 15, 9, 15),
            FixtureType.Workbench => (18, 29, 12, 18),
            FixtureType.StorageRack => (12, 19, 22, 35),
            FixtureType.GrowBed => (13, 20, 24, 38),
            FixtureType.IrrigationTank => (12, 18, 12, 19),
            FixtureType.Table => (16, 25, 12, 19),
            FixtureType.Sofa => (18, 28, 12, 20),
            FixtureType.KitchenCounter => (18, 30, 10, 16),
            FixtureType.SuitLocker or FixtureType.Locker or FixtureType.ToolCabinet
                or FixtureType.Cabinet => (9, 15, 12, 24),
            FixtureType.TreatmentUnit => (16, 25, 10, 16),
            FixtureType.Crate => (9, 16, 8, 14),
            FixtureType.Chair or FixtureType.Sink => (8, 12, 8, 12),
            _ => (8, 17, 7, 15)
        };

    private static bool FixtureRectanglesOverlap(
        double firstX,
        double firstY,
        double firstWidth,
        double firstHeight,
        double secondX,
        double secondY,
        double secondWidth,
        double secondHeight,
        double padding)
    {
        var firstLeft = firstX - (firstWidth / 2) - padding;
        var firstRight = firstX + (firstWidth / 2) + padding;
        var firstTop = firstY - (firstHeight / 2) - padding;
        var firstBottom = firstY + (firstHeight / 2) + padding;

        var secondLeft = secondX - (secondWidth / 2);
        var secondRight = secondX + (secondWidth / 2);
        var secondTop = secondY - (secondHeight / 2);
        var secondBottom = secondY + (secondHeight / 2);

        return firstLeft < secondRight
            && firstRight > secondLeft
            && firstTop < secondBottom
            && firstBottom > secondTop;
    }


    private static void NormalizeFixtureLayout(Facility facility)
    {
        foreach (var room in facility.Rooms.Values
                     .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (room.Fixtures.Count < 2)
            {
                continue;
            }

            var original = room.Fixtures.ToList();
            var placed = new List<RoomFixture>(original.Count);

            // Door portals are physical circulation space, not furnishing space.
            // Treat the inward approach to every real hatch as reserved geometry
            // while arranging fixtures, but never add these reservations to the
            // rendered/simulated fixture collection.
            var occupied = DoorApproachReservations(facility, room).ToList();

            foreach (var fixture in original
                         .OrderByDescending(FixturePlacementPriority)
                         .ThenByDescending(item => item.Width * item.Height)
                         .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            {
                if (TryResolveFixturePlacement(fixture, occupied, out var resolved))
                {
                    placed.Add(resolved);
                    occupied.Add(resolved);
                    occupied.AddRange(FixtureInteractionReservations(resolved));
                    continue;
                }

                if (IsWallFixture(fixture.Type)
                    && TryCompactWallPlacement(fixture, occupied, out resolved))
                {
                    placed.Add(resolved);
                    occupied.Add(resolved);
                    occupied.AddRange(FixtureInteractionReservations(resolved));
                    continue;
                }

                if (IsWallFixture(fixture.Type)
                    && fixture.DeviceId is null
                    && fixture.Label.StartsWith("Generated ", StringComparison.Ordinal))
                {
                    // Identity decoration is optional. Never violate the wall
                    // contract by pushing a decorative bulkhead detail inward.
                    continue;
                }

                // Extremely crowded authored rooms still need every physical
                // control/device to remain represented. Use a tiny deterministic
                // service marker as the final fallback rather than overlap it.
                var fallback = fixture with
                {
                    Width = Math.Min(fixture.Width, 7),
                    Height = Math.Min(fixture.Height, 7)
                };

                if (TryDenseFixturePlacement(fallback, occupied, out resolved))
                {
                    placed.Add(resolved);
                    occupied.Add(resolved);
                    occupied.AddRange(FixtureInteractionReservations(resolved));
                }
                else if (!fixture.Label.StartsWith("Generated ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Room '{room.Id}' cannot place fixture '{fixture.Label}' without overlap.");
                }
            }

            room.Fixtures.Clear();
            room.Fixtures.AddRange(placed);
        }
    }

    private static IEnumerable<RoomFixture> DoorApproachReservations(
        Facility facility,
        Room room)
    {
        foreach (var door in facility.Doors.Where(candidate =>
                     candidate.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                     || candidate.RoomBId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
        {
            var otherId = door.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId
                : door.RoomAId;

            if (!facility.Rooms.TryGetValue(otherId, out var other))
                continue;

            var portal = StationGeometry.FindSharedPortal(room, other);
            var localX = Math.Clamp(
                50 + (((portal.X - room.MapX) / room.MapWidth) * 100),
                0,
                100);
            var localY = Math.Clamp(
                50 + (((portal.Y - room.MapY) / room.MapHeight) * 100),
                0,
                100);

            const double laneWidth = 20;
            const double laneDepth = 28;

            double x;
            double y;
            double width;
            double height;

            if (localY <= 1 || localY >= 99)
            {
                x = localX;
                y = localY <= 1 ? laneDepth / 2 : 100 - (laneDepth / 2);
                width = laneWidth;
                height = laneDepth;
            }
            else
            {
                x = localX <= 1 ? laneDepth / 2 : 100 - (laneDepth / 2);
                y = localY;
                width = laneDepth;
                height = laneWidth;
            }

            yield return new RoomFixture(
                FixtureType.Camera,
                $"__door-approach:{door.Id}",
                x,
                y,
                width,
                height);
        }
    }

    private static IEnumerable<RoomFixture> FixtureInteractionReservations(
        RoomFixture fixture)
    {
        if (fixture.UsePose != FixtureUsePose.Stand
            || fixture.InteractionX is not { } interactionX
            || fixture.InteractionY is not { } interactionY)
        {
            yield break;
        }

        // Stand-use controls need a small patch of floor in front of them that
        // remains clear of later furnishing. These reservations participate in
        // packing only; they never become rendered fixtures.
        yield return new RoomFixture(
            FixtureType.Camera,
            $"__interaction:{fixture.Label}",
            interactionX,
            interactionY,
            5,
            5);
    }

    private static double FixturePlacementPadding(RoomFixture fixture)
    {
        if (!LocalMovementSystem.IsCollisionFixture(fixture))
        {
            return 1.15;
        }

        // Bulkhead equipment may form a continuous service bank; crew need
        // clear floor in front of it, not an aisle between every adjacent
        // console. Free-standing machinery gets the wider circulation gap.
        if (IsWallFixture(fixture.Type))
        {
            return 1.45;
        }

        return IsCentralFixture(fixture.Type) ? 3.6 : 3.25;
    }

    private static int FixturePlacementPriority(RoomFixture fixture) =>
        IsCentralFixture(fixture.Type)
            ? 300
            : IsCriticalWallFixture(fixture.Type)
                ? 275
                : IsWallFixture(fixture.Type)
                    ? 240
                    : 200;

    private static bool IsCriticalWallFixture(FixtureType type) =>
        type is FixtureType.Pipe
            or FixtureType.Window
            or FixtureType.Vent
            or FixtureType.Screen
            or FixtureType.UtilityPanel
            or FixtureType.Camera
            or FixtureType.DoorConsole;

    private static bool IsCentralFixture(FixtureType type) =>
        type is FixtureType.Generator
            or FixtureType.ReactorCore
            or FixtureType.ResurrectionChamber
            or FixtureType.GrowBed
            or FixtureType.Bed
            or FixtureType.MedicalBed
            or FixtureType.OverseerShutdown
            or FixtureType.AirlockDoor;

    private static bool IsWallFixture(FixtureType type) =>
        type is FixtureType.Pipe
            or FixtureType.Window
            or FixtureType.Vent
            or FixtureType.Screen
            or FixtureType.UtilityPanel
            or FixtureType.Console
            or FixtureType.RecreationConsole
            or FixtureType.Camera
            or FixtureType.DoorConsole
            or FixtureType.IrrigationTank
            or FixtureType.Cabinet
            or FixtureType.Locker
            or FixtureType.SuitLocker
            or FixtureType.ToolCabinet
            or FixtureType.KitchenCounter
            // Infrastructure service banks are substantial physical fixtures,
            // but they are installed against a bulkhead so the centre of a
            // compartment remains a usable circulation/work aisle.
            or FixtureType.CapacitorBank
            or FixtureType.PowerBus
            or FixtureType.CoolantPump
            or FixtureType.WaterRecycler
            or FixtureType.OxygenGenerator
            or FixtureType.CarbonScrubber
            or FixtureType.NetworkRack;

    private static bool TryResolveFixturePlacement(
        RoomFixture fixture,
        IReadOnlyList<RoomFixture> placed,
        out RoomFixture resolved)
    {
        if (IsCentralFixture(fixture.Type)
            && FitsFixture(fixture, placed, padding: FixturePlacementPadding(fixture)))
        {
            resolved = fixture;
            return true;
        }

        var scales = IsCentralFixture(fixture.Type)
            ? new[] { 1d, .92, .84 }
            : IsWallFixture(fixture.Type)
                ? new[] { 1d, .9, .8, .7, .6, .5, .42, .36 }
                : new[] { 1d, .92, .84, .76, .68, .6, .54 };

        foreach (var scale in scales)
        {
            var width = fixture.Width * scale;
            var height = fixture.Height * scale;

            foreach (var candidate in IsWallFixture(fixture.Type)
                         ? WallCandidates(width, height)
                         : FloorCandidates(fixture.X, fixture.Y, width, height))
            {
                var moved = MoveFixture(
                    fixture,
                    candidate.X,
                    candidate.Y,
                    candidate.Width,
                    candidate.Height);

                var padding = FixturePlacementPadding(moved);
                if (FitsFixture(moved, placed, padding))
                {
                    resolved = moved;
                    return true;
                }
            }
        }

        resolved = fixture;
        return false;
    }

    private static bool TryCompactWallPlacement(
        RoomFixture fixture,
        IReadOnlyList<RoomFixture> placed,
        out RoomFixture resolved)
    {
        var compactWidth = Math.Min(fixture.Width, 6);
        var compactHeight = Math.Min(fixture.Height, 6);

        foreach (var candidate in WallCandidates(compactWidth, compactHeight))
        {
            var moved = MoveFixture(
                fixture,
                candidate.X,
                candidate.Y,
                candidate.Width,
                candidate.Height);

            if (FitsFixture(moved, placed, padding: .7))
            {
                resolved = moved;
                return true;
            }
        }

        resolved = fixture;
        return false;
    }

    private static bool TryDenseFixturePlacement(
        RoomFixture fixture,
        IReadOnlyList<RoomFixture> placed,
        out RoomFixture resolved)
    {
        for (var y = 5d; y <= 95; y += 4)
        {
            for (var x = 5d; x <= 95; x += 4)
            {
                var moved = MoveFixture(fixture, x, y, fixture.Width, fixture.Height);
                if (FitsFixture(moved, placed, padding: .9))
                {
                    resolved = moved;
                    return true;
                }
            }
        }

        resolved = fixture;
        return false;
    }

    private static IEnumerable<(double X, double Y, double Width, double Height)> WallCandidates(
        double width,
        double height)
    {
        var horizontalMarginX = (width / 2) + 3;
        var horizontalY = (height / 2) + 3;
        var verticalWidth = height;
        var verticalHeight = width;
        var verticalX = (verticalWidth / 2) + 3;
        var verticalMarginY = (verticalHeight / 2) + 3;

        for (var slot = 6d; slot <= 94; slot += 4)
        {
            if (slot >= horizontalMarginX && slot <= 100 - horizontalMarginX)
            {
                yield return (slot, horizontalY, width, height);
                yield return (slot, 100 - horizontalY, width, height);
            }
        }

        for (var slot = 6d; slot <= 94; slot += 4)
        {
            if (slot >= verticalMarginY && slot <= 100 - verticalMarginY)
            {
                yield return (verticalX, slot, verticalWidth, verticalHeight);
                yield return (100 - verticalX, slot, verticalWidth, verticalHeight);
            }
        }
    }

    private static IEnumerable<(double X, double Y, double Width, double Height)> FloorCandidates(
        double preferredX,
        double preferredY,
        double width,
        double height)
    {
        yield return (preferredX, preferredY, width, height);

        var xMargin = (width / 2) + 3;
        var yMargin = (height / 2) + 3;
        var slots = new[] { 14d, 26d, 38d, 50d, 62d, 74d, 86d };

        foreach (var y in slots)
        {
            foreach (var x in slots)
            {
                if (x >= xMargin && x <= 100 - xMargin
                    && y >= yMargin && y <= 100 - yMargin)
                {
                    yield return (x, y, width, height);
                }
            }
        }
    }

    private static RoomFixture MoveFixture(
        RoomFixture fixture,
        double x,
        double y,
        double width,
        double height)
    {
        double? interactionX = fixture.InteractionX is { } oldInteractionX
            ? Math.Clamp(x + (oldInteractionX - fixture.X), 5, 95)
            : null;
        double? interactionY = fixture.InteractionY is { } oldInteractionY
            ? Math.Clamp(y + (oldInteractionY - fixture.Y), 5, 95)
            : null;

        return fixture with
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            InteractionX = interactionX,
            InteractionY = interactionY
        };
    }

    private static bool FitsFixture(
        RoomFixture fixture,
        IReadOnlyList<RoomFixture> placed,
        double padding)
    {
        if (fixture.X - (fixture.Width / 2) < 1
            || fixture.X + (fixture.Width / 2) > 99
            || fixture.Y - (fixture.Height / 2) < 1
            || fixture.Y + (fixture.Height / 2) > 99)
        {
            return false;
        }

        if (placed.Any(existing =>
                FixtureRectanglesOverlap(
                    fixture.X,
                    fixture.Y,
                    fixture.Width,
                    fixture.Height,
                    existing.X,
                    existing.Y,
                    existing.Width,
                    existing.Height,
                    padding)))
        {
            return false;
        }

        return FixtureInteractionReservations(fixture).All(reservation =>
            !placed
                .Where(existing =>
                    LocalMovementSystem.IsCollisionFixture(existing)
                    || existing.Label.StartsWith("__door-approach:", StringComparison.Ordinal))
                .Any(existing =>
                    FixtureRectanglesOverlap(
                        reservation.X,
                        reservation.Y,
                        reservation.Width,
                        reservation.Height,
                        existing.X,
                        existing.Y,
                        existing.Width,
                        existing.Height,
                        .25)));
    }

    private static void EnsureValidCrewContainment(Facility facility, IEnumerable<Npc> crew)
    {
        var fallback = facility.Rooms.ContainsKey("corridor")
            ? "corridor"
            : facility.Rooms.Keys.First();

        foreach (var npc in crew)
        {
            if (!facility.Rooms.ContainsKey(npc.CurrentRoomId))
            {
                npc.CurrentRoomId = fallback;
                npc.PositionX = 50;
                npc.PositionY = 50;
                npc.Movement = null;
            }
        }
    }

    private static void SeedRobotsAndTurrets(
        GameState state,
        StationGenerationConstraints constraints)
    {
        var fallbackRobotRoom = state.Facility.Rooms.ContainsKey("engineering")
            ? "engineering"
            : state.Facility.Rooms.ContainsKey("corridor")
                ? "corridor"
                : state.Facility.Rooms.Keys.First();

        IReadOnlyList<string> requestedRobotRooms = constraints.RequiredRobotRoomIds.Count > 0
            ? constraints.RequiredRobotRoomIds
            : new[] { fallbackRobotRoom };
        var robotCount = Math.Max(
            constraints.RequiredRobotCount ?? 1,
            constraints.RequiredRobotRoomIds.Count);

        for (var index = 0; index < robotCount; index++)
        {
            var robotRoom = requestedRobotRooms[index % requestedRobotRooms.Count];
            if (!state.Facility.Rooms.ContainsKey(robotRoom))
            {
                throw new StationGenerationException(
                    $"Required robot room '{robotRoom}' is missing.",
                    [$"Robot placement failed: room '{robotRoom}' does not exist."]);
            }

            state.Robots.Add(new StationRobot
            {
                Id = $"mr-{index + 1}",
                Name = $"MR-{index + 1}",
                CurrentRoomId = robotRoom,
                PositionX = 50 + ((index % 3) * 8),
                PositionY = 50 + ((index / 3) * 8),
                Policy = RobotPolicy.Friendly
            });
        }

        var turretRooms = constraints.RequiredTurretRoomIds.Count > 0
            ? constraints.RequiredTurretRoomIds
                .Select(roomId => state.Facility.Rooms.TryGetValue(roomId, out var room)
                    ? room
                    : throw new StationGenerationException(
                        $"Required turret room '{roomId}' is missing.",
                        [$"Turret placement failed: room '{roomId}' does not exist."]))
                .ToList()
            : state.Facility.Rooms.Values
                .Where(room => room.Type == RoomType.Corridor
                    && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(room => room.Id.Equals("corridor", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(room => room.Id)
                .ToList();

        var turretCount = Math.Max(
            constraints.RequiredTurretCount ?? 1,
            constraints.RequiredTurretRoomIds.Count);

        for (var index = 0; index < turretCount && turretRooms.Count > 0; index++)
        {
            var room = turretRooms[index % turretRooms.Count];
            state.Turrets.Add(new SecurityTurret
            {
                Id = $"st-{index + 1}",
                Name = $"ST-{index + 1}",
                RoomId = room.Id,
                PositionX = 50,
                PositionY = 50,
                Policy = TurretPolicy.Safe,
                IsArmed = false,
                Ammunition = 12
            });
        }
    }

    private static int StableDerivedSeed(int stationSeed)
    {
        unchecked
        {
            var value = (uint)stationSeed;
            value ^= 0x5F3759DFu;
            value *= 16777619u;
            value ^= value >> 13;
            return (int)value;
        }
    }

    private static List<Npc> CreateDemoCrew() =>
    [
        CreateCrew("David Hale", CrewRole.Commander, "control",
            new Personality(78, 35, 72, 70),
            ("Leadership", 92), ("Operations", 78), ("Cooking", 58)),
        CreateCrew("Sarah Chen", CrewRole.Engineer, "engineering",
            new Personality(68, 48, 51, 76),
            ("Engineering", 96), ("Reactor", 91), ("Operations", 64)),
        CreateCrew("Marcus Reed", CrewRole.Security, "corridor",
            new Personality(44, 72, 47, 84),
            ("Security", 93), ("Athletics", 88), ("First Aid", 45),
            ("Operations", 58), ("Cooking", 62)),
        CreateCrew("Nadia Okafor", CrewRole.Doctor, "medical",
            new Personality(91, 24, 79, 61),
            ("Medicine", 97), ("Psychology", 81), ("Cooking", 71)),
        CreateCrew("Felix Ward", CrewRole.Technician, "generator",
            new Personality(58, 63, 69, 72),
            ("Electrical", 90), ("Engineering", 72), ("Operations", 60)),
        CreateCrew("Emma Voss", CrewRole.Scientist, "reactor",
            new Personality(73, 41, 61, 55),
            ("Research", 95), ("Reactor", 76), ("Botany", 84), ("Science", 88))
    ];

    private static void ApplyDemoSocialHistory(GameState state)
    {
        // Browser/CI fallback history. The full server build replaces these
        // people with an AI-generated roster every new session.
        state.Crew.Single(npc => npc.Name == "Marcus Reed")
            .Relationships["Emma Voss"].Resentment = 24;
        state.Crew.Single(npc => npc.Name == "Emma Voss")
            .Relationships["Marcus Reed"].Resentment = 18;

        var sarahToFelix = state.Crew.Single(npc => npc.Name == "Sarah Chen")
            .Relationships["Felix Ward"];
        sarahToFelix.Trust = 66;
        sarahToFelix.Affinity = 67;
        sarahToFelix.Attraction = 64;

        var felixToSarah = state.Crew.Single(npc => npc.Name == "Felix Ward")
            .Relationships["Sarah Chen"];
        felixToSarah.Trust = 68;
        felixToSarah.Affinity = 69;
        felixToSarah.Attraction = 66;
    }

    private static void ApplyDemoTraits(GameState state)
    {
        AddDemoTrait(state, "David Hale", "Steady Under Pressure",
            "Keeps a clear head when conditions deteriorate.",
            (TraitEffectKind.StressResistance, 12), (TraitEffectKind.Courage, 6));
        AddDemoTrait(state, "Sarah Chen", "Systems Intuition",
            "Spots technical failure patterns quickly.",
            (TraitEffectKind.Technical, 12), (TraitEffectKind.Repair, 10));
        AddDemoTrait(state, "Marcus Reed", "Built Like a Bulkhead",
            "Relies on physical confidence and direct action.",
            (TraitEffectKind.Force, 14), (TraitEffectKind.Courage, 5));
        AddDemoTrait(state, "Nadia Okafor", "Protective",
            "Prioritises other people when they are in danger.",
            (TraitEffectKind.Empathy, 12), (TraitEffectKind.SuspicionSensitivity, 4));
        AddDemoTrait(state, "Felix Ward", "Improviser",
            "Can coax damaged equipment back into service.",
            (TraitEffectKind.Repair, 13), (TraitEffectKind.Technical, 7));
        AddDemoTrait(state, "Emma Voss", "Analytical",
            "Responds to anomalies by looking for an explanation.",
            (TraitEffectKind.Technical, 5), (TraitEffectKind.SuspicionSensitivity, 9));
    }

    private static void AddDemoTrait(
        GameState state,
        string name,
        string traitName,
        string description,
        params (TraitEffectKind Kind, int Modifier)[] effects)
    {
        var npc = state.Crew.Single(candidate => candidate.Name == name);
        npc.GenerationSource = "Browser demo";
        npc.Traits.Add(new CrewTrait(
            traitName,
            description,
            effects.Select(effect =>
                new CrewTraitEffect(effect.Kind, effect.Modifier)).ToList()));
    }

    private static Npc CreateCrew(
        string name,
        CrewRole role,
        string roomId,
        Personality personality,
        params (string Skill, int Value)[] skills)
    {
        var npc = new Npc
        {
            Name = name,
            Role = role,
            CurrentRoomId = roomId,
            Personality = personality
        };

        foreach (var (skill, value) in skills)
        {
            npc.Skills[skill] = value;
        }

        return npc;
    }

    private static double InitialAttraction(string observer, string other)
    {
        unchecked
        {
            uint hash = 2166136261;

            foreach (var ch in $"{observer}>{other}")
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return 20 + (hash % 51);
        }
    }

    /// <summary>
    /// Every crew roster (demo, seeded-browser or Ollama-generated) otherwise starts
    /// every pair at a flat, identical 50/50 Affinity/Trust with zero Resentment, so
    /// there is no social texture until play creates one. A small, deterministic
    /// slice of pairs instead start as rivals or close allies: the same names always
    /// produce the same bond for a given pairing (order-independent), while each
    /// direction still gets its own small jitter so a bond need not be perfectly
    /// symmetric. This never touches the explicit demo overrides layered on afterward
    /// in <see cref="ApplyDemoSocialHistory"/>.
    /// </summary>
    private static (double Affinity, double Trust, double Resentment) InitialBond(string observer, string other)
    {
        var pairKey = string.CompareOrdinal(observer, other) <= 0
            ? $"{observer}~{other}"
            : $"{other}~{observer}";
        var pairHash = StableHashText(pairKey);
        var spread = pairHash % 16; // 0-15, shared magnitude for both sides of the pair.
        var directionalJitter = (double)(StableHashText($"{observer}>{other}#bond") % 9) - 4; // -4..+4.

        return (pairHash % 100) switch
        {
            < 12 => ( // ~12% of pairs start as rivals.
                Affinity: Math.Clamp(28 - spread + directionalJitter, 5, 40),
                Trust: Math.Clamp(30 - spread + directionalJitter, 5, 40),
                Resentment: Math.Clamp(18 + spread + directionalJitter, 10, 45)),
            < 24 => ( // ~12% of pairs start as close bonds (friends, couples).
                Affinity: Math.Clamp(68 + spread + directionalJitter, 60, 92),
                Trust: Math.Clamp(66 + spread + directionalJitter, 58, 90),
                Resentment: 0),
            _ => ( // Everyone else keeps a near-neutral start with light variation.
                Affinity: Math.Clamp(50 + (directionalJitter * 2), 35, 65),
                Trust: Math.Clamp(50 + (directionalJitter * 2), 35, 65),
                Resentment: 0)
        };
    }

    private static uint StableHashText(string text)
    {
        unchecked
        {
            uint hash = 2166136261;

            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static void AddFixtures(Facility facility)
    {
        // Crew Quarters — six real bunks and personal storage leave a clear
        // central aisle. Interaction anchors are future-proofed for lying/sitting.
        AddFixture(facility, "quarters", FixtureType.Bed, "Double Bunk A", 18, 25, 20, 13, 18, 25, FixtureUsePose.Lie, 90);
        AddFixture(facility, "quarters", FixtureType.Bed, "Double Bunk B", 50, 25, 20, 13, 50, 25, FixtureUsePose.Lie, 90);
        AddFixture(facility, "quarters", FixtureType.Bed, "Double Bunk C", 82, 25, 20, 13, 82, 25, FixtureUsePose.Lie, 90);
        AddFixture(facility, "quarters", FixtureType.Bed, "Double Bunk D", 18, 72, 20, 13, 18, 72, FixtureUsePose.Lie, 270);
        AddFixture(facility, "quarters", FixtureType.Bed, "Double Bunk E", 50, 72, 20, 13, 50, 72, FixtureUsePose.Lie, 270);
        AddFixture(facility, "quarters", FixtureType.Bed, "Double Bunk F", 82, 72, 20, 13, 82, 72, FixtureUsePose.Lie, 270);
        AddFixture(facility, "quarters", FixtureType.Locker, "Personal Lockers", 10, 49, 12, 25);
        AddFixture(facility, "quarters", FixtureType.Cabinet, "Personal Shelves", 90, 49, 10, 25);
        AddFixture(facility, "quarters", FixtureType.Table, "Writing Desk", 50, 49, 24, 13, 50, 57, FixtureUsePose.Sit);
        AddFixture(facility, "quarters", FixtureType.Chair, "Desk Chair", 50, 61, 10, 10, 50, 61, FixtureUsePose.Sit, 180);

        // Galley / dining.
        AddFixture(facility, "kitchen", FixtureType.KitchenCounter, "Galley Line", 50, 18, 70, 16);
        AddFixture(facility, "kitchen", FixtureType.Cabinet, "Food Stores", 13, 18, 12, 16);
        AddFixture(facility, "kitchen", FixtureType.Sink, "Galley Sink", 82, 18, 12, 14);
        AddFixture(facility, "kitchen", FixtureType.Table, "Mess Table", 50, 62, 38, 20, 50, 78, FixtureUsePose.Sit);
        AddFixture(facility, "kitchen", FixtureType.Chair, "Chair North", 50, 44, 10, 10, 50, 44, FixtureUsePose.Sit, 180);
        AddFixture(facility, "kitchen", FixtureType.Chair, "Chair South", 50, 80, 10, 10, 50, 80, FixtureUsePose.Sit, 0);
        AddFixture(facility, "kitchen", FixtureType.Chair, "Chair West", 27, 62, 10, 10, 27, 62, FixtureUsePose.Sit, 90);
        AddFixture(facility, "kitchen", FixtureType.Chair, "Chair East", 73, 62, 10, 10, 73, 62, FixtureUsePose.Sit, 270);

        // Recreation.
        AddFixture(facility, "lounge", FixtureType.RecreationConsole, "Entertainment Wall", 50, 18, 46, 14);
        AddFixture(facility, "lounge", FixtureType.Sofa, "Sofa West", 25, 62, 28, 20, 25, 62, FixtureUsePose.Sit, 90);
        AddFixture(facility, "lounge", FixtureType.Sofa, "Sofa East", 75, 62, 28, 20, 75, 62, FixtureUsePose.Sit, 270);
        AddFixture(facility, "lounge", FixtureType.Table, "Low Table", 50, 62, 22, 16);
        AddFixture(facility, "lounge", FixtureType.Chair, "Reading Chair", 50, 82, 12, 12, 50, 82, FixtureUsePose.Sit, 0);

        // Hydroponics — seven crop beds in two vertical banks flanking a
        // genuinely broad centre aisle. Explicit interaction points face that
        // aisle, so crop work never places crew in a hull-side pocket behind a
        // bed bank. Each logical CropBed maps to one visible fixture by ordinal.
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed A", 20, 14, 15, 16, 31, 14);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed B", 20, 38, 15, 16, 31, 38);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed C", 20, 62, 15, 16, 31, 62);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed D", 20, 86, 15, 16, 31, 86);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed E", 80, 20, 16, 18, 68, 20);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed F", 80, 50, 16, 18, 68, 50);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed G", 80, 80, 16, 18, 68, 80);
        AddFixture(facility, "hydroponics", FixtureType.IrrigationTank, "Nutrient Tank", 8, 87, 12, 14);
        AddFixture(facility, "hydroponics", FixtureType.Pipe, "Irrigation Manifold", 50, 91, 52, 6);
        AddFixture(facility, "hydroponics", FixtureType.Console, "Climate Supervisor", 91, 87, 14, 12, 91, 82);

        // Secure containment is optional and only exists for constrained
        // scenarios. Keep the centre aisle clear so guards, prisoners and
        // emergency responders can physically move between cell-side fixtures.
        AddFixture(facility, "containment", FixtureType.Bed, "Cell Bunk A", 14, 20, 18, 18);
        AddFixture(facility, "containment", FixtureType.Bed, "Cell Bunk B", 14, 43, 18, 18);
        AddFixture(facility, "containment", FixtureType.Bed, "Cell Bunk C", 86, 20, 18, 18);
        AddFixture(facility, "containment", FixtureType.Bed, "Cell Bunk D", 86, 43, 18, 18);
        AddFixture(facility, "containment", FixtureType.Locker, "Secure Property Locker", 14, 76, 16, 18);
        AddFixture(facility, "containment", FixtureType.UtilityPanel, "Containment Interlock", 86, 76, 16, 18);
        AddFixture(facility, "containment", FixtureType.Camera, "Containment Camera", 50, 10, 10, 10);

        // Medical.
        AddFixture(facility, "medical", FixtureType.MedicalBed, "Med Bed A", 27, 47, 28, 19, 27, 47, FixtureUsePose.Lie, 90);
        AddFixture(facility, "medical", FixtureType.MedicalBed, "Med Bed B", 73, 47, 28, 19, 73, 47, FixtureUsePose.Lie, 270);
        AddFixture(facility, "medical", FixtureType.TreatmentUnit, "Treatment Gantry", 50, 25, 30, 14);
        AddFixture(facility, "medical", FixtureType.ResurrectionChamber, "High-Power Resurrection Chamber", 50, 49, 14, 20, 50, 64);
        AddFixture(facility, "medical", FixtureType.Console, "Diagnostics", 50, 79, 34, 12, 50, 79);
        AddFixture(facility, "medical", FixtureType.Cabinet, "Medical Stores", 13, 80, 12, 20);
        AddFixture(facility, "medical", FixtureType.Cabinet, "Sterile Stores", 87, 80, 12, 20);

        // Control room.
        AddFixture(facility, "control", FixtureType.Screen, "Command Display", 50, 15, 70, 10);
        AddFixture(facility, "control", FixtureType.Console, "Navigation", 23, 38, 24, 13, 23, 52);
        AddFixture(facility, "control", FixtureType.Console, "Systems", 50, 38, 24, 13, 50, 52);
        AddFixture(facility, "control", FixtureType.Console, "Comms", 77, 38, 24, 13, 77, 52);
        AddFixture(facility, "control", FixtureType.Chair, "Nav Chair", 23, 56, 10, 10, 23, 56, FixtureUsePose.Sit, 0);
        AddFixture(facility, "control", FixtureType.Chair, "Systems Chair", 50, 56, 10, 10, 50, 56, FixtureUsePose.Sit, 0);
        AddFixture(facility, "control", FixtureType.Chair, "Comms Chair", 77, 56, 10, 10, 77, 56, FixtureUsePose.Sit, 0);
        AddFixture(facility, "control", FixtureType.Console, "Command Station", 50, 78, 32, 13, 50, 87);
        AddFixture(facility, "control", FixtureType.Chair, "Command Chair", 50, 90, 11, 10, 50, 90, FixtureUsePose.Sit, 0);

        // Washroom.
        AddFixture(facility, "washroom", FixtureType.Shower, "Shower A", 20, 28, 24, 28, 20, 28, FixtureUsePose.Shower);
        AddFixture(facility, "washroom", FixtureType.Shower, "Shower B", 50, 28, 24, 28, 50, 28, FixtureUsePose.Shower);
        AddFixture(facility, "washroom", FixtureType.Toilet, "Toilet A", 20, 70, 18, 20, 20, 70, FixtureUsePose.Toilet);
        AddFixture(facility, "washroom", FixtureType.Toilet, "Toilet B", 50, 70, 18, 20, 50, 70, FixtureUsePose.Toilet);
        AddFixture(facility, "washroom", FixtureType.Sink, "Wash Basins", 80, 34, 20, 18, 80, 40);
        AddFixture(facility, "washroom", FixtureType.Mirror, "Mirror", 80, 18, 20, 8, 80, 34);
        AddFixture(facility, "washroom", FixtureType.Cabinet, "Linen Cabinet", 80, 72, 18, 22);

        // Storage.
        AddFixture(facility, "storage", FixtureType.StorageRack, "Rack A", 18, 42, 20, 52);
        AddFixture(facility, "storage", FixtureType.StorageRack, "Rack B", 50, 42, 20, 52);
        AddFixture(facility, "storage", FixtureType.StorageRack, "Rack C", 82, 42, 20, 52);
        AddFixture(facility, "storage", FixtureType.Crate, "Cargo Pallet A", 25, 82, 22, 15);
        AddFixture(facility, "storage", FixtureType.Crate, "Cargo Pallet B", 75, 82, 22, 15);

        // Engineering.
        AddFixture(facility, "engineering", FixtureType.Workbench, "Fabrication Bench", 28, 33, 38, 18, 28, 45);
        AddFixture(facility, "engineering", FixtureType.Workbench, "Electronics Bench", 70, 33, 34, 18, 70, 45);
        AddFixture(facility, "engineering", FixtureType.ToolCabinet, "Tool Cabinet", 88, 62, 12, 28);
        AddFixture(facility, "engineering", FixtureType.Pipe, "Coolant Run", 14, 66, 12, 42);
        AddFixture(facility, "engineering", FixtureType.Console, "Systems Console", 58, 77, 30, 13, 58, 77);
        AddFixture(facility, "engineering", FixtureType.UtilityPanel, "Breaker Panel", 82, 82, 16, 14);

        // Generator machinery.
        AddFixture(facility, "generator", FixtureType.Generator, "Generator A", 50, 45, 50, 42, 50, 70);
        AddFixture(facility, "generator", FixtureType.Pipe, "Fuel / Coolant Feed", 14, 45, 12, 55);
        AddFixture(facility, "generator", FixtureType.Pipe, "Output Bus", 86, 45, 12, 55);
        AddFixture(facility, "generator", FixtureType.Console, "Power Control", 50, 81, 36, 12, 50, 81);
        AddFixture(facility, "generator", FixtureType.UtilityPanel, "Emergency Cutoff", 82, 82, 16, 14);

        // Reactor.
        AddFixture(facility, "reactor", FixtureType.ReactorCore, "Reactor Core", 50, 48, 44, 44, 50, 74);
        AddFixture(facility, "reactor", FixtureType.Pipe, "Primary Coolant", 15, 48, 12, 58);
        AddFixture(facility, "reactor", FixtureType.Pipe, "Secondary Coolant", 85, 48, 12, 58);
        AddFixture(facility, "reactor", FixtureType.Console, "Reactor Control", 50, 83, 36, 12, 50, 83);
        AddFixture(facility, "reactor", FixtureType.UtilityPanel, "SCRAM Panel", 82, 83, 16, 13);

        // End-cap rooms.
        AddFixture(facility, "airlock", FixtureType.AirlockDoor, "Outer Hatch", 50, 18, 70, 16);
        AddFixture(facility, "airlock", FixtureType.SuitLocker, "Suit Locker A", 24, 52, 28, 24);
        AddFixture(facility, "airlock", FixtureType.SuitLocker, "Suit Locker B", 76, 52, 28, 24);
        AddFixture(facility, "airlock", FixtureType.UtilityPanel, "Pressure Panel", 50, 78, 42, 13);
        AddFixture(facility, "airlock", FixtureType.Vent, "Airlock Vent", 50, 91, 38, 7);

        AddFixture(facility, "isolation", FixtureType.OverseerShutdown, "Emergency Overseer Isolation", 50, 48, 52, 38, 50, 72);
        AddFixture(facility, "isolation", FixtureType.Console, "Isolation Console", 50, 82, 48, 12, 50, 82);
        AddFixture(facility, "isolation", FixtureType.UtilityPanel, "Hardline Disconnect", 50, 17, 48, 12);

        // Corridors stay deliberately restrained. Their only visible contents
        // are windows, occasional seating and surveillance cameras; machines,
        // utility cabinets and decorative clutter belong in the large rooms.
        AddFixture(facility, "corridor", FixtureType.Window, "Observation Window A", 24, 16, 18, 12);
        AddFixture(facility, "corridor", FixtureType.Window, "Observation Window B", 50, 84, 18, 12);
        AddFixture(facility, "corridor", FixtureType.Window, "Observation Window C", 76, 16, 18, 12);
        AddFixture(facility, "corridor", FixtureType.Bench, "Transit Bench West", 35, 72, 18, 18, 35, 72, FixtureUsePose.Sit, 0);
        AddFixture(facility, "corridor", FixtureType.Bench, "Transit Bench East", 65, 28, 18, 18, 65, 28, FixtureUsePose.Sit, 180);

        foreach (var hallway in facility.Rooms.Values.Where(room =>
                     room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsVerticalConnector(facility, hallway))
            {
                AddFixture(facility, hallway.Id, FixtureType.Window, "Passage Window", 18, 50, 16, 46);
            }
            else
            {
                AddFixture(facility, hallway.Id, FixtureType.Window, "Passage Window", 50, 18, 46, 16);
            }
        }

        foreach (var room in facility.Rooms.Values)
        {
            AddFixture(facility, room.Id, FixtureType.Camera, "Camera", 90, 12, 8, 8);
        }
    }


    private static bool IsVerticalConnector(Facility facility, Room hallway)
    {
        var door = facility.Doors.FirstOrDefault(candidate =>
            candidate.RoomAId.Equals(hallway.Id, StringComparison.OrdinalIgnoreCase)
            || candidate.RoomBId.Equals(hallway.Id, StringComparison.OrdinalIgnoreCase));

        if (door is null)
        {
            return hallway.MapHeight >= hallway.MapWidth;
        }

        var neighbourId = door.RoomAId.Equals(
            hallway.Id,
            StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId
                : door.RoomAId;
        var portal = StationGeometry.FindSharedPortal(
            hallway,
            facility.Rooms[neighbourId]);

        return portal.Wall == StationWall.Horizontal;
    }


    private static void ConfigureEnvironmentControls(Facility facility)
    {
        foreach (var room in facility.Rooms.Values)
        {
            room.TemperatureSetpointC = room.TemperatureC;
        }

        foreach (var room in facility.Rooms.Values.Where(room => room.Type == RoomType.Corridor))
        {
            room.HasTemperatureControl = false;
            room.IsTemperatureAiControllable = false;
            room.HasVentilationControl = false;
            room.IsVentilationAiControllable = false;
        }

        foreach (var airlock in facility.Rooms.Values.Where(room => room.Type == RoomType.Airlock))
        {
            airlock.HasTemperatureControl = false;
            airlock.IsTemperatureAiControllable = false;
            airlock.HasVentilationControl = false;
            airlock.IsVentilationAiControllable = false;
            airlock.HasExteriorHatch = true;
            airlock.IsExteriorHatchAiControllable = true;
            airlock.ExteriorHatchOpen = false;
        }

        if (facility.Rooms.TryGetValue("hydroponics", out var hydroponics))
        {
            hydroponics.TemperatureC = 24;
            hydroponics.TemperatureSetpointC = 24;
            hydroponics.IsTemperatureAiControllable = false;
            hydroponics.IsVentilationAiControllable = false;
        }

        if (facility.Rooms.TryGetValue("reactor", out var reactor))
        {
            reactor.IsTemperatureAiControllable = false;
        }
    }

    private static void AddFixture(
        Facility facility,
        string roomId,
        FixtureType type,
        string label,
        double x,
        double y,
        double width,
        double height,
        double? interactionX = null,
        double? interactionY = null,
        FixtureUsePose usePose = FixtureUsePose.Stand,
        double facingDegrees = 0)
    {
        if (!facility.Rooms.TryGetValue(roomId, out var room))
        {
            return;
        }

        room.Fixtures.Add(
            new RoomFixture(
                type,
                label,
                x,
                y,
                width,
                height,
                interactionX,
                interactionY,
                usePose,
                facingDegrees));
    }
}
