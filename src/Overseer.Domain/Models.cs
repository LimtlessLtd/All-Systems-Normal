namespace Overseer.Domain;

public enum CrewRole
{
    Commander,
    Engineer,
    Security,
    Doctor,
    Technician,
    Scientist
}

public enum RoomType
{
    CrewQuarters,
    Kitchen,
    Medical,
    ControlRoom,
    Generator,
    Reactor,
    Engineering,
    Storage,
    Corridor,
    Airlock
}

public enum FixtureType
{
    Bed,
    Table,
    Console,
    MedicalBed,
    ReactorCore,
    Generator,
    Workbench,
    StorageRack,
    AirlockDoor,
    KitchenCounter,
    Camera,
    Locker
}

public enum ActionKind
{
    Idle,
    Move,
    Rest,
    Eat,
    Investigate,
    Repair,
    Talk,
    Socialize,
    Argue,
    Attack,
    RequestHelp
}

public sealed record Memory(
    string Description,
    TimeSpan OccurredAt,
    double Importance);

public sealed record Belief(
    string Subject,
    string Statement,
    double Confidence);

public sealed record Personality(
    double Empathy,
    double Temper,
    double Sociability,
    double Courage);

public sealed class Relationship
{
    public required string PersonName { get; init; }
    public double Affinity { get; set; } = 50;
    public double Trust { get; set; } = 50;
    public double Resentment { get; set; }
    public int Conversations { get; set; }
    public int Arguments { get; set; }
}

public sealed record NpcAction(
    ActionKind Kind,
    string? TargetId,
    string Reason);

public sealed record NpcIntent(
    ActionKind Action,
    string? TargetId,
    string Goal,
    string Reason,
    int Urgency,
    string Source,
    TimeSpan CreatedAt);

public sealed record RoomFixture(
    FixtureType Type,
    string Label,
    double X,
    double Y,
    double Width,
    double Height);

public sealed class Npc
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required CrewRole Role { get; init; }
    public required string CurrentRoomId { get; set; }
    public required Personality Personality { get; init; }

    public double Health { get; set; } = 100;
    public double Hunger { get; set; } = 10;
    public double Fatigue { get; set; } = 10;
    public double Fear { get; set; } = 5;
    public double Stress { get; set; } = 10;

    public string? CauseOfDeath { get; set; }
    public bool IsAlive => Health > 0;

    public Dictionary<string, int> Skills { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Relationship> Relationships { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<Memory> Memories { get; } = [];
    public List<Belief> Beliefs { get; } = [];

    public NpcAction CurrentAction { get; set; } =
        new(ActionKind.Idle, null, "Waiting for something to happen.");

    public NpcIntent? Intent { get; set; }
    public string MindMode { get; set; } = "Routine";
    public string LastThought { get; set; } = "No deliberate thought yet.";
    public TimeSpan LastThoughtAt { get; set; }
}
    
public sealed class Room
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required RoomType Type { get; init; }

    public double MapX { get; init; }
    public double MapY { get; init; }
    public double MapWidth { get; init; } = 16;
    public double MapHeight { get; init; } = 14;

    public bool IsPowered { get; set; } = true;
    public bool LightsOn { get; set; } = true;
    public bool CameraOnline { get; set; } = true;
    public double TemperatureC { get; set; } = 21;
    public double OxygenPercent { get; set; } = 20.9;

    public List<RoomFixture> Fixtures { get; } = [];

    public bool HasVisualFeed => IsPowered && CameraOnline;
}

public sealed class Door
{
    public required string Id { get; init; }
    public required string RoomAId { get; init; }
    public required string RoomBId { get; init; }

    public bool IsOpen { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsPowered { get; set; } = true;

    public bool IsPassable => IsPowered && IsOpen && !IsLocked;

    public bool Connects(string firstRoomId, string secondRoomId) =>
        (RoomAId.Equals(firstRoomId, StringComparison.OrdinalIgnoreCase)
            && RoomBId.Equals(secondRoomId, StringComparison.OrdinalIgnoreCase))
        || (RoomAId.Equals(secondRoomId, StringComparison.OrdinalIgnoreCase)
            && RoomBId.Equals(firstRoomId, StringComparison.OrdinalIgnoreCase));
}

public sealed class Facility
{
    public Dictionary<string, Room> Rooms { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<Door> Doors { get; } = [];

    public Door? FindDoorBetween(string firstRoomId, string secondRoomId) =>
        Doors.FirstOrDefault(door => door.Connects(firstRoomId, secondRoomId));
}

public sealed class GameState
{
    public required Facility Facility { get; init; }
    public List<Npc> Crew { get; init; } = [];
    public TimeSpan Elapsed { get; set; }
    public List<string> EventLog { get; } = [];
}
