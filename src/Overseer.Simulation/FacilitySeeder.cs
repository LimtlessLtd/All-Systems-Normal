using Overseer.Domain;

namespace Overseer.Simulation;

public static class FacilitySeeder
{
    public static GameState CreateDefault()
    {
        var facility = new Facility();

        AddRoom(facility, "quarters", "Crew Quarters", RoomType.CrewQuarters, 15, 29, 20, 19);
        AddRoom(facility, "kitchen", "Kitchen", RoomType.Kitchen, 38, 29, 18, 19);
        AddRoom(facility, "medical", "Medical", RoomType.Medical, 59, 29, 18, 19);
        AddRoom(facility, "control", "Control Room", RoomType.ControlRoom, 83, 29, 24, 20);
        AddRoom(facility, "airlock", "Airlock", RoomType.Airlock, 7, 52, 10, 14);
        AddRoom(facility, "corridor", "Central Corridor", RoomType.Corridor, 50, 52, 76, 12);
        AddRoom(facility, "storage", "Storage", RoomType.Storage, 17, 76, 18, 20);
        AddRoom(facility, "engineering", "Engineering", RoomType.Engineering, 41, 76, 22, 20);
        AddRoom(facility, "generator", "Generator", RoomType.Generator, 67, 76, 18, 20);
        AddRoom(facility, "reactor", "Reactor", RoomType.Reactor, 88, 76, 18, 22);

        AddFixtures(facility);

        Connect(facility, "quarters", "corridor");
        Connect(facility, "kitchen", "corridor");
        Connect(facility, "medical", "corridor");
        Connect(facility, "control", "corridor");
        Connect(facility, "engineering", "corridor");
        Connect(facility, "storage", "corridor");
        Connect(facility, "airlock", "corridor");
        Connect(facility, "engineering", "reactor");
        Connect(facility, "engineering", "generator");
        Connect(facility, "control", "generator");

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
                    Resentment = 0
                };
            }
        }

        // A few mild pre-existing tensions give the social simulation something to work with
        // without scripting an outcome.
        state.Crew.Single(npc => npc.Name == "Marcus Reed")
            .Relationships["Emma Voss"].Resentment = 24;
        state.Crew.Single(npc => npc.Name == "Emma Voss")
            .Relationships["Marcus Reed"].Resentment = 18;

        state.Crew.Single(npc => npc.Name == "Sarah Chen")
            .Relationships["Felix Ward"].Trust = 62;
        state.Crew.Single(npc => npc.Name == "Felix Ward")
            .Relationships["Sarah Chen"].Trust = 64;

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

    private static void AddFixtures(Facility facility)
    {
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk A", 22, 35, 24, 18);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk B", 62, 35, 24, 18);
        AddFixture(facility, "quarters", FixtureType.Bed, "Bunk C", 22, 68, 24, 18);
        AddFixture(facility, "quarters", FixtureType.Locker, "Lockers", 72, 70, 18, 16);

        AddFixture(facility, "kitchen", FixtureType.KitchenCounter, "Galley", 50, 22, 68, 20);
        AddFixture(facility, "kitchen", FixtureType.Table, "Mess Table", 50, 62, 48, 25);

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

        AddFixture(facility, "corridor", FixtureType.Console, "Security Panel", 18, 50, 10, 45);
        AddFixture(facility, "corridor", FixtureType.Console, "Utility Panel", 82, 50, 10, 45);

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
