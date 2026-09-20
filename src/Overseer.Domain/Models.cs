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

public enum ScenarioObjectiveKind
{
    /// <summary>Hold out for a fixed number of simulated minutes.</summary>
    SurviveMinutes,

    KeepCrewAlive,
    LifeSupportUptimePercent,

    /// <summary>
    /// Complete when the sponsor's mandatory directives are all satisfied,
    /// however long that takes. This is what an open-ended mission uses in place
    /// of a countdown: Overseer is patient, and the shift ends when the work is
    /// done rather than when a clock runs out.
    /// </summary>
    DirectivesSatisfied
}

public enum EvidenceOrigin
{
    DirectObservation,
    PhysicalDiscovery,
    Testimony,
    Inference
}

public enum InvestigationLeadStage
{
    Open,
    Checked,
    Resolved
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

public sealed record ScenarioObjective(
    string Id,
    string Title,
    string Description,
    ScenarioObjectiveKind Kind = ScenarioObjectiveKind.SurviveMinutes,
    double Target = 0,
    bool IsOptional = false);

public sealed class ScenarioObjectiveProgress
{
    public required string ObjectiveId { get; init; }
    public double Current { get; set; }
    public double Target { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFailed { get; set; }
    public string StatusText { get; set; } = "";
}

public sealed class ExperimentTelemetry
{
    public double SimulatedMinutes { get; set; }
    public double LifeSupportOnlineMinutes { get; set; }
    public int InvestigationsCompleted { get; set; }
    public int ShutdownControlsDiscovered { get; set; }
    public int EvidenceShared { get; set; }
    public int ShutdownTeamsFormed { get; set; }
    public int ShutdownAttempts { get; set; }
    public int RestrictiveDoorCommands { get; set; }
    public int AirlockSafetyBypasses { get; set; }
    public int Score { get; set; }
}

public sealed record ScenarioDefinition(
    string Id,
    string Title,
    string Briefing,
    ShutdownAccessVariant ShutdownVariant,
    IReadOnlyList<ScenarioObjective> Objectives,
    IReadOnlyList<CorporateDirective>? Directives = null,
    StationGenerationConstraints? StationConstraints = null);

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
    public int RequiredCrewCount { get; init; } = 1;
}

/// <summary>
/// A falsifiable assertion embedded in a piece of evidence. Claims are what
/// make deceit possible: an NPC who can directly observe the subject of a
/// claim can discover that the claim is no longer true.
/// </summary>
public enum EvidenceClaim
{
    /// <summary>Nothing about this evidence can be checked against the world.</summary>
    None,

    /// <summary>Overseer restricted access to the subject room.</summary>
    AccessRestricted,

    /// <summary>Overseer cut power to the subject room.</summary>
    PowerCut,

    /// <summary>Overseer disabled primary life support.</summary>
    LifeSupportDisabled,

    /// <summary>Overseer opened an exterior hatch.</summary>
    HatchOpened
}

public sealed record OverseerEvidence(
    string Description,
    double Weight,
    TimeSpan ObservedAt,
    string? SourceNpcName = null,
    EvidenceOrigin Origin = EvidenceOrigin.DirectObservation,
    string? LocationId = null,
    string? EvidenceId = null,
    string? SourceEvidenceId = null,
    double Reliability = 1,
    EvidenceClaim Claim = EvidenceClaim.None)
{
    /// <summary>
    /// Set when the NPC personally observed the world contradicting this claim.
    /// Discredited evidence keeps a residue of doubt rather than vanishing.
    /// </summary>
    public bool IsDiscredited { get; init; }

    /// <summary>
    /// Present weight after decay. Starts equal to <see cref="Weight"/> and is
    /// eroded by time, by reassurance, and by being contradicted.
    /// </summary>
    public double CurrentWeight { get; set; } = Weight;
}

public sealed class InvestigationLead
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required string RoomId { get; init; }
    public required TimeSpan CreatedAt { get; init; }
    public string? SourceEvidenceId { get; init; }
    public InvestigationLeadStage Stage { get; set; } = InvestigationLeadStage.Open;
    public TimeSpan? LastInvestigatedAt { get; set; }
}

public sealed record KnowledgeDiscovery(
    string Id,
    string Description,
    string RoomId,
    TimeSpan DiscoveredAt);

public sealed class ShutdownTeam
{
    public required string Id { get; init; }
    public required string MechanismId { get; init; }
    public required Guid LeaderId { get; init; }
    public required TimeSpan FormedAt { get; init; }
    public HashSet<Guid> MemberIds { get; } = [];
    public HashSet<Guid> InvitedNpcIds { get; } = [];
    public bool IsActive { get; set; } = true;
}

public sealed record ShutdownTeamInvitation(
    string TeamId,
    string MechanismId,
    string FromNpcName,
    string TargetRoomId,
    TimeSpan OfferedAt);


public enum AirlockCycleMode
{
    Idle,
    Pressurizing,
    Depressurizing
}

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
    RecruitShutdownAlly,
    JoinShutdownTeam,
    ShutdownOverseer,
    OverrideDoor,
    ForceDoor,
    RestoreSystem,
    SecureAirlock,
    TendCrops,
    Harvest,
    Cook,
    RepairDoor,
    WeldDoor,
    BarricadeDoor,
    ShutdownRobot,
    IsolateRobotNetwork,
    DisableRobotCharging,
    DamageRobot,
    ReprogramRobot,
    DisarmTurret,
    IsolateTurretNetwork,
    DisableTurretPower,
    DamageTurret,
    ReprogramTurret
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

public interface IStationMobileEntity
{
    string CurrentRoomId { get; set; }
    double PositionX { get; set; }
    double PositionY { get; set; }
    NpcMovement? Movement { get; set; }
}

public enum RobotPolicy
{
    Friendly,
    Neutral,
    Hostile
}

public sealed class StationRobot : IStationMobileEntity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CurrentRoomId { get; set; }
    public double PositionX { get; set; } = 50;
    public double PositionY { get; set; } = 50;
    public NpcMovement? Movement { get; set; }

    public RobotPolicy Policy { get; set; } = RobotPolicy.Friendly;
    public double Health { get; set; } = 100;
    public double BatteryPercent { get; set; } = 100;
    public bool ChargingEnabled { get; set; } = true;
    public bool IsNetworkIsolated { get; set; }
    public bool IsLocallyShutdown { get; set; }
    public bool IsRemotelyShutdown { get; set; }
    public string CurrentTask { get; set; } = "Awaiting assignment.";
    public string? TargetRoomId { get; set; }
    public Guid? TargetNpcId { get; set; }
    public TimeSpan? ActionCompletesAt { get; set; }
    public TimeSpan? NextAttackAt { get; set; }

    public bool IsDestroyed => Health <= 0;
    public bool HasRemoteControlLink => !IsNetworkIsolated && !IsDestroyed;
    public bool IsOperational =>
        !IsDestroyed
        && BatteryPercent > 0
        && !IsLocallyShutdown
        && !IsRemotelyShutdown;
}

public enum TurretPolicy
{
    Safe,
    ProtectOverseer,
    SuppressCrew
}

public sealed class SecurityTurret
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string RoomId { get; init; }
    public double PositionX { get; init; } = 50;
    public double PositionY { get; init; } = 50;

    public TurretPolicy Policy { get; set; } = TurretPolicy.Safe;
    public double Integrity { get; set; } = 100;
    public bool IsArmed { get; set; }
    public bool IsNetworkIsolated { get; set; }
    public bool PowerFeedEnabled { get; set; } = true;
    public int Ammunition { get; set; } = 12;
    public double Heat { get; set; }
    public Guid? TrackedNpcId { get; set; }
    public TimeSpan? NextShotAt { get; set; }
    public string CurrentTask { get; set; } = "Safe and disarmed.";

    public bool IsDestroyed => Integrity <= 0;
    public bool HasRemoteControlLink => !IsNetworkIsolated && !IsDestroyed;
}

public sealed record CrewSighting(
    Guid PersonId,
    string PersonName,
    string RoomId,
    TimeSpan SeenAt);

public enum MissingPersonConcernStage
{
    Concerned,
    Searching,
    Escalated
}

public sealed class MissingPersonConcern
{
    public required Guid PersonId { get; init; }
    public required string PersonName { get; init; }
    public TimeSpan? LastSeenAt { get; set; }
    public string? LastKnownRoomId { get; set; }
    public required string ExpectedRoomId { get; set; }
    public required TimeSpan FirstConcernAt { get; init; }
    public TimeSpan LastUpdatedAt { get; set; }
    public MissingPersonConcernStage Stage { get; set; } = MissingPersonConcernStage.Concerned;
    public HashSet<string> CheckedRoomIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SourceNpcName { get; init; }
    public TimeSpan? LastSharedAt { get; set; }
}

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

public sealed class Npc : IStationMobileEntity
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

    // V0.6C keeps exact observer-specific knowledge of physical shutdown
    // mechanisms. The bool remains as a compatibility/readability convenience,
    // but deterministic shutdown validation uses the exact mechanism IDs.
    public bool KnowsShutdownControl { get; set; }
    public HashSet<string> KnownShutdownMechanismIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, InvestigationLead> InvestigationLeads { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<KnowledgeDiscovery> Discoveries { get; } = [];
    public string? ShutdownTeamId { get; set; }
    public ShutdownTeamInvitation? PendingShutdownTeamInvitation { get; set; }

    /// <summary>
    /// How far this person takes Overseer at its word, 0..100. Being caught in
    /// a lie spends this; it is not the same thing as suspecting hostility.
    /// </summary>
    public double OverseerCredibility { get; set; } = 70;

    /// <summary>Claims Overseer has made to this person that are not yet settled.</summary>
    public List<OverseerClaimRecord> PendingOverseerClaims { get; } = [];

    /// <summary>Messages this person has received, newest first, for prompt context.</summary>
    public List<OverseerMessage> ReceivedMessages { get; } = [];

    /// <summary>
    /// Accounts already compared with a colleague, so one conversation is not
    /// replayed on every tick. Keys identify the pair of statements discussed.
    /// </summary>
    public HashSet<string> ComparedAccounts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? CauseOfDeath { get; set; }
    public bool IsPresent { get; set; } = true;
    public bool IsAlive => Health > 0;

    public Dictionary<string, int> Skills { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Relationship> Relationships { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<Memory> Memories { get; } = [];
    public List<Belief> Beliefs { get; } = [];

    // Observer-specific knowledge. Missing-person logic must never infer remote
    // death/ejection directly from global IsAlive/IsPresent state.
    public Dictionary<Guid, CrewSighting> LastSeenCrew { get; } = [];
    public Dictionary<Guid, MissingPersonConcern> MissingPersonConcerns { get; } = [];
    public HashSet<string> ObservedUnsafeAirlocks { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Faults this person has personally noticed, keyed "roomId:fault". Cleared
    // when the fault clears so a recurring failure can be noticed again.
    public HashSet<string> ObservedFaults { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public bool NeedsMindReconsideration { get; set; }

    public NpcAction CurrentAction { get; set; } =
        new(ActionKind.Idle, null, "Waiting for something to happen.");

    public NpcIntent? Intent { get; set; }
    public TimeSpan RoutineUntil { get; set; }

    /// <summary>Equipment this person is currently servicing, if any.</summary>
    public string? ServicingDeviceId { get; set; }

    /// <summary>Crop bed this person is currently tending or harvesting.</summary>
    public string? TendingBedId { get; set; }

    // The provisioning job in hand, held independently of Intent. Arriving
    // somewhere clears the intent that took you there, so a job that relied on
    // the intent surviving would be dropped and reassigned forever.
    public ActionKind? ProvisioningJob { get; set; }

    public string? ProvisioningRoomId { get; set; }

    /// <summary>
    /// When the provisioning job in hand finishes. Kept separate from
    /// ServiceCompletesAt: sharing one timer let the maintenance system clear
    /// the galley's clock every tick, so nothing was ever cooked.
    /// </summary>
    public TimeSpan? ProvisioningCompletesAt { get; set; }

    /// <summary>When the service visit in progress finishes.</summary>
    public TimeSpan? ServiceCompletesAt { get; set; }
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
    public AirlockCycleMode AirlockCycleMode { get; set; }
    public bool AirlockSafetyInterlocksEnabled { get; set; } = true;
    public bool IsAirlockSafetyAiControllable { get; set; } = true;
    public bool AirlockAlarmActive { get; set; }

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
    public int StructuralIntegrityPercent { get; set; } = 100;
    public bool IsTechnicallyBypassed { get; set; }
    public bool IsWelded { get; set; }
    public bool IsBarricaded { get; set; }
    public string? SecuredByNpcName { get; set; }

    public bool HasPhysicalSecuring => IsWelded || IsBarricaded;
    public bool IsPassable =>
        !HasPhysicalSecuring
        && (IsManuallyOverridden || (IsPowered && IsOpen && !IsLocked));

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
    public StationGenerationMetadata? StationGeneration { get; init; }
    public List<Npc> Crew { get; init; } = [];
    public List<StationRobot> Robots { get; } = [];
    public List<SecurityTurret> Turrets { get; } = [];
    public TimeSpan Elapsed { get; set; }
    public List<string> EventLog { get; } = [];
    public ScenarioDefinition? Scenario { get; set; }
    public ScenarioStatus ScenarioStatus { get; set; } = ScenarioStatus.Running;
    public string? ScenarioOutcome { get; set; }
    public LifeSupportState LifeSupport { get; } = new();
    public List<ShutdownMechanism> ShutdownMechanisms { get; } = [];
    public List<ShutdownTeam> ShutdownTeams { get; } = [];
    public Dictionary<string, ScenarioObjectiveProgress> ObjectiveProgress { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public ExperimentTelemetry Telemetry { get; } = new();
    public List<AudioCue> AudioCues { get; } = [];
    public long NextAudioCueSequence { get; set; } = 1;

    // Overseer's own voice. Messages are the player's only non-physical verb.
    public List<OverseerMessage> OverseerMessages { get; } = [];
    public long NextMessageSequence { get; set; } = 1;

    // Station upkeep. Equipment wears out, the crew service it, and the power
    // grid is what everything else depends on.
    public Dictionary<string, StationDevice> Devices { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public PowerGrid Power { get; } = new();

    // The food chain: beds in hydroponics, stores the galley draws on.
    public List<CropBed> CropBeds { get; } = [];
    public StationStores Stores { get; } = new();

    /// <summary>Seeded per game so a station's wear and tear is reproducible.</summary>
    public int UpkeepSeed { get; set; }

    // Corporate campaign layer. Directives are the assigned objectives; progress
    // is graded deterministically by CorporateDirectiveSystem.
    public List<CorporateDirective> Directives { get; } = [];

    public Dictionary<string, DirectiveProgress> DirectiveProgress { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>0..100 standing with the corporate sponsor.</summary>
    public double ComplianceScore { get; set; } = 100;
}
