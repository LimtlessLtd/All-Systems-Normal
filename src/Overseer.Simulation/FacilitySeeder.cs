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
        stationConstraints ??= ScenarioCatalog.SecureContinuity.StationConstraints;
        var chosenStationSeed = stationSeed ?? Random.Shared.Next();
        var generation = StationGenerator.Generate(chosenStationSeed, stationConstraints);
        var facility = generation.Facility;

        AddFixtures(facility);
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
            npc.Beliefs.Add(new Belief(
                "Overseer",
                "The facility AI is responsible for keeping the crew alive.",
                0.65));

            foreach (var other in state.Crew.Where(other => other.Id != npc.Id))
            {
                npc.Relationships[other.Name] = new Relationship
                {
                    PersonName = other.Name,
                    Affinity = 50,
                    Trust = 50,
                    Resentment = 0,
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
        CrewProvisioningSystem.Plant(state, seed);

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

        var requestedRobotRooms = constraints.RequiredRobotRoomIds.Count > 0
            ? constraints.RequiredRobotRoomIds
            : [fallbackRobotRoom];
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

    private static void AddFixtures(Facility facility)
    {
        // Crew Quarters — six real bunks and personal storage leave a clear
        // central aisle. Interaction anchors are future-proofed for lying/sitting.
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk A", 18, 25, 20, 13, 18, 25, FixtureUsePose.Lie, 90);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk B", 50, 25, 20, 13, 50, 25, FixtureUsePose.Lie, 90);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk C", 82, 25, 20, 13, 82, 25, FixtureUsePose.Lie, 90);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk D", 18, 72, 20, 13, 18, 72, FixtureUsePose.Lie, 270);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk E", 50, 72, 20, 13, 50, 72, FixtureUsePose.Lie, 270);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk F", 82, 72, 20, 13, 82, 72, FixtureUsePose.Lie, 270);
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

        // Hydroponics.
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed A", 23, 45, 18, 50);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed B", 50, 45, 18, 50);
        AddFixture(facility, "hydroponics", FixtureType.GrowBed, "Grow Bed C", 77, 45, 18, 50);
        AddFixture(facility, "hydroponics", FixtureType.IrrigationTank, "Nutrient Tank", 13, 80, 16, 18);
        AddFixture(facility, "hydroponics", FixtureType.Pipe, "Irrigation Manifold", 50, 74, 56, 7);
        AddFixture(facility, "hydroponics", FixtureType.Console, "Climate Supervisor", 82, 81, 24, 12, 82, 81);

        // Medical.
        AddFixture(facility, "medical", FixtureType.MedicalBed, "Med Bed A", 27, 47, 28, 19, 27, 47, FixtureUsePose.Lie, 90);
        AddFixture(facility, "medical", FixtureType.MedicalBed, "Med Bed B", 73, 47, 28, 19, 73, 47, FixtureUsePose.Lie, 270);
        AddFixture(facility, "medical", FixtureType.TreatmentUnit, "Treatment Gantry", 50, 25, 30, 14);
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
