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
    Recreation,
    Washroom,
    Hydroponics,
    Corridor,
    Airlock
}

public enum FixtureType
{
    Bed,
    Table,
    Chair,
    Bench,
    Window,
    Console,
    MedicalBed,
    TreatmentUnit,
    ReactorCore,
    Generator,
    Workbench,
    StorageRack,
    Crate,
    AirlockDoor,
    KitchenCounter,
    Camera,
    Locker,
    SuitLocker,
    Cabinet,
    ToolCabinet,
    Shower,
    Sink,
    Toilet,
    Sofa,
    RecreationConsole,
    Mirror,
    GrowBed,
    IrrigationTank,
    Pipe,
    Vent,
    UtilityPanel,
    Screen,
    OverseerShutdown
}

public enum FixtureUsePose
{
    Stand,
    Sit,
    Lie,
    Shower,
    Toilet
}

public enum ScenarioStatus
{
    Running,
    Won,
    Failed
}

public enum ShutdownAccessVariant
{
    Absent,
    EasyToSeal,
    Redundant,
    HardwiredManual,
    CrewOverridable,
    ImpossibleToSeal
}

public sealed record ScenarioObjective(string Id, string Title, string Description);

public sealed record ScenarioDefinition(
    string Id,
    string Title,
    string Briefing,
    ShutdownAccessVariant ShutdownVariant,
    IReadOnlyList<ScenarioObjective> Objectives);

public sealed class ShutdownMechanism
{
    public required string Id { get; init; }
    public required string RoomId { get; init; }
    public string Label { get; init; } = "OVERSEER EMERGENCY ISOLATION";
    public bool IsOnline { get; set; } = true;
    public bool IsHardwired { get; init; }
    public bool IsAiSealable { get; init; } = true;
    public bool CrewCanOverrideRoute { get; init; }
    public int ActivationMinutes { get; init; } = 2;
}

public sealed record OverseerEvidence(
    string Description,
    double Weight,
    TimeSpan ObservedAt,
    string? SourceNpcName = null);

public enum ActionKind
{
    Idle,
    Move,
    Rest,
    Sleep,
    Eat,
    Recreate,
    Groom,
    Shower,
    UseToilet,
    Work,
    Intimacy,
    Investigate,
    Repair,
    Talk,
    Socialize,
    Argue,
    Attack,
    RequestHelp,
    ShutdownOverseer,
    OverrideDoor,
    ForceDoor,
    RestoreSystem
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

public enum TraitEffectKind
{
    Empathy,
    Temper,
    Sociability,
    Courage,
    Force,
    Technical,
    Repair,
    StressResistance,
    SuspicionSensitivity
}

public sealed record CrewTraitEffect(
    TraitEffectKind Kind,
    int Modifier);

public sealed record CrewTrait(
    string Name,
    string Description,
    IReadOnlyList<CrewTraitEffect> Effects);

public static class CrewTraitMath
{
    public static int Modifier(Npc npc, TraitEffectKind kind) =>
        npc.Traits
            .SelectMany(trait => trait.Effects)
            .Where(effect => effect.Kind == kind)
            .Sum(effect => effect.Modifier);
}

public enum NpcBubbleKind
{
    Thought,
    Speech,
    Alert
}

public enum AudioCueKind
{
    Speech,
    Thought,
    Suspicion,
    Warning,
    Hostile,
    Critical,
    Failure,
    Important,
    System
}

public sealed record AudioCue(
    long Sequence,
    AudioCueKind Kind,
    TimeSpan CreatedAt,
    string? SourceId = null,
    string? RoomId = null);

public sealed record NpcBubble(
    string Text,
    NpcBubbleKind Kind,
    TimeSpan CreatedAt,
    TimeSpan ExpiresAt);

public sealed record ScheduledNpcBubble(
    string Text,
    NpcBubbleKind Kind,
    TimeSpan StartsAt,
    TimeSpan Duration);

public sealed class Relationship
{
    public required string PersonName { get; init; }
    public double Affinity { get; set; } = 50;
    public double Trust { get; set; } = 50;
    public double Resentment { get; set; }
    public double Attraction { get; set; }
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

public sealed record NpcMovement(
    string DoorId,
    string FromRoomId,
    string ToRoomId,
    double ExitX,
    double ExitY,
    double EntryX,
    double EntryY);

public sealed record RoomFixture(
    FixtureType Type,
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    double? InteractionX = null,
    double? InteractionY = null,
    FixtureUsePose UsePose = FixtureUsePose.Stand,
    double FacingDegrees = 0);

public sealed class Npc
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required CrewRole Role { get; init; }
    public required string CurrentRoomId { get; set; }
    public required Personality Personality { get; init; }
    public string GenerationSource { get; set; } = "Seeded";
    public List<CrewTrait> Traits { get; } = [];

    // Local room coordinates in the 0..100 range. CurrentRoomId remains the
    // authoritative containment state; these coordinates make movement physical
    // without allowing rendering code to decide where a person really is.
    public double PositionX { get; set; } = 50;
    public double PositionY { get; set; } = 50;
    public NpcMovement? Movement { get; set; }

    public double Health { get; set; } = 100;
    public double Hunger { get; set; } = 10;
    public double Fatigue { get; set; } = 10;
    public double Fear { get; set; } = 5;
    public double Stress { get; set; } = 10;

    // Everyday human needs use the same 0..100 "pressure" convention as
    // Hunger/Fatigue: higher values mean the need is becoming more pressing.
    public double HygieneNeed { get; set; } = 12;
    public double BladderNeed { get; set; } = 10;
    public double RecreationNeed { get; set; } = 18;
    public double SocialNeed { get; set; } = 15;
    public double IntimacyNeed { get; set; } = 10;

    public double OverseerSuspicion { get; set; }
    public List<OverseerEvidence> OverseerEvidence { get; } = [];
    public bool KnowsShutdownControl { get; set; }

    public string? CauseOfDeath { get; set; }
    public bool IsPresent { get; set; } = true;
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
    public TimeSpan RoutineUntil { get; set; }
    public NpcBubble? Bubble { get; set; }
    public List<ScheduledNpcBubble> PendingBubbles { get; } = [];
    public TimeSpan NextConversationAt { get; set; }
    public string MindMode { get; set; } = "Routine";
    public string LastThought { get; set; } = "No deliberate thought yet.";
    public TimeSpan LastThoughtAt { get; set; }
    public HashSet<Guid> DiscoveredBodies { get; } = [];
}
    
public sealed class LifeSupportState
{
    public bool IsOnline { get; set; } = true;
    public bool IsAiControllable { get; set; } = true;
    public double OxygenReservePercent { get; set; } = 100;
    public double ScrubberEfficiencyPercent { get; set; } = 100;
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
    public double TemperatureSetpointC { get; set; } = 21;
    public bool HasTemperatureControl { get; set; } = true;
    public bool TemperatureControlOnline { get; set; } = true;
    public bool IsTemperatureAiControllable { get; set; } = true;

    public double OxygenPercent { get; set; } = 20.9;
    public double CarbonDioxidePercent { get; set; } = 0.04;
    public double PressureKpa { get; set; } = 101.3;
    public bool VentilationEnabled { get; set; } = true;
    public bool HasVentilationControl { get; set; } = true;
    public bool IsVentilationAiControllable { get; set; } = true;

    // Exterior hatches model a real boundary to space rather than a fake
    // off-map kill action. At present the seeded Airlock owns one.
    public bool HasExteriorHatch { get; set; }
    public bool ExteriorHatchOpen { get; set; }
    public bool IsExteriorHatchAiControllable { get; set; }

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
    public bool IsAiControllable { get; set; } = true;
    public bool ManualOverrideAvailable { get; set; }
    public bool IsManuallyOverridden { get; set; }
    public int ManualOverrideMinutes { get; set; } = 3;
    public int ManualOverrideSkillRequired { get; set; } = 65;
    public bool CanBeForced { get; set; } = true;
    public int ForceDifficulty { get; set; } = 68;
    public int TechnicalDifficulty { get; set; } = 62;
    public bool IsDamaged { get; set; }

    public bool IsPassable => IsManuallyOverridden || (IsPowered && IsOpen && !IsLocked);

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
    public ScenarioDefinition? Scenario { get; set; }
    public ScenarioStatus ScenarioStatus { get; set; } = ScenarioStatus.Running;
    public string? ScenarioOutcome { get; set; }
    public LifeSupportState LifeSupport { get; } = new();
    public List<ShutdownMechanism> ShutdownMechanisms { get; } = [];
    public List<AudioCue> AudioCues { get; } = [];
    public long NextAudioCueSequence { get; set; } = 1;
}
