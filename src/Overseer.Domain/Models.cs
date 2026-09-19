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

public enum ActionKind
{
    Idle,
    Move,
    Rest,
    Eat,
    Investigate,
    Repair,
    Talk,
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

public sealed record NpcAction(
    ActionKind Kind,
    string? TargetId,
    string Reason);

public sealed class Npc
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required CrewRole Role { get; init; }
    public required string CurrentRoomId { get; set; }

    public double Health { get; set; } = 100;
    public double Hunger { get; set; } = 10;
    public double Fatigue { get; set; } = 10;
    public double Fear { get; set; } = 5;
    public double Stress { get; set; } = 10;

    public Dictionary<string, int> Skills { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<Memory> Memories { get; } = [];
    public List<Belief> Beliefs { get; } = [];

    public NpcAction CurrentAction { get; set; } =
        new(ActionKind.Idle, null, "Waiting for something to happen.");
}

public sealed class Room
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required RoomType Type { get; init; }

    public bool IsPowered { get; set; } = true;
    public double TemperatureC { get; set; } = 21;
    public double OxygenPercent { get; set; } = 20.9;
}

public sealed class Door
{
    public required string Id { get; init; }
    public required string RoomAId { get; init; }
    public required string RoomBId { get; init; }

    public bool IsOpen { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsPowered { get; set; } = true;

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
