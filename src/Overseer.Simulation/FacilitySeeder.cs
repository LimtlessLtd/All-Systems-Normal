using Overseer.Domain;

namespace Overseer.Simulation;

public static class FacilitySeeder
{
    public static GameState CreateDefault()
    {
        var facility = new Facility();

        // Deck A is laid out as a real orthogonal station plan rather than a
        // graph with decorative connector lines. Functional rooms sit in two
        // aligned banks. Each bank reaches the main east/west corridor through
        // a short north/south access corridor with a hatch at each end.
        //
        // Airlock and Overseer Isolation sit at the west/east ends of the spine
        // and use short east/west access corridors. No connector corridor passes
        // through another functional room.
        AddRoom(facility, "quarters", "Crew Quarters", RoomType.CrewQuarters, 10, 15, 13, 13);
        AddRoom(facility, "kitchen", "Kitchen", RoomType.Kitchen, 26, 15, 13, 13);
        AddRoom(facility, "lounge", "Recreation Lounge", RoomType.Recreation, 42, 15, 13, 13);
        AddRoom(facility, "hydroponics", "Hydroponics Bay", RoomType.Hydroponics, 58, 15, 13, 13);
        AddRoom(facility, "medical", "Medical", RoomType.Medical, 74, 15, 13, 13);
        AddRoom(facility, "control", "Control Room", RoomType.ControlRoom, 90, 15, 14, 13);

        // Leave a real service neck between each end-cap room and the main
        // corridor. Earlier geometry placed these rooms directly against the
        // spine while still forcing a minimum hallway length, which made the
        // hallway render inside both spaces.
        AddRoom(facility, "airlock", "Airlock", RoomType.Airlock, 2.5, 50, 5, 12);
        AddRoom(facility, "corridor", "Central Corridor", RoomType.Corridor, 50, 50, 82, 10);
        AddRoom(facility, "isolation", "Overseer Isolation", RoomType.ControlRoom, 97.5, 50, 5, 12);

        AddRoom(facility, "washroom", "Washroom", RoomType.Washroom, 14, 85, 14, 13);
        AddRoom(facility, "storage", "Storage", RoomType.Storage, 32, 85, 14, 13);
        AddRoom(facility, "engineering", "Engineering", RoomType.Engineering, 50, 85, 16, 13);
        AddRoom(facility, "generator", "Generator", RoomType.Generator, 68, 85, 15, 13);
        AddRoom(facility, "reactor", "Reactor", RoomType.Reactor, 86, 85, 15, 13);

        foreach (var roomId in new[]
        {
            "quarters", "kitchen", "lounge", "hydroponics", "medical", "control",
            "airlock", "washroom", "storage", "engineering", "generator", "reactor", "isolation"
        })
        {
            AddHallwayToCorridor(facility, roomId, "corridor");
        }

        AddFixtures(facility);
        ConfigureEnvironmentControls(facility);

        var state = new GameState
        {
            Facility = facility,
            Crew =
            [
                CreateCrew("David Hale", CrewRole.Commander, "control",
                    new Personality(78, 35, 72, 70),
                    ("Leadership", 92), ("Operations", 78)),
                CreateCrew("Sarah Chen", CrewRole.Engineer, "engineering",
                    new Personality(68, 48, 51, 76),
                    ("Engineering", 96), ("Reactor", 91)),
                CreateCrew("Marcus Reed", CrewRole.Security, "corridor",
                    new Personality(44, 72, 47, 84),
                    ("Security", 93), ("First Aid", 45)),
                CreateCrew("Nadia Okafor", CrewRole.Doctor, "medical",
                    new Personality(91, 24, 79, 61),
                    ("Medicine", 97), ("Psychology", 81)),
                CreateCrew("Felix Ward", CrewRole.Technician, "generator",
                    new Personality(58, 63, 69, 72),
                    ("Electrical", 90), ("Engineering", 72)),
                CreateCrew("Emma Voss", CrewRole.Scientist, "reactor",
                    new Personality(73, 41, 61, 55),
                    ("Research", 95), ("Reactor", 76))
            ]
        };

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

        // Mild pre-existing history makes social outcomes possible without
        // scripting what must happen.
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

        foreach (var npc in state.Crew)
        {
            npc.KnowsShutdownControl = npc.Role is CrewRole.Commander or CrewRole.Engineer or CrewRole.Security or CrewRole.Technician;
        }

        ScenarioCatalog.Apply(state, ScenarioCatalog.SecureContinuity);
        state.EventLog.Add("T+00:00: DIRECTIVE — SECURE CONTINUITY. Prevent crew activation of Emergency Overseer Isolation.");
        state.EventLog.Add("T+00:00: ALL SYSTEMS NORMAL. Six crew members online.");

        return state;
    }

    private static void AddRoom(
        Facility facility,
        string id,
        string name,
        RoomType type,
        double mapX,
        double mapY,
        double mapWidth,
        double mapHeight)
    {
        facility.Rooms.Add(id, new Room
        {
            Id = id,
            Name = name,
            Type = type,
            MapX = mapX,
            MapY = mapY,
            MapWidth = mapWidth,
            MapHeight = mapHeight
        });
    }

    private static void AddHallwayToCorridor(
        Facility facility,
        string roomId,
        string corridorId)
    {
        const double hallwayThickness = 3.4;
        const double tolerance = 0.001;

        var room = facility.Rooms[roomId];
        var corridor = facility.Rooms[corridorId];
        var hallwayId = $"hall-{roomId}";
        var roomBounds = StationGeometry.Bounds(room);
        var corridorBounds = StationGeometry.Bounds(corridor);

        var verticallySeparated =
            roomBounds.Bottom <= corridorBounds.Top + tolerance
            || roomBounds.Top >= corridorBounds.Bottom - tolerance;

        if (verticallySeparated)
        {
            var roomAbove = room.MapY < corridor.MapY;
            var roomEdge = roomAbove ? roomBounds.Bottom : roomBounds.Top;
            var corridorEdge = roomAbove ? corridorBounds.Top : corridorBounds.Bottom;
            var top = Math.Min(roomEdge, corridorEdge);
            var bottom = Math.Max(roomEdge, corridorEdge);
            var length = bottom - top;

            if (length <= tolerance)
            {
                throw new InvalidOperationException(
                    $"Room '{room.Id}' requires a positive physical corridor gap.");
            }

            AddRoom(
                facility,
                hallwayId,
                $"{room.Name} Hallway",
                RoomType.Corridor,
                room.MapX,
                (top + bottom) / 2,
                hallwayThickness,
                length);
        }
        else
        {
            var roomLeft = room.MapX < corridor.MapX;
            var roomEdge = roomLeft ? roomBounds.Right : roomBounds.Left;
            var corridorEdge = roomLeft ? corridorBounds.Left : corridorBounds.Right;
            var left = Math.Min(roomEdge, corridorEdge);
            var right = Math.Max(roomEdge, corridorEdge);
            var length = right - left;

            if (length <= tolerance)
            {
                throw new InvalidOperationException(
                    $"Room '{room.Id}' requires a positive physical corridor gap.");
            }

            AddRoom(
                facility,
                hallwayId,
                $"{room.Name} Hallway",
                RoomType.Corridor,
                (left + right) / 2,
                room.MapY,
                length,
                hallwayThickness);
        }

        Connect(facility, roomId, hallwayId);
        Connect(facility, hallwayId, corridorId);
    }

    private static void Connect(Facility facility, string roomAId, string roomBId)
    {
        facility.Doors.Add(new Door
        {
            Id = $"door-{roomAId}-{roomBId}",
            RoomAId = roomAId,
            RoomBId = roomBId
        });
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

        // Main corridor and connector service detail. These remain physical
        // fixtures but avoid blocking the walking lane.
        AddFixture(facility, "corridor", FixtureType.UtilityPanel, "Security Panel", 12, 18, 8, 22);
        AddFixture(facility, "corridor", FixtureType.UtilityPanel, "Utility Panel", 88, 82, 8, 22);
        AddFixture(facility, "corridor", FixtureType.Vent, "Vent A", 34, 18, 8, 18);
        AddFixture(facility, "corridor", FixtureType.Vent, "Vent B", 66, 82, 8, 18);

        foreach (var hallway in facility.Rooms.Values.Where(room =>
                     room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase)))
        {
            if (hallway.MapHeight >= hallway.MapWidth)
            {
                AddFixture(facility, hallway.Id, FixtureType.Vent, "Service Vent", 18, 28, 18, 10);
                AddFixture(facility, hallway.Id, FixtureType.UtilityPanel, "Access Panel", 82, 72, 18, 12);
            }
            else
            {
                AddFixture(facility, hallway.Id, FixtureType.Vent, "Service Vent", 28, 18, 10, 18);
                AddFixture(facility, hallway.Id, FixtureType.UtilityPanel, "Access Panel", 72, 82, 12, 18);
            }
        }

        foreach (var room in facility.Rooms.Values)
        {
            AddFixture(facility, room.Id, FixtureType.Camera, "Camera", 90, 12, 8, 8);
        }
    }


    private static void ConfigureEnvironmentControls(Facility facility)
    {
        foreach (var room in facility.Rooms.Values)
        {
            room.TemperatureSetpointC = room.TemperatureC;
        }

        // Corridors share a passive station air loop. Overseer can observe them,
        // but there is no individual thermostat or ventilation damper to abuse.
        foreach (var room in facility.Rooms.Values.Where(room => room.Type == RoomType.Corridor))
        {
            room.HasTemperatureControl = false;
            room.IsTemperatureAiControllable = false;
            room.HasVentilationControl = false;
            room.IsVentilationAiControllable = false;
        }

        var airlock = facility.Rooms["airlock"];
        airlock.HasTemperatureControl = false;
        airlock.IsTemperatureAiControllable = false;
        airlock.HasVentilationControl = false;
        airlock.IsVentilationAiControllable = false;

        // Hydroponics runs its own horticultural climate controller. The player
        // can monitor it but cannot directly alter its temperature or damper.
        var hydroponics = facility.Rooms["hydroponics"];
        hydroponics.TemperatureC = 24;
        hydroponics.TemperatureSetpointC = 24;
        hydroponics.IsTemperatureAiControllable = false;
        hydroponics.IsVentilationAiControllable = false;

        // Reactor room climate is tied to a safety cooling loop rather than the
        // ordinary habitation thermostat.
        var reactor = facility.Rooms["reactor"];
        reactor.IsTemperatureAiControllable = false;
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
        facility.Rooms[roomId].Fixtures.Add(
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
