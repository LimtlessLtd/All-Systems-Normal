using Overseer.Domain;

namespace Overseer.Simulation;

public static class FacilitySeeder
{
    public static GameState CreateDefault()
    {
        var facility = new Facility();

        AddRoom(facility, "quarters", "Crew Quarters", RoomType.CrewQuarters);
        AddRoom(facility, "kitchen", "Kitchen", RoomType.Kitchen);
        AddRoom(facility, "medical", "Medical", RoomType.Medical);
        AddRoom(facility, "control", "Control Room", RoomType.ControlRoom);
        AddRoom(facility, "generator", "Generator", RoomType.Generator);
        AddRoom(facility, "reactor", "Reactor", RoomType.Reactor);
        AddRoom(facility, "engineering", "Engineering", RoomType.Engineering);
        AddRoom(facility, "storage", "Storage", RoomType.Storage);
        AddRoom(facility, "corridor", "Central Corridor", RoomType.Corridor);
        AddRoom(facility, "airlock", "Airlock", RoomType.Airlock);

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
                    ("Leadership", 92), ("Operations", 78)),
                CreateCrew("Sarah Chen", CrewRole.Engineer, "engineering",
                    ("Engineering", 96), ("Reactor", 91)),
                CreateCrew("Marcus Reed", CrewRole.Security, "corridor",
                    ("Security", 93), ("First Aid", 45)),
                CreateCrew("Nadia Okafor", CrewRole.Doctor, "medical",
                    ("Medicine", 97), ("Psychology", 81)),
                CreateCrew("Felix Ward", CrewRole.Technician, "generator",
                    ("Electrical", 90), ("Engineering", 72)),
                CreateCrew("Emma Voss", CrewRole.Scientist, "reactor",
                    ("Research", 95), ("Reactor", 76))
            ]
        };

        foreach (var npc in state.Crew)
        {
            npc.Beliefs.Add(new Belief(
                "Overseer",
                "The facility AI is responsible for keeping the crew alive.",
                0.65));
        }

        state.EventLog.Add("T+00:00: ALL SYSTEMS NORMAL. Six crew members online.");

        return state;
    }

    private static void AddRoom(
        Facility facility,
        string id,
        string name,
        RoomType type)
    {
        facility.Rooms.Add(id, new Room
        {
            Id = id,
            Name = name,
            Type = type
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
        params (string Skill, int Value)[] skills)
    {
        var npc = new Npc
        {
            Name = name,
            Role = role,
            CurrentRoomId = roomId
        };

        foreach (var (skill, value) in skills)
        {
            npc.Skills[skill] = value;
        }

        return npc;
    }
}
