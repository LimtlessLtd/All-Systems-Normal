namespace Overseer.Domain;

public enum CrewRole
{
    Commander,
    Engineer,
    Security,
    Doctor,
    Technician,
    Scientist,
    Prisoner
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
    Containment,
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
    ResurrectionChamber,
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
    OverseerShutdown,
    CapacitorBank,
    PowerBus,
    CoolantPump,
    WaterRecycler,
    OxygenGenerator,
    CarbonScrubber,
    NetworkRack,
    DoorConsole
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

public enum ScenarioRosterPolicy
{
    FreshGenerated,
    CampaignContinuing
}

public enum PrisonerDangerLevel
{
    Low,
    Moderate,
    High,
    Extreme
}

public sealed record PrisonerDefinition(
    string Name,
    PrisonerDangerLevel DangerLevel,
    string StartRoomId = "containment",
    double ViolenceBias = 0);

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
    StationGenerationConstraints? StationConstraints = null,
    ScenarioRosterPolicy RosterPolicy = ScenarioRosterPolicy.CampaignContinuing,
    IReadOnlyList<PrisonerDefinition>? Prisoners = null);

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

/// <summary>
/// An unsettled pact offer, communicated but not yet a binding <see cref="CrewPact"/>.
/// The promisee's own cognition decides whether to accept it.
/// </summary>
public sealed record PactProposal(
    Guid FromNpcId,
    string FromNpcName,
    CrewPactKind Kind,
    string PromiseText,
    TimeSpan? TriggerAt,
    TimeSpan? Deadline,
    TimeSpan OfferedAt);

/// <summary>
/// An unsettled suggestion one crew member has made to another (owner idea
/// #6: emergent leadership via trust — "someone repeatedly fixing problems
/// gets listened to"). No new leader role and no compliance mechanic: C#
/// only exposes this alongside the target's existing <see cref="Relationship.Trust"/>
/// in the suggester; the target's own cognition independently decides
/// whether to act on it, ignore it, or do something else entirely.
/// </summary>
public sealed record NpcSuggestion(
    string FromNpcName,
    string SuggestionText,
    TimeSpan MadeAt);

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
    ProposePact,
    AcceptPact,
    FulfillPact,
    BreakPact,

    /// <summary>
    /// Owner idea #6 (emergent leadership via trust): suggest a concrete
    /// action to a co-located crew member. C# never scripts compliance —
    /// it only places the suggestion in the target's awareness
    /// (<see cref="Npc.PendingSuggestion"/>) alongside how much the target
    /// trusts the suggester; the target's own cognition independently
    /// decides whether to act on it.
    /// </summary>
    Suggest,

    RecruitShutdownAlly,
    JoinShutdownTeam,
    ShutdownOverseer,
    OverrideDoor,
    ForceDoor,

    /// <summary>
    /// Generic physical tamper affordance (owner idea #12): disconnect a
    /// locally accessible station device. C# records only the physical act and
    /// resulting device state; cognition owns the motive.
    /// </summary>
    DisconnectDevice,

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
    ReprogramTurret,
    IsolateSecurityController,
    PurgeSecurityController,

    // V0.10E open-ended crew affordances. These remain high-level intentions:
    // deterministic simulation validates targets, access, skills and outcomes.
    InspectEquipment,
    CheckOnCrew,
    AssistCrew,
    AskAboutLocation,
    MedicalCheckup,
    TreatInjury,
    AdministerMedication,
    CleanBlood,
    ResurrectCrew,
    CoordinateWork,
    ReassureCrew,
    MisleadCrew,
    ReportConcern,
    VerifyClaim,
    StandGuard,
    SeekSafety,
    FightFire,
    EvacuateHazard,
    SealHazardRoom,
    VentHazardRoom,
    OpenDoor,
    CloseDoor,
    LockDoor,
    UnlockDoor,

    /// <summary>
    /// Owner idea #3, slice 2: hide or retrieve one of your own personal
    /// possessions. Owner-only.
    /// </summary>
    HideItem,
    ReturnItem,

    /// <summary>
    /// Owner idea #3, slice 3: take a possession that is not your own — from
    /// a co-located crew member currently holding it (with or without their
    /// consent), or, for <see cref="StealItem"/> only, from a hiding spot you
    /// yourself believe (per <c>KnownPossessions</c>) is in your current
    /// room. Never omniscient.
    /// </summary>
    BorrowItem,
    StealItem,

    /// <summary>
    /// Owner idea #3, slice 5: destroy a possession — your own, one you
    /// currently hold after borrowing/stealing it, one a co-located crew
    /// member currently holds, or one hidden in your current room you
    /// yourself believe is there. Same knowledge/belief rules as
    /// <see cref="StealItem"/>; never omniscient.
    /// </summary>
    DestroyItem,

    /// <summary>
    /// Physically restrain an escaped prisoner and return them to containment.
    /// Always resolved deterministically by simulation, never by the mind.
    /// </summary>
    RecapturePrisoner
}


public enum StationSelectionKind
{
    Room,
    Crew,
    Door,
    Robot,
    Turret,
    Device,
    CropBed
}

/// <summary>
/// UI-neutral identity for anything the operator can inspect. Keeping selection
/// out of Razor lets both front ends route the same authoritative entities.
/// </summary>
public sealed record StationSelection(
    StationSelectionKind Kind,
    string Id);

/// <summary>
/// <paramref name="RumourHopCount"/> (owner idea #4) counts how many
/// retellings removed this memory is from the original direct witness: 0 for
/// something this person actually witnessed or was told deterministically
/// (e.g. a pact settlement, a search result), incremented by
/// <see cref="ConversationTopicSystem"/> each time gossip passes it on
/// again. Certainty/specificity degrades with hop count along a fixed table
/// rather than copying the previous holder's description verbatim.
/// <paramref name="RumourCoreDescription"/> is the original event text each
/// retelling degrades from — kept separate from the ever-changing
/// human-facing <paramref name="Description"/> so each new hop wraps the
/// same original content instead of re-wrapping an already-wrapped string
/// (which would nest indefinitely). Null for a hop-0 memory, whose own
/// <paramref name="Description"/> already is the core content.
/// <paramref name="IsFailedAttempt"/> marks a memory written by
/// <c>IntentExecutionSystem.FailIntent</c> — a rejected/impossible action
/// this person just tried. A plain importance value is not a reliable
/// enough marker for cognition to reliably notice "I already tried this"
/// (a fresh low-importance memory can be crowded out of the salience-based
/// RECENT/IMPORTANT MEMORIES prompt block by other same-tick memories), so
/// <c>NpcPromptBuilder</c> surfaces these in their own dedicated section
/// instead, guaranteed visible regardless of general salience competition.
/// </summary>
/// <param name="IsSensitive">
/// Owner idea #14 (secrets): this memory is something its holder would
/// reasonably want kept private, e.g. witnessing someone hide a possession.
/// Automatic background gossip (<c>ConversationTopicSystem</c>) never
/// selects a sensitive memory as a topic — the only way it reaches another
/// person is a deliberate LLM-authored social action naming a specific
/// target, exactly like any other judgment call cognition makes. No
/// disclosure consequence or blackmail affordance exists yet; those are
/// separate, later slices.
/// </param>
/// <param name="TraumaRoomId">
/// Owner idea #15 (fear conditioning): the room this memory is physically
/// tied to, set by <see cref="Simulation.FearConditioningSystem"/> for a
/// near-death survival memory. Null for an ordinary memory with no
/// location-specific dread attached. <c>NpcPromptBuilder</c> surfaces it so
/// cognition can weigh the dread; C# never blocks or biases room entry.
/// </param>
/// <param name="MoralActorName">
/// Owner idea #16 (moral disagreements): the crew member whose witnessed
/// decision this memory records (an attack, a theft, a broken promise,
/// venting a compartment with people inside). C# only tags who did what;
/// whether it was justified is cognition's judgment.
/// </param>
/// <param name="PanicClaimRoomId">
/// Owner idea #19 (collective panic cascades): the room a panicked crew
/// member was fleeing when this memory was recorded, set by
/// <see cref="Simulation.PanicAlertSystem"/>. Null for an ordinary memory.
/// The claim's source is named in <see cref="Description"/> when the hearer
/// could identify them, or left anonymous when only the shout itself
/// carried (e.g. through a hatch); either way, whether to react immediately,
/// investigate first, or ignore it is the hearer's own judgment call.
/// </param>
public sealed record Memory(
    string Description,
    TimeSpan OccurredAt,
    double Importance,
    int RumourHopCount = 0,
    string? RumourCoreDescription = null,
    bool IsFailedAttempt = false,
    bool IsSensitive = false,
    string? TraumaRoomId = null,
    string? MoralActorName = null,
    string? PanicClaimRoomId = null);

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

public enum CrewPactKind
{
    CoverShift,
    KeepQuiet,
    OweFavor,
    Other
}

public enum CrewPactStatus
{
    Active,
    Fulfilled,
    Broken
}

/// <summary>
/// Deterministic record of an interpersonal commitment. Cognition may choose
/// whether to make or honor a promise; simulation owns its durable state and
/// the consequences of settling it.
/// </summary>
public sealed class CrewPact
{
    public required string Id { get; init; }
    public required Guid PromisorId { get; init; }
    public required Guid PromiseeId { get; init; }
    public required CrewPactKind Kind { get; init; }
    public required string PromiseText { get; init; }
    public required TimeSpan CreatedAt { get; init; }
    public TimeSpan? TriggerAt { get; init; }
    public TimeSpan? Deadline { get; init; }
    public CrewPactStatus Status { get; set; } = CrewPactStatus.Active;
    public TimeSpan? SettledAt { get; set; }
    public string? SettlementNote { get; set; }
}

public enum CrewTaskStatus
{
    InProgress,
    Succeeded,
    Interrupted,
    Failed
}

/// <summary>
/// Authoritative timed physical work. Minds may request an action, but only
/// deterministic systems create, advance and resolve this state.
/// </summary>
public sealed class CrewTaskState
{
    public required ActionKind Action { get; init; }
    public string? TargetId { get; init; }
    public required string Description { get; init; }
    public required TimeSpan StartedAt { get; init; }
    public required TimeSpan CompletesAt { get; init; }
    public CrewTaskStatus Status { get; set; } = CrewTaskStatus.InProgress;
    public string Outcome { get; set; } = "In progress.";
    public double? FinalProgressPercent { get; set; }

    public double ProgressPercent(TimeSpan now)
    {
        if (Status == CrewTaskStatus.Succeeded)
            return 100;
        if (Status != CrewTaskStatus.InProgress)
            return Math.Clamp(FinalProgressPercent ?? 0, 0, 99.9);

        return Math.Clamp(
            (now - StartedAt).TotalSeconds
            / Math.Max(1, (CompletesAt - StartedAt).TotalSeconds) * 100,
            0,
            100);
    }
}

public sealed record NpcAction(
    ActionKind Kind,
    string? TargetId,
    string Reason,
    Guid? SubjectId = null);

public sealed record NpcIntent(
    ActionKind Action,
    string? TargetId,
    string Goal,
    string Reason,
    int Urgency,
    string Source,
    TimeSpan CreatedAt,
    Guid? SubjectId = null);

/// <summary>
/// One deterministic step queued behind an NPC's current <see cref="NpcIntent"/>
/// as part of a bounded <see cref="NpcPlan"/> (owner idea #5: crew-generated
/// multi-step plans). Carries only what is needed to become the next
/// <see cref="NpcIntent"/> when it activates; a queued step is never
/// pre-validated at plan-creation time, it is validated by the exact same
/// execution-time checks any freshly decided intent already goes through.
/// </summary>
public sealed record NpcPlanStep(ActionKind Action, string? TargetId, string Goal, Guid? SubjectId = null);

/// <summary>
/// A short ordered sequence of <see cref="NpcPlanStep"/>s a mind has
/// committed to pursuing one step at a time. The step at index 0 promotes
/// into <see cref="Npc.Intent"/> only once the current intent completes;
/// deterministic C# never chooses or validates plan content itself beyond
/// the existing per-intent execution checks, and the plan is abandoned
/// (<see cref="Npc.Plan"/> cleared) the moment a step fails, expires or is
/// pre-empted, rather than blindly continuing a plan whose premise may no
/// longer hold. Nothing produces an <see cref="NpcPlan"/> yet: this is the
/// deterministic foundation a future cognition slice builds on.
/// </summary>
public sealed record NpcPlan
{
    /// <summary>Bounded: a plan is a short list of near-term steps, not a script.</summary>
    public const int MaxSteps = 4;

    public IReadOnlyList<NpcPlanStep> Steps { get; }
    public string Reason { get; }
    public int Urgency { get; }
    public string Source { get; }

    private NpcPlan(IReadOnlyList<NpcPlanStep> steps, string reason, int urgency, string source)
    {
        Steps = steps;
        Reason = reason;
        Urgency = urgency;
        Source = source;
    }

    public static NpcPlan Create(IReadOnlyList<NpcPlanStep> steps, string reason, int urgency, string source)
    {
        if (steps is not { Count: > 0 and <= MaxSteps })
        {
            throw new ArgumentException(
                $"A plan must have between 1 and {MaxSteps} steps.",
                nameof(steps));
        }

        return new NpcPlan(steps, reason, urgency, source);
    }

    /// <summary>The remaining plan once its first step has been promoted, or null once none are left.</summary>
    public NpcPlan? WithoutFirstStep() =>
        Steps.Count <= 1 ? null : new NpcPlan(Steps.Skip(1).ToList(), Reason, Urgency, Source);
}

public sealed record CognitionTelemetryEntry(
    long Sequence,
    TimeSpan CreatedAt,
    Guid NpcId,
    string NpcName,
    string Source,
    string? Prompt,
    string? RawResponse,
    ActionKind? Action,
    string? TargetId,
    string? Goal,
    string? Reason,
    int? Urgency,
    string? Note);


public enum RoomStatusPlateSide
{
    Top,
    Bottom
}

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
    public double FacingDegrees { get; set; }
    public bool IsLocallyMoving { get; set; }
    public NpcMovement? Movement { get; set; }

    public RobotPolicy Policy { get; set; } = RobotPolicy.Friendly;
    public double Health { get; set; } = 100;
    public double BatteryPercent { get; set; } = 100;
    public bool ChargingEnabled { get; set; } = true;
    public bool IsNetworkIsolated { get; set; }
    public bool IsControlLinkCompromised { get; set; }
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
    public bool IsControlLinkCompromised { get; set; }
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
    double FacingDegrees = 0,
    string? DeviceId = null);

public sealed record BloodEvidence(
    string Id,
    Guid SourceNpcId,
    string RoomId,
    double X,
    double Y,
    double Severity,
    TimeSpan CreatedAt);

public enum PossessionKind
{
    FoodStash,
    Photograph,
    Medication,
    Tool,
    Keepsake,
    Weapon
}

/// <summary>
/// A small personally meaningful item (idea #3): a food stash, photograph,
/// medication, tool, keepsake or improvised weapon. Deterministic simulation
/// owns every state change; the LLM only ever chooses to
/// borrow/steal/hide/return/destroy it through existing affordances.
/// Exactly one of <see cref="CurrentHolderId"/> or <see cref="HiddenAtRoomId"/>
/// is set while the item is not destroyed; it starts held by its owner.
/// Owner idea #11 (contraband): hiding is gated on currently holding the
/// item, not owning it, so a thief can stash something they stole rather
/// than being forced to carry it in plain sight.
/// </summary>
public sealed class PersonalPossession
{
    public required string Id { get; init; }
    public required Guid OwnerId { get; init; }
    public required string Name { get; init; }
    public required PossessionKind Kind { get; init; }
    public Guid? CurrentHolderId { get; set; }
    public string? HiddenAtRoomId { get; set; }
    public string? HiddenAtFixtureLabel { get; set; }
    public bool IsDestroyed { get; set; }

    /// <summary>
    /// Owner idea #3, slices 4-5: whether the owner has already been given a
    /// "surprised realization" memory for what is currently true of this
    /// possession (who holds it, or that it's gone). Starts true (nothing to
    /// notice; they hold it themself). Every path that already grants the
    /// owner a direct memory (they were physically present, as the holder
    /// taken from or destroyed from) leaves this true. Only a theft or
    /// destruction the owner is absent for — the one case nobody tells them
    /// anything — sets it false, for <c>PossessionTheftNoticeSystem</c> to
    /// notice once the owner is physically back where they hid it. A destroyed
    /// item stays false after that notice, so the owner keeps seeing it as
    /// missing rather than learning it was destroyed.
    /// </summary>
    public bool OwnerAwareOfCurrentState { get; set; } = true;
}

/// <summary>
/// An observer's own belief about a possession they know exists, as of the
/// last time they actually perceived it — mirroring <see cref="CrewSighting"/>.
/// Exactly one of <see cref="HolderId"/> or <see cref="HiddenAtRoomId"/> is
/// set, reflecting what this observer last actually saw or was told, not the
/// item's live global state. A belief only updates through ambient co-located
/// perception of a held item, or by witnessing a hide/borrow/steal act.
/// </summary>
public sealed record PossessionSighting(
    string PossessionId,
    Guid? HolderId,
    string? HolderName,
    string? HiddenAtRoomId,
    string? HiddenAtFixtureLabel,
    TimeSpan ObservedAt);

public sealed class MedicalState
{
    public double Supplies { get; set; } = 12;
    public int MedicationDoses { get; set; } = 8;
    public int ResurrectionCharges { get; set; } = 1;
    public double ResurrectionEnergyKwh { get; set; } = 6;
}

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
    public double FacingDegrees { get; set; }
    public bool IsLocallyMoving { get; set; }
    public NpcMovement? Movement { get; set; }

    public double Health { get; set; } = 100;
    public double LastHealthSnapshot { get; set; } = 100;

    /// <summary>
    /// One-shot gate for <see cref="Simulation.FearConditioningSystem"/>
    /// (owner idea #15): true once the current low-health episode has already
    /// produced a traumatic memory, so a slow bleed-out records one memory
    /// per crisis instead of spamming a fresh one every tick while Health
    /// stays low. Resets once Health recovers above the near-death
    /// threshold, so a later, separate crisis can be recorded again.
    /// </summary>
    public bool NearDeathCrisisRecorded { get; set; }

    /// <summary>
    /// Owner idea #19 (collective panic cascades): true once this person's
    /// current dangerous-room flight has already sounded an audible panic
    /// claim for anyone in earshot, so a multi-tick escape produces one
    /// shout instead of spamming a fresh one every tick. Resets once they
    /// are no longer in a dangerous room, so a later, separate flight can be
    /// heard again. See <see cref="Simulation.PanicAlertSystem"/>.
    /// </summary>
    public bool PanicAlertSounded { get; set; }

    public TimeSpan? LastBloodEvidenceAt { get; set; }
    public TimeSpan? LastMedicalCheckupAt { get; set; }
    public Guid? MedicalPatientId { get; set; }
    public TimeSpan? MedicalActionCompletesAt { get; set; }

    /// <summary>
    /// The clinical procedure in progress. Kept apart from CurrentAction, which
    /// movement, routine and social systems rewrite while the procedure runs.
    /// </summary>
    public ActionKind? MedicalActionKind { get; set; }

    /// <summary>
    /// Injured colleagues this person has already reacted to, so seeing the same
    /// patient again does not force a fresh decision every minute.
    /// </summary>
    public HashSet<Guid> NoticedInjuredCrewIds { get; } = [];
    public double Hunger { get; set; } = 10;
    public double Fatigue { get; set; } = 10;

    /// <summary>
    /// Accumulated minutes spent awake during this person's scheduled sleep
    /// window. It drives deterministic fatigue, movement and cognition penalties.
    /// </summary>
    public double SleepDebtMinutes { get; set; }

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
    public PactProposal? PendingPactProposal { get; set; }

    /// <summary>
    /// A suggestion another crew member recently made to this person (owner
    /// idea #6). Expires unacted-on; see <see cref="NpcSuggestion"/>.
    /// </summary>
    public NpcSuggestion? PendingSuggestion { get; set; }

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
    public TimeSpan? LastDeathAnnouncementAt { get; set; }
    public bool IsPresent { get; set; } = true;
    public bool IsPrisoner { get; set; }
    public PrisonerDangerLevel PrisonerDangerLevel { get; set; } = PrisonerDangerLevel.Low;
    public double PrisonerViolenceBias { get; set; }

    /// <summary>
    /// True once this prisoner has physically breached containment and is at
    /// large. Deterministic escape/recapture logic owns this transition; it is
    /// never set directly by cognition.
    /// </summary>
    public bool HasEscapedContainment { get; set; }

    /// <summary>
    /// True while deterministic containment logic has committed the prisoner
    /// to a physical hatch crossing that has not yet succeeded or been blocked.
    /// </summary>
    public bool IsContainmentBreachInProgress { get; set; }

    public bool IsAlive => Health > 0;

    public HashSet<CropKind> FoodLikes { get; } = [];
    public HashSet<CropKind> FoodDislikes { get; } = [];

    /// <summary>
    /// Deterministic telemetry: simulated minutes this person has actually spent
    /// consuming a food for which they have a strong negative preference.
    /// </summary>
    public double DislikedFoodExposureMinutes { get; set; }

    public Dictionary<string, int> Skills { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deterministic accumulated minutes physically spent using station
    /// fixtures, keyed by a stable room/type/label identity. Personal-space
    /// systems derive preferences from repeated actual use rather than assigning
    /// scripted favourites.
    /// </summary>
    public Dictionary<string, double> FixtureUseMinutes { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deterministic count of successful authoritative tasks, grouped by action.
    /// This is history only; no decision logic reads it yet.
    /// </summary>
    public Dictionary<ActionKind, int> CompletedTaskCounts { get; } = [];

    public void RecordTaskCompletion(ActionKind action) =>
        CompletedTaskCounts[action] =
            CompletedTaskCounts.GetValueOrDefault(action) + 1;

    public Dictionary<string, Relationship> Relationships { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deterministic social clique this NPC currently belongs to, derived purely
    /// from the existing mutual-affinity relationship graph by
    /// <see cref="SocialClusterSystem"/>. Null while alone or estranged from
    /// everyone. Recomputed every tick; it amplifies in-clique gossip and lets
    /// witnessing clique-mates side with a friend in an argument, but it is
    /// present-state telemetry, not a stored allegiance.
    /// </summary>
    public int? CliqueId { get; set; }

    public List<Memory> Memories { get; } = [];
    public List<Belief> Beliefs { get; } = [];

    // Observer-specific knowledge. Missing-person logic must never infer remote
    // death/ejection directly from global IsAlive/IsPresent state.
    public Dictionary<Guid, CrewSighting> LastSeenCrew { get; } = [];
    public Dictionary<Guid, MissingPersonConcern> MissingPersonConcerns { get; } = [];
    public HashSet<string> ObservedUnsafeAirlocks { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ObservedBloodEvidenceIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Possessions (owned or otherwise) this person knows exist and their last
    // actually-observed state: their own items plus any they have witnessed
    // being held, hidden, borrowed or stolen, or found by searching. Reading
    // this key set answers "have I ever perceived this item"; reading a
    // value answers "what did I last actually see" — never the item's live
    // global state, which the observer may not know has changed since.
    public Dictionary<string, PossessionSighting> KnownPossessions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Faults this person has personally noticed, keyed "roomId:fault". Cleared
    // when the fault clears so a recurring failure can be noticed again.
    public HashSet<string> ObservedFaults { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public bool NeedsMindReconsideration { get; set; }

    public NpcAction CurrentAction { get; set; } =
        new(ActionKind.Idle, null, "Waiting for something to happen.");

    public NpcIntent? Intent { get; set; }

    /// <summary>
    /// Steps queued behind <see cref="Intent"/> as part of a bounded
    /// <see cref="NpcPlan"/> (owner idea #5). Promoted by the deterministic
    /// simulation's plan-execution system one step at a time; cleared
    /// whenever the current step fails, expires or is pre-empted rather than
    /// continued blindly.
    /// </summary>
    public NpcPlan? Plan { get; set; }

    public TimeSpan RoutineUntil { get; set; }

    /// <summary>
    /// Final room a deterministic planner is currently routing toward. The UI
    /// may visualize it, but it never decides movement from this value.
    /// </summary>
    public string? PlannedDestinationRoomId { get; set; }

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

    /// <summary>
    /// Current/most-recent authoritative timed work. Completed/interrupted work
    /// is retained until another task starts so the Inspector can show outcome.
    /// </summary>
    public CrewTaskState? ActiveTask { get; set; }

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
    /// <summary>What Overseer/crew have requested; actual operation also requires utilities.</summary>
    public bool RequestedOnline { get; set; } = true;
    public bool IsOnline { get; set; } = true;
    public bool IsAiControllable { get; set; } = true;
    public double OxygenReservePercent { get; set; } = 100;
    public double ScrubberEfficiencyPercent { get; set; } = 100;
    public double WaterReservePercent { get; set; } = 100;
    public bool OxygenGeneratorOnline { get; set; } = true;
    public bool CarbonScrubberOnline { get; set; } = true;
    public bool WaterRecyclerOnline { get; set; } = true;
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

    /// <summary>
    /// Generation-owned side reserved for the attached status plate. Procedural
    /// packing treats that external strip as occupied geometry.
    /// </summary>
    public RoomStatusPlateSide? StatusPlateSide { get; init; }

    public bool IsPowered { get; set; } = true;
    public bool LightsOn { get; set; } = true;
    public bool CameraOnline { get; set; } = true;
    public bool CameraNetworkReachable { get; set; } = true;

    public double TemperatureC { get; set; } = 21;
    public double TemperatureSetpointC { get; set; } = 21;
    public bool HasTemperatureControl { get; set; } = true;
    public bool TemperatureControlOnline { get; set; } = true;
    public bool IsTemperatureAiControllable { get; set; } = true;

    public double OxygenPercent { get; set; } = 20.9;
    public double CarbonDioxidePercent { get; set; } = 0.04;
    public double PressureKpa { get; set; } = 101.3;

    /// <summary>0..100 deterministic compartment fire severity.</summary>
    public double FireIntensity { get; set; }

    /// <summary>0..100 visible smoke contamination from fire.</summary>
    public double SmokePercent { get; set; }

    /// <summary>
    /// Derived line-of-sight remaining in the compartment. At extreme smoke
    /// density the room is effectively opaque even if its lights still work.
    /// </summary>
    public double VisibilityPercent =>
        Math.Clamp(100 - (SmokePercent * 1.18), 0, 100);

    /// <summary>0..100 physical pressure-hull condition for this compartment.</summary>
    public double HullIntegrityPercent { get; set; } = 100;

    /// <summary>
    /// A real opening to space. EnvironmentSystem treats this exactly like an
    /// exterior vacuum source so decompression uses the existing atmosphere graph.
    /// </summary>
    public bool HasHullBreach { get; set; }

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

    public bool HasVisualFeed => IsPowered && CameraOnline && CameraNetworkReachable;
}

public sealed class Door
{
    public required string Id { get; init; }
    public required string RoomAId { get; init; }
    public required string RoomBId { get; init; }

    public bool IsOpen { get; set; } = true;
    public bool IsLocked { get; set; }

    // True only while the current IsLocked=true state was set directly by the
    // Overseer lock verb (StationSession.ToggleLock), never by a crew member's
    // own LockDoor action. Every other code path that changes IsLocked resets
    // this back to false so it never survives a hand-off to a different cause.
    public bool LockedByOverseer { get; set; }
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

    // Crew may open an ordinary unlocked hatch while traversing it. The
    // authoritative simulation schedules the same hatch to close again once
    // traffic has cleared; rendering only reflects IsOpen.
    public Guid? LastCrewOperatorId { get; set; }
    public TimeSpan? CrewAutoCloseAt { get; set; }

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

    // Interpersonal commitments are authoritative deterministic state. Minds
    // decide whether to make/honor them; simulation records what actually happened.
    public List<CrewPact> CrewPacts { get; } = [];
    public long NextCrewPactSequence { get; set; } = 1;
    public List<StationRobot> Robots { get; } = [];
    public List<SecurityTurret> Turrets { get; } = [];
    public TimeSpan Elapsed { get; set; }
    public List<string> EventLog { get; } = [];
    public List<CognitionTelemetryEntry> CognitionTelemetry { get; } = [];
    public long NextCognitionTelemetrySequence { get; set; } = 1;
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
    public List<BloodEvidence> BloodEvidence { get; } = [];
    public List<PersonalPossession> Possessions { get; } = [];
    public MedicalState Medical { get; } = new();

    // Overseer's own voice. Messages are the player's only non-physical verb.
    public List<OverseerMessage> OverseerMessages { get; } = [];
    public long NextMessageSequence { get; set; } = 1;

    // Station upkeep. Equipment wears out, the crew service it, and the power
    // grid is what everything else depends on.
    public Dictionary<string, StationDevice> Devices { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public PowerGrid Power { get; } = new();
    public bool ControlNetworkOnline { get; set; } = true;
    public SecurityMalwareState SecurityMalware { get; } = new();

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
