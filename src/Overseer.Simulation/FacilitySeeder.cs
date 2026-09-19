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
        AddRoom(facility, "quarters", "Crew Quarters", RoomType.CrewQuarters, 14, 15, 15, 13);
        AddRoom(facility, "kitchen", "Kitchen", RoomType.Kitchen, 32, 15, 14, 13);
        AddRoom(facility, "lounge", "Recreation Lounge", RoomType.Recreation, 50, 15, 15, 13);
        AddRoom(facility, "medical", "Medical", RoomType.Medical, 68, 15, 15, 13);
        AddRoom(facility, "control", "Control Room", RoomType.ControlRoom, 86, 15, 16, 13);

        AddRoom(facility, "airlock", "Airlock", RoomType.Airlock, 4, 50, 8, 12);
        AddRoom(facility, "corridor", "Central Corridor", RoomType.Corridor, 50, 50, 80, 10);
        AddRoom(facility, "isolation", "Overseer Isolation", RoomType.ControlRoom, 96, 50, 8, 12);

        AddRoom(facility, "washroom", "Washroom", RoomType.Washroom, 14, 85, 14, 13);
        AddRoom(facility, "storage", "Storage", RoomType.Storage, 32, 85, 14, 13);
        AddRoom(facility, "engineering", "Engineering", RoomType.Engineering, 50, 85, 16, 13);
        AddRoom(facility, "generator", "Generator", RoomType.Generator, 68, 85, 15, 13);
        AddRoom(facility, "reactor", "Reactor", RoomType.Reactor, 86, 85, 15, 13);

        foreach (var roomId in new[]
        {
            "quarters", "kitchen", "lounge", "medical", "control",
            "airlock", "washroom", "storage", "engineering", "generator", "reactor", "isolation"
        })
        {
            AddHallwayToCorridor(facility, roomId, "corridor");
        }

        AddFixtures(facility);

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
        var room = facility.Rooms[roomId];
        var corridor = facility.Rooms[corridorId];
        var hallwayId = $"hall-{roomId}";

        if (Math.Abs(room.MapY - corridor.MapY) >= Math.Abs(room.MapX - corridor.MapX))
        {
            var roomAbove = room.MapY < corridor.MapY;
            var roomEdge = room.MapY
                + (roomAbove ? room.MapHeight / 2 : -room.MapHeight / 2);
            var corridorEdge = corridor.MapY
                + (roomAbove ? -corridor.MapHeight / 2 : corridor.MapHeight / 2);
            var top = Math.Min(roomEdge, corridorEdge);
            var bottom = Math.Max(roomEdge, corridorEdge);

            AddRoom(
                facility,
                hallwayId,
                $"{room.Name} Hallway",
                RoomType.Corridor,
                room.MapX,
                (top + bottom) / 2,
                4.2,
                Math.Max(3, bottom - top));
        }
        else
        {
            var roomLeft = room.MapX < corridor.MapX;
            var roomEdge = room.MapX
                + (roomLeft ? room.MapWidth / 2 : -room.MapWidth / 2);
            var corridorEdge = corridor.MapX
                + (roomLeft ? -corridor.MapWidth / 2 : corridor.MapWidth / 2);
            var left = Math.Min(roomEdge, corridorEdge);
            var right = Math.Max(roomEdge, corridorEdge);

            AddRoom(
                facility,
                hallwayId,
                $"{room.Name} Hallway",
                RoomType.Corridor,
                (left + right) / 2,
                room.MapY,
                Math.Max(3, right - left),
                4.2);
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
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk A", 23, 30, 25, 19);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk B", 63, 30, 25, 19);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk C", 23, 68, 25, 19);
        AddFixture(facility, "quarters", FixtureType.Locker, "Lockers", 72, 70, 18, 16);
        AddFixture(facility, "quarters", FixtureType.Mirror, "Mirror", 50, 82, 22, 8);

        AddFixture(facility, "kitchen", FixtureType.KitchenCounter, "Galley", 50, 22, 68, 20);
        AddFixture(facility, "kitchen", FixtureType.Table, "Mess Table", 50, 62, 48, 25);

        AddFixture(facility, "lounge", FixtureType.Sofa, "Sofa", 38, 55, 48, 24);
        AddFixture(facility, "lounge", FixtureType.RecreationConsole, "Games Terminal", 76, 36, 25, 24);
        AddFixture(facility, "lounge", FixtureType.Table, "Coffee Table", 45, 78, 35, 12);

        AddFixture(facility, "washroom", FixtureType.Shower, "Shower A", 24, 33, 25, 35);
        AddFixture(facility, "washroom", FixtureType.Shower, "Shower B", 62, 33, 25, 35);
        AddFixture(facility, "washroom", FixtureType.Sink, "Sink", 25, 78, 28, 14);
        AddFixture(facility, "washroom", FixtureType.Mirror, "Mirror", 50, 78, 28, 10);
        AddFixture(facility, "washroom", FixtureType.Toilet, "Toilet", 78, 76, 18, 22);

        AddFixture(facility, "medical", FixtureType.MedicalBed, "Med Bed A", 30, 42, 30, 22);
        AddFixture(facility, "medical", FixtureType.MedicalBed, "Med Bed B", 68, 42, 30, 22);
        AddFixture(facility, "medical", FixtureType.Console, "Diagnostics", 50, 76, 42, 15);

        AddFixture(facility, "control", FixtureType.Console, "Command Console", 50, 26, 68, 18);
        AddFixture(facility, "control", FixtureType.Console, "Navigation", 28, 62, 28, 18);
        AddFixture(facility, "control", FixtureType.Console, "Comms", 72, 62, 28, 18);

        AddFixture(facility, "storage", FixtureType.StorageRack, "Rack A", 28, 35, 25, 52);
        AddFixture(facility, "storage", FixtureType.StorageRack, "Rack B", 70, 35, 25, 52);

        AddFixture(facility, "engineering", FixtureType.Workbench, "Workbench", 35, 35, 45, 22);
        AddFixture(facility, "engineering", FixtureType.Console, "Systems Console", 72, 68, 32, 22);

        AddFixture(facility, "generator", FixtureType.Generator, "Generator A", 50, 45, 52, 45);
        AddFixture(facility, "generator", FixtureType.Console, "Power Control", 50, 78, 44, 13);

        AddFixture(facility, "reactor", FixtureType.ReactorCore, "Reactor Core", 50, 50, 50, 50);
        AddFixture(facility, "reactor", FixtureType.Console, "Reactor Control", 50, 82, 48, 13);

        AddFixture(facility, "airlock", FixtureType.AirlockDoor, "Outer Hatch", 50, 50, 60, 55);
        AddFixture(facility, "corridor", FixtureType.Console, "Security Panel", 18, 50, 8, 50);
        AddFixture(facility, "corridor", FixtureType.Console, "Utility Panel", 82, 50, 8, 50);
        AddFixture(facility, "isolation", FixtureType.OverseerShutdown, "Emergency Overseer Isolation", 50, 50, 52, 45);

        foreach (var room in facility.Rooms.Values)
        {
            AddFixture(facility, room.Id, FixtureType.Camera, "Camera", 90, 12, 8, 8);
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
        double height)
    {
        facility.Rooms[roomId].Fixtures.Add(
            new RoomFixture(type, label, x, y, width, height));
    }
}
