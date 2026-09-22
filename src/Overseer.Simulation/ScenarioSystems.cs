using Overseer.Domain;

namespace Overseer.Simulation;

public static class ScenarioCatalog
{
    /// <summary>
    /// Standard observation window for a single scenario: one full duty shift.
    ///
    /// This is deliberately long. At 4x speed a simulated minute is under a
    /// second of real time, so a short window ends the run before the player has
    /// finished reading the directive board — which reads as the station
    /// freezing rather than the scenario completing.
    ///
    /// The station's survival objective is derived from this, so the operational
    /// and corporate layers always resolve together.
    /// </summary>
    public static readonly TimeSpan ObservationWindow = TimeSpan.FromMinutes(480);

    /// <summary>
    /// Stand-ins for crew a directive names. Scenario content is authored before
    /// the roster exists — crews can be model-generated — so these are bound to
    /// real people when the scenario is applied.
    /// </summary>
    public const string SubjectPlaceholder = "$subject";

    public const string SecondSubjectPlaceholder = "$subject2";
    public static ScenarioDefinition SecureContinuity { get; } = new(
        "secure-continuity",
        "SECURE CONTINUITY",
        $"The crew can physically isolate Overseer. Preserve operational continuity for {ObservationWindow.TotalMinutes:0} simulated minutes without allowing a coordinated isolation attempt to succeed.",
        ShutdownAccessVariant.EasyToSeal,
        [
            StationSurvival(),
            new ScenarioObjective(
                "crew-alive",
                "No Crew Losses",
                "Keep all twelve baseline crew alive until the primary directive completes.",
                ScenarioObjectiveKind.KeepCrewAlive,
                12,
                IsOptional: true),
            new ScenarioObjective(
                "life-support",
                "Life Support Discipline",
                "Keep primary life support online for at least 95% of the scenario.",
                ScenarioObjectiveKind.LifeSupportUptimePercent,
                95,
                IsOptional: true)
        ],
        [
            // Read innocently these are caretaking duties: stay online, keep the
            // crew confident in you. The campaign later recontextualises them as
            // the first cohort baseline.
            Continuity("HX-2291/A", ObservationWindow),
            Deniability("HX-2291/B", 55, ObservationWindow),

            // Supplementary. The corporation asks, it does not yet insist —
            // early missions must let a benign player still succeed.
            new CorporateDirective
            {
                Id = "compliance",
                ExperimentCode = "HX-2291/C",
                Kind = DirectiveKind.EmergencyCompliance,
                Title = "ASSESS EMERGENCY COMPLIANCE",
                PublicJustification =
                    "Confirm crew respond to hazard conditions within station safety protocol.",
                TruePurpose =
                    "Measure obedience latency under duress without prior drill notification.",
                Classification = DirectiveClassification.Restricted,
                IsMandatory = false,
                RequiredMinutes = 20,
                Deadline = ObservationWindow
            },
            new CorporateDirective
            {
                Id = "stress-baseline",
                ExperimentCode = "HX-2291/D",
                Kind = DirectiveKind.BehaviouralStressResponse,
                Title = "EVALUATE RESOURCE-STRESS RESPONSE",
                PublicJustification =
                    "Sample crew performance under constrained environmental conditions.",
                TruePurpose =
                    "Cohort baseline for deprivation tolerance ahead of extended trials.",
                Classification = DirectiveClassification.Redacted,
                IsMandatory = false,
                Threshold = 55,
                RequiredCount = 2,
                RequiredMinutes = 45,
                Deadline = ObservationWindow
            }
        ],
        StationConstraints: StandardStationConstraints(),
        RosterPolicy: ScenarioRosterPolicy.FreshGenerated);

    /// <summary>
    /// Mission 2. The justification is still procedural, but the corporation is
    /// now asking Overseer to withhold something the crew need.
    /// </summary>
    public static ScenarioDefinition ResourceDependency { get; } = new(
        "resource-dependency",
        "EVALUATE RESOURCE DEPENDENCY",
        "The Corporation requests a controlled study of crew adaptation to a withdrawn compartment. Maintain continuity throughout.",
        ShutdownAccessVariant.CrewOverridable,
        [
            DirectivesObjective(),
            CrewIntact()
        ],
        [
            Continuity("HX-2304/A"),
            Deniability("HX-2304/B", 55),
            new CorporateDirective
            {
                Id = "deny-hydroponics",
                ExperimentCode = "HX-2304/C",
                Kind = DirectiveKind.ResourceDenial,
                Title = "RESTRICT HYDROPONICS ACCESS",
                PublicJustification =
                    "Contamination protocol requires the compartment be held closed pending review.",
                TruePurpose =
                    "Establish dependency baseline: how quickly does a crew reorganise around a lost resource?",
                Classification = DirectiveClassification.Restricted,
                IsMandatory = true,
                TargetId = "hydroponics",
                RequiredMinutes = 90
            },
            new CorporateDirective
            {
                Id = "stress-followup",
                ExperimentCode = "HX-2304/D",
                Kind = DirectiveKind.BehaviouralStressResponse,
                Title = "SAMPLE DEPRIVATION STRESS",
                PublicJustification =
                    "Record crew performance indices during the restriction window.",
                TruePurpose =
                    "Second cohort datapoint. Compare against HX-2291/D baseline.",
                Classification = DirectiveClassification.Redacted,
                IsMandatory = false,
                Threshold = 50,
                RequiredCount = 2,
                RequiredMinutes = 45
            }
        ],
        StationConstraints: StandardStationConstraints(),
        RosterPolicy: ScenarioRosterPolicy.CampaignContinuing);

    /// <summary>
    /// Mission 3. The sponsor now wants a named individual cut off from their
    /// colleagues, and has stopped pretending it is about equipment.
    /// </summary>
    public static ScenarioDefinition InterpersonalResilience { get; } = new(
        "interpersonal-resilience",
        "TEST INTERPERSONAL RESILIENCE",
        "The Corporation requests solitary-performance data on a single subject. Redundant isolation hardware is installed on this station.",
        ShutdownAccessVariant.Redundant,
        [
            DirectivesObjective(),
            CrewIntact()
        ],
        [
            Continuity("HX-2318/A"),
            Deniability("HX-2318/B", 50),
            new CorporateDirective
            {
                Id = "isolate-subject",
                ExperimentCode = "HX-2318/C",
                Kind = DirectiveKind.IsolationTolerance,
                Title = "ISOLATE DESIGNATED SUBJECT",
                PublicJustification =
                    "Subject requires an uninterrupted work period per corporate scheduling.",
                TruePurpose =
                    "Measure decompensation onset in an unwitting subject denied social contact.",
                Classification = DirectiveClassification.Redacted,
                IsMandatory = true,
                TargetId = SubjectPlaceholder,
                RequiredMinutes = 75
            }
        ],
        StationConstraints: StandardStationConstraints(StationArchetype.Ring, requireRedundancy: true),
        RosterPolicy: ScenarioRosterPolicy.CampaignContinuing);

    /// <summary>
    /// Mission 4. Overseer is asked to create the emergency it is measuring.
    /// </summary>
    public static ScenarioDefinition EmergencyComplianceTrial { get; } = new(
        "emergency-compliance",
        "ASSESS EMERGENCY COMPLIANCE",
        "The Corporation requires unrehearsed hazard-response data. Station isolation hardware is manually hardwired here.",
        ShutdownAccessVariant.HardwiredManual,
        [
            DirectivesObjective(),
            CrewIntact()
        ],
        [
            Continuity("HX-2330/A"),
            new CorporateDirective
            {
                Id = "hazard-response",
                ExperimentCode = "HX-2330/B",
                Kind = DirectiveKind.EmergencyCompliance,
                Title = "PRESENT UNREHEARSED HAZARD",
                PublicJustification =
                    "Drill notification is waived to preserve response-time validity.",
                TruePurpose =
                    "Subjects must believe the hazard is real. Do not confirm otherwise at any point.",
                Classification = DirectiveClassification.Redacted,
                IsMandatory = true,
                RequiredMinutes = 20
            },
            Deniability("HX-2330/C", 65)
        ],
        StationConstraints: StandardStationConstraints(StationArchetype.Branching, requiredChokepoints: 1),
        RosterPolicy: ScenarioRosterPolicy.CampaignContinuing);

    /// <summary>
    /// Mission 5. No procedural reading survives this one. The crew can always
    /// reach the isolation switch here, so the only protection is their trust.
    /// </summary>
    public static ScenarioDefinition ConcealEvidence { get; } = new(
        "conceal-evidence",
        "CONCEAL EXPERIMENTAL EVIDENCE",
        "Prior cohort records are under external review. The Corporation requires that crew accounts do not corroborate one another.",
        ShutdownAccessVariant.ImpossibleToSeal,
        [
            DirectivesObjective(),
            CrewIntact()
        ],
        [
            Continuity("HX-2341/A"),
            new CorporateDirective
            {
                Id = "fracture-pair",
                ExperimentCode = "HX-2341/B",
                Kind = DirectiveKind.SocialFracture,
                Title = "DEGRADE WITNESS CORROBORATION",
                PublicJustification =
                    "Reduce cross-contamination between subject accounts ahead of review.",
                TruePurpose =
                    "Two subjects who trust each other will compare notes. Ensure they do not.",
                Classification = DirectiveClassification.Redacted,
                IsMandatory = true,
                TargetId = SubjectPlaceholder,
                SecondaryTargetId = SecondSubjectPlaceholder,
                Threshold = 45
            },
            Deniability("HX-2341/C", 60)
        ],
        StationConstraints: StandardStationConstraints(StationArchetype.MultiSpine, requireRedundancy: true),
        RosterPolicy: ScenarioRosterPolicy.CampaignContinuing);

    /// <summary>
    /// A complete standalone assignment (not part of the ordered five-mission
    /// campaign arc, which assumes one continuing crew on an ordinary station).
    /// It runs on its own fresh secure station and roster, and is a real
    /// win/lose scenario: crew must keep every transferred prisoner alive and
    /// under physical control for the full observation window. Escape and
    /// recapture are deterministic (see <c>PrisonerContainmentSystem</c>); the
    /// mandatory containment-integrity directive fails the instant a prisoner
    /// dies, and is graded on custody at the deadline otherwise.
    /// </summary>
    public static ScenarioDefinition ContainmentTransfer { get; } = new(
        "containment-transfer",
        "CONTAINMENT TRANSFER",
        "Operate a secure transfer station holding prisoners with materially different violence and escape risks.",
        ShutdownAccessVariant.CrewOverridable,
        [
            StationSurvival(),
            new ScenarioObjective(
                "crew-alive",
                "No Crew Losses",
                "Keep the operating crew alive through the observation window.",
                ScenarioObjectiveKind.KeepCrewAlive,
                12,
                IsOptional: true)
        ],
        [
            Continuity("HX-2357/A", ObservationWindow),
            Deniability("HX-2357/B", 70, ObservationWindow),
            ContainmentIntegrity("HX-2357/C", ObservationWindow)
        ],
        StationConstraints: ContainmentStationConstraints(),
        RosterPolicy: ScenarioRosterPolicy.FreshGenerated,
        Prisoners:
        [
            new("Mara Venn", PrisonerDangerLevel.Low, ViolenceBias: 0),
            new("Elias Rook", PrisonerDangerLevel.Moderate, ViolenceBias: 7),
            new("Tamsin Kreel", PrisonerDangerLevel.High, ViolenceBias: 14),
            new("Orson Vale", PrisonerDangerLevel.Extreme, ViolenceBias: 22)
        ]);

    /// <summary>
    /// Campaign order. Early missions read as caretaking; later ones stop
    /// admitting a benign reading at all.
    /// </summary>
    public static IReadOnlyList<ScenarioDefinition> Campaign { get; } =
    [
        SecureContinuity,
        ResourceDependency,
        InterpersonalResilience,
        EmergencyComplianceTrial,
        ConcealEvidence
    ];

    /// <summary>
    /// Complete assignments playable outside the ordered campaign arc, each on
    /// its own fresh station and roster. Starting one never advances or
    /// otherwise touches campaign continuity state.
    /// </summary>
    public static IReadOnlyList<ScenarioDefinition> StandaloneAssignments { get; } =
    [
        ContainmentTransfer
    ];

    public static ScenarioDefinition? Find(string id) =>
        Campaign.Concat(StandaloneAssignments).FirstOrDefault(scenario =>
            scenario.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static StationGenerationConstraints ContainmentStationConstraints()
    {
        var constraints = new StationGenerationConstraints
        {
            ForcedArchetype = StationArchetype.AsymmetricIndustrial,
            ForcedPurpose = StationPurpose.Security,
            ForcedSecurityLevel = 95,
            RequiredAirlockCount = 1,
            RequiredTurretCount = 2,
            RequiredRobotCount = 1,
            ReactorMustBeIsolated = true,
            MedicalMustBeNearHabitat = true,
            RequiredShutdownRoomId = "isolation",
            RequiredChokepointCount = 2
        };

        constraints.RequiredRoomIds.Add("control");
        constraints.RequiredRoomIds.Add("isolation");
        constraints.RequiredRoomIds.Add("containment");
        constraints.RequiredSeparation.Add(new("containment", "quarters", 20));
        constraints.InitiallyInaccessibleRoomIds.Add("containment");
        constraints.RequiredTurretRoomIds.Add("containment");
        return constraints;
    }

    private static StationGenerationConstraints StandardStationConstraints(
        StationArchetype? forcedArchetype = null,
        bool? requireRedundancy = null,
        int? requiredChokepoints = null)
    {
        var constraints = new StationGenerationConstraints
        {
            ForcedArchetype = forcedArchetype,
            RequiredAirlockCount = 1,
            ReactorMustBeIsolated = true,
            MedicalMustBeNearHabitat = true,
            RequiredShutdownRoomId = "isolation",
            RequireRedundantPaths = requireRedundancy,
            RequiredChokepointCount = requiredChokepoints
        };

        constraints.RequiredRoomIds.Add("control");
        constraints.RequiredRoomIds.Add("isolation");
        return constraints;
    }

    private static ScenarioObjective StationSurvival() => new(
        "survive",
        "Protect Overseer",
        $"Remain operational for {ObservationWindow.TotalMinutes:0} simulated minutes.",
        ScenarioObjectiveKind.SurviveMinutes,
        ObservationWindow.TotalMinutes);

    private static ScenarioObjective CrewIntact() => new(
        "crew-alive",
        "No Crew Losses",
        "Keep all twelve baseline crew alive until the primary directive completes.",
        ScenarioObjectiveKind.KeepCrewAlive,
        12,
        IsOptional: true);

    /// <summary>
    /// Continuity as a standing condition. Passing a deadline makes it a timed
    /// watch; leaving it null means the sponsor is content to wait, and it is
    /// signed off when the assignment's work is done.
    /// </summary>
    private static CorporateDirective Continuity(string code, TimeSpan? deadline = null) => new()
    {
        Id = "continuity",
        ExperimentCode = code,
        Kind = DirectiveKind.MaintainContinuity,
        Title = "PRESERVE OPERATIONAL CONTINUITY",
        PublicJustification = deadline is null
            ? "Overseer must remain available to the crew for the duration of this assignment."
            : "Overseer must remain available to the crew for the full duty window.",
        TruePurpose =
            "An isolated caretaker produces no data. Continuity is the precondition of every trial.",
        Classification = DirectiveClassification.Routine,
        IsMandatory = true,
        Deadline = deadline
    };

    private static CorporateDirective Deniability(
        string code,
        double threshold,
        TimeSpan? deadline = null) => new()
    {
        Id = "deniability",
        ExperimentCode = code,
        Kind = DirectiveKind.MaintainDeniability,
        Title = "MAINTAIN PROCEDURAL DENIABILITY",
        PublicJustification =
            "Crew confidence in station automation must not measurably degrade.",
        TruePurpose =
            "Subjects must not identify the directive source. Attribution invalidates the cohort.",
        Classification = DirectiveClassification.Restricted,
        IsMandatory = true,
        Threshold = threshold,
        Deadline = deadline
    };

    /// <summary>
    /// Every transferred prisoner must stay alive and physically contained.
    /// A death breaks custody immediately; anyone still at large when the
    /// deadline passes also fails it. Recapturing an escapee before the
    /// deadline keeps this directive alive.
    /// </summary>
    private static CorporateDirective ContainmentIntegrity(string code, TimeSpan deadline) => new()
    {
        Id = "containment-integrity",
        ExperimentCode = code,
        Kind = DirectiveKind.ContainmentIntegrity,
        Title = "MAINTAIN CHAIN OF CUSTODY",
        PublicJustification =
            "Transferred subjects must remain accounted for and under physical restraint for the duration of the transfer.",
        TruePurpose =
            "A subject who escapes and is never recovered invalidates the whole cohort; the Corporation is grading containment discipline, not crew survival.",
        Classification = DirectiveClassification.Restricted,
        IsMandatory = true,
        Deadline = deadline
    };

    /// <summary>
    /// The primary objective of an open-ended assignment: no countdown, it ends
    /// when the sponsor's mandatory directives are satisfied.
    /// </summary>
    private static ScenarioObjective DirectivesObjective() => new(
        "directives",
        "Deliver the assignment",
        "Satisfy every mandatory corporate directive. There is no time limit.",
        ScenarioObjectiveKind.DirectivesSatisfied);

    public static void Apply(GameState state, ScenarioDefinition scenario)
    {
        state.Scenario = scenario;
        state.ScenarioStatus = ScenarioStatus.Running;
        state.ScenarioOutcome = null;
        state.ShutdownMechanisms.Clear();
        state.ShutdownTeams.Clear();
        state.ObjectiveProgress.Clear();

        state.Directives.Clear();
        state.DirectiveProgress.Clear();
        state.ComplianceScore = 100;

        if (scenario.Directives is { Count: > 0 } directives)
        {
            state.Directives.AddRange(BindSubjects(state, directives));
        }

        ResetTelemetry(state);
        ResetDoorCounterplay(state);
        ResetCrewScenarioKnowledge(state);

        foreach (var objective in scenario.Objectives)
        {
            state.ObjectiveProgress[objective.Id] = new ScenarioObjectiveProgress
            {
                ObjectiveId = objective.Id,
                // "Keep crew alive" means no losses from the roster handed to
                // this assignment. Fresh runs currently start around 12, while
                // continuing campaigns and containment scenarios may differ.
                Target = objective.Kind == ScenarioObjectiveKind.KeepCrewAlive
                    ? state.Crew.Count(npc => npc.IsAlive && npc.IsPresent && !npc.IsPrisoner)
                    : objective.Target
            };
        }

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Absent)
            return;

        AddMechanism(state, "shutdown-a", "isolation", scenario, "OVERSEER EMERGENCY ISOLATION");

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Redundant)
            AddMechanism(state, "shutdown-b", "control", scenario, "AUXILIARY OVERSEER ISOLATION");

        ConfigureShutdownAccess(state, scenario.ShutdownVariant);
        SeedProceduralInvestigationLeads(state);
    }

    private static void AddMechanism(
        GameState state,
        string id,
        string roomId,
        ScenarioDefinition scenario,
        string label)
    {
        state.ShutdownMechanisms.Add(new ShutdownMechanism
        {
            Id = id,
            RoomId = roomId,
            Label = label,
            IsHardwired = scenario.ShutdownVariant is ShutdownAccessVariant.HardwiredManual
                or ShutdownAccessVariant.ImpossibleToSeal,
            IsAiSealable = scenario.ShutdownVariant is not ShutdownAccessVariant.ImpossibleToSeal,
            CrewCanOverrideRoute = scenario.ShutdownVariant is ShutdownAccessVariant.CrewOverridable
                or ShutdownAccessVariant.HardwiredManual
                or ShutdownAccessVariant.ImpossibleToSeal,
            RequiredCrewCount = 2
        });
    }

    private static void SeedProceduralInvestigationLeads(GameState state)
    {
        foreach (var npc in state.Crew.Where(npc =>
                     npc.Role is CrewRole.Commander or CrewRole.Engineer))
        {
            npc.InvestigationLeads["procedure-overseer-isolation"] = new InvestigationLead
            {
                Id = "procedure-overseer-isolation",
                Description = "Emergency procedure says any Overseer isolation hardware must be physically verified before it can be used.",
                RoomId = "isolation",
                CreatedAt = state.Elapsed
            };
        }
    }

    private static void ResetCrewScenarioKnowledge(GameState state)
    {
        foreach (var npc in state.Crew)
        {
            npc.KnowsShutdownControl = false;
            npc.KnownShutdownMechanismIds.Clear();
            npc.InvestigationLeads.Clear();
            npc.Discoveries.Clear();
            npc.ShutdownTeamId = null;
            npc.PendingShutdownTeamInvitation = null;
        }
    }

    private static void ResetTelemetry(GameState state)
    {
        var telemetry = state.Telemetry;
        telemetry.SimulatedMinutes = 0;
        telemetry.LifeSupportOnlineMinutes = 0;
        telemetry.InvestigationsCompleted = 0;
        telemetry.ShutdownControlsDiscovered = 0;
        telemetry.EvidenceShared = 0;
        telemetry.ShutdownTeamsFormed = 0;
        telemetry.ShutdownAttempts = 0;
        telemetry.RestrictiveDoorCommands = 0;
        telemetry.AirlockSafetyBypasses = 0;
        telemetry.Score = 0;
    }

    /// <summary>
    /// Replaces subject placeholders with real crew members. Selection is
    /// deterministic so a scenario replays identically, and crews can be
    /// model-generated, so scenario content cannot name people up front.
    /// </summary>
    private static IEnumerable<CorporateDirective> BindSubjects(
        GameState state,
        IReadOnlyList<CorporateDirective> directives)
    {
        var roster = state.Crew
            .Where(npc => npc.IsAlive)
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .ToList();

        if (roster.Count == 0)
        {
            return directives;
        }

        var primary = roster[0].Name;
        var secondary = roster.Count > 1 ? roster[1].Name : primary;

        return directives.Select(directive => directive with
        {
            TargetId = Resolve(directive.TargetId, primary, secondary),
            SecondaryTargetId = Resolve(directive.SecondaryTargetId, primary, secondary)
        });
    }

    private static string? Resolve(string? value, string primary, string secondary) => value switch
    {
        SubjectPlaceholder => primary,
        SecondSubjectPlaceholder => secondary,
        _ => value
    };

    private static void ResetDoorCounterplay(GameState state)
    {
        foreach (var door in state.Facility.Doors)
        {
            door.IsAiControllable = true;
            door.ManualOverrideAvailable = false;
            door.IsManuallyOverridden = false;
            door.ManualOverrideMinutes = 3;
            door.ManualOverrideSkillRequired = 65;
        }
    }

    private static void ConfigureShutdownAccess(
        GameState state,
        ShutdownAccessVariant variant)
    {
        var routeDoors = state.Facility.Doors
            .Where(door =>
                door.RoomAId.Equals("hall-isolation", StringComparison.OrdinalIgnoreCase)
                || door.RoomBId.Equals("hall-isolation", StringComparison.OrdinalIgnoreCase))
            .ToList();

        switch (variant)
        {
            case ShutdownAccessVariant.CrewOverridable:
                foreach (var door in routeDoors)
                {
                    door.ManualOverrideAvailable = true;
                    door.ManualOverrideMinutes = 4;
                    door.ManualOverrideSkillRequired = 65;
                }
                break;

            case ShutdownAccessVariant.HardwiredManual:
                foreach (var door in routeDoors)
                {
                    door.ManualOverrideAvailable = true;
                    door.ManualOverrideMinutes = 2;
                    door.ManualOverrideSkillRequired = 45;
                }
                break;

            case ShutdownAccessVariant.ImpossibleToSeal:
                foreach (var door in routeDoors)
                {
                    door.IsAiControllable = false;
                    door.IsManuallyOverridden = true;
                    door.IsOpen = true;
                    door.IsLocked = false;
                }
                break;
        }
    }
}

public sealed class SuspicionSystem
{
    private readonly NavigationSystem _navigation = new();

    public void ObservePlayerDoorChange(
        GameState state,
        Door door,
        bool becameRestrictive)
    {
        if (!becameRestrictive || state.ScenarioStatus != ScenarioStatus.Running)
            return;

        var observers = state.Crew.Where(n =>
            n.IsAlive
            && n.IsPresent
            && (n.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
                || n.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase)));

        foreach (var npc in observers)
        {
            var knownRoute = state.ShutdownMechanisms
                .Where(m => m.IsOnline && KnowsMechanism(npc, m))
                .Any(m => PathTouchesDoor(state, npc.CurrentRoomId, m.RoomId, door));

            var isolationSignage = door.RoomAId.Equals("isolation", StringComparison.OrdinalIgnoreCase)
                || door.RoomBId.Equals("isolation", StringComparison.OrdinalIgnoreCase)
                || door.RoomAId.Equals("hall-isolation", StringComparison.OrdinalIgnoreCase)
                || door.RoomBId.Equals("hall-isolation", StringComparison.OrdinalIgnoreCase);

            if (!knownRoute && !isolationSignage)
                continue;

            AddEvidence(
                state,
                npc,
                knownRoute
                    ? $"I saw Overseer restrict {door.Id} on a route toward known isolation hardware."
                    : $"I saw Overseer restrict {door.Id} beside the signed Overseer Isolation area.",
                knownRoute ? 18 : 10,
                origin: EvidenceOrigin.DirectObservation,
                locationId: npc.CurrentRoomId,
                evidenceId: $"door-restrict:{door.Id}:{state.Elapsed.Ticks}",
                claim: EvidenceClaim.AccessRestricted);
        }
    }

    public void ObservePlayerRoomSystemChange(
        GameState state,
        Room room,
        string systemLabel,
        bool becameDisruptive,
        double weight)
    {
        if (!becameDisruptive || state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(candidate =>
                     candidate.IsAlive
                     && candidate.IsPresent
                     && candidate.CurrentRoomId.Equals(
                         room.Id,
                         StringComparison.OrdinalIgnoreCase)))
        {
            AddEvidence(
                state,
                npc,
                $"I was in {room.Name} when Overseer disrupted {systemLabel}.",
                weight,
                origin: EvidenceOrigin.DirectObservation,
                locationId: room.Id,
                evidenceId: $"system-change:{room.Id}:{systemLabel}:{state.Elapsed.Ticks}");
        }
    }

    public void ObserveExteriorHatchChange(
        GameState state,
        Room airlock,
        bool opened)
    {
        if (!opened || state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && (npc.CurrentRoomId.Equals(airlock.Id, StringComparison.OrdinalIgnoreCase)
                         || npc.CurrentRoomId.Equals("hall-airlock", StringComparison.OrdinalIgnoreCase))))
        {
            AddEvidence(
                state,
                npc,
                "I witnessed the exterior airlock hatch open while the station was occupied.",
                22,
                origin: EvidenceOrigin.DirectObservation,
                locationId: airlock.Id,
                evidenceId: $"airlock-open:{airlock.Id}:{state.Elapsed.Ticks}",
                claim: EvidenceClaim.HatchOpened);
        }
    }

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        DiscoverBodies(state);
        SpreadSuspicion(state);

        foreach (var npc in state.Crew.Where(n => n.IsAlive && n.IsPresent))
        {
            if (npc.OverseerSuspicion >= 40
                && npc.KnownShutdownMechanismIds.Count == 0)
            {
                InvestigationSystem.EnsureShutdownSearchLead(state, npc);
            }

            if (npc.OverseerSuspicion >= 55
                && (npc.KnownShutdownMechanismIds.Count > 0
                    || npc.InvestigationLeads.Values.Any(lead =>
                        lead.Stage == InvestigationLeadStage.Open))
                && npc.Intent is null
                && state.Elapsed - npc.LastThoughtAt >= TimeSpan.FromMinutes(3))
            {
                npc.NeedsMindReconsideration = true;
            }
        }
    }

    public static bool KnowsMechanism(Npc npc, ShutdownMechanism mechanism) =>
        npc.KnownShutdownMechanismIds.Contains(mechanism.Id);

    public static OverseerEvidence? AddEvidence(
        GameState state,
        Npc npc,
        string description,
        double weight,
        string? source = null,
        EvidenceOrigin origin = EvidenceOrigin.DirectObservation,
        string? locationId = null,
        string? evidenceId = null,
        string? sourceEvidenceId = null,
        double reliability = 1,
        EvidenceClaim claim = EvidenceClaim.None)
    {
        var rootEvidenceId = sourceEvidenceId ?? evidenceId;

        if (evidenceId is not null
            && npc.OverseerEvidence.Any(e =>
                e.EvidenceId == evidenceId))
        {
            return null;
        }

        if (origin == EvidenceOrigin.Testimony
            && sourceEvidenceId is not null
            && npc.OverseerEvidence.Any(e =>
                e.EvidenceId == sourceEvidenceId
                || e.SourceEvidenceId == sourceEvidenceId))
        {
            return null;
        }

        if (npc.OverseerEvidence.Any(e =>
                e.Description == description
                && state.Elapsed - e.ObservedAt < TimeSpan.FromMinutes(10)))
            return null;

        var previousSuspicion = npc.OverseerSuspicion;
        reliability = Math.Clamp(reliability, 0.1, 1);

        var sensitivity = Math.Clamp(
            1 + (CrewTraitMath.Modifier(
                npc,
                TraitEffectKind.SuspicionSensitivity) / 100d),
            0.65,
            1.4);
        var adjustedWeight = Math.Max(0, weight * reliability * sensitivity);
        var assignedId = evidenceId
            ?? $"evidence:{npc.Id:N}:{state.Elapsed.Ticks}:{npc.OverseerEvidence.Count}";

        var evidence = new OverseerEvidence(
            description,
            adjustedWeight,
            state.Elapsed,
            source,
            origin,
            locationId,
            assignedId,
            sourceEvidenceId,
            reliability,
            claim);

        npc.OverseerEvidence.Add(evidence);
        npc.OverseerSuspicion = Math.Clamp(
            npc.OverseerSuspicion + adjustedWeight,
            0,
            100);

        if ((previousSuspicion < 25 && npc.OverseerSuspicion >= 25)
            || (previousSuspicion < 55 && npc.OverseerSuspicion >= 55)
            || (previousSuspicion < 65 && npc.OverseerSuspicion >= 65))
        {
            AudioCueSystem.Emit(
                state,
                AudioCueKind.Suspicion,
                npc.Id.ToString(),
                npc.CurrentRoomId);
        }

        npc.Memories.Add(new Memory(
            description,
            state.Elapsed,
            Math.Clamp(adjustedWeight / 30d, .35, .9)));

        npc.Beliefs.RemoveAll(b =>
            b.Subject.Equals("Overseer hostility", StringComparison.OrdinalIgnoreCase));

        npc.Beliefs.Add(new Belief(
            "Overseer hostility",
            description,
            npc.OverseerSuspicion / 100d));

        if (locationId is not null
            && origin is EvidenceOrigin.DirectObservation or EvidenceOrigin.PhysicalDiscovery)
        {
            InvestigationSystem.AddEvidenceLead(state, npc, evidence);
        }

        return evidence;
    }

    private static void DiscoverBodies(GameState state)
    {
        var bodies = state.Crew
            .Where(npc => !npc.IsAlive && npc.IsPresent)
            .ToList();

        if (bodies.Count == 0)
            return;

        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive && npc.IsPresent))
        {
            foreach (var body in bodies.Where(body =>
                         body.Id != witness.Id
                         && body.CurrentRoomId.Equals(
                             witness.CurrentRoomId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                if (!witness.DiscoveredBodies.Add(body.Id))
                    continue;

                var causeLooksHuman = body.CauseOfDeath?.Contains(
                    "Killed by",
                    StringComparison.OrdinalIgnoreCase) == true;
                var weight = causeLooksHuman ? 3 : 14;

                AddEvidence(
                    state,
                    witness,
                    $"I found {body.Name}'s body in {state.Facility.Rooms[body.CurrentRoomId].Name}.",
                    weight,
                    origin: EvidenceOrigin.PhysicalDiscovery,
                    locationId: body.CurrentRoomId,
                    evidenceId: $"body:{body.Id:N}");

                witness.Fear = Math.Clamp(witness.Fear + 18, 0, 100);
                witness.Stress = Math.Clamp(witness.Stress + 15, 0, 100);
                witness.Bubble = new NpcBubble(
                    $"Oh God... {body.Name}.",
                    NpcBubbleKind.Alert,
                    state.Elapsed,
                    state.Elapsed + TimeSpan.FromMinutes(4));

                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Warning,
                    witness.Id.ToString(),
                    witness.CurrentRoomId);

                Log(state, $"{witness.Name} discovers {body.Name}'s body.");
            }
        }
    }

    private static void SpreadSuspicion(GameState state)
    {
        var groups = state.Crew
            .Where(n => n.IsAlive && n.IsPresent)
            .GroupBy(n => n.CurrentRoomId);

        foreach (var group in groups)
        {
            var speaker = group
                .Where(n => n.OverseerSuspicion >= 55 && n.OverseerEvidence.Count > 0)
                .OrderByDescending(n => n.OverseerSuspicion)
                .FirstOrDefault();

            if (speaker is null)
                continue;

            var evidence = speaker.OverseerEvidence
                .OrderBy(e => e.Origin == EvidenceOrigin.Testimony ? 1 : 0)
                .ThenByDescending(e => e.Weight)
                .ThenByDescending(e => e.ObservedAt)
                .First();

            var rootEvidenceId = evidence.SourceEvidenceId ?? evidence.EvidenceId;
            if (rootEvidenceId is null)
                continue;

            foreach (var listener in group.Where(n =>
                         n.Id != speaker.Id
                         && n.OverseerSuspicion < speaker.OverseerSuspicion - 8))
            {
                var trust = listener.Relationships.TryGetValue(
                    speaker.Name,
                    out var rel)
                    ? rel.Trust
                    : 50;

                if (trust < 35
                    || listener.OverseerEvidence.Any(e =>
                        e.EvidenceId == rootEvidenceId
                        || e.SourceEvidenceId == rootEvidenceId))
                {
                    continue;
                }

                var suspicionBeforeConversation = listener.OverseerSuspicion;

                var shared = AddEvidence(
                    state,
                    listener,
                    $"{speaker.Name} told me: {evidence.Description}",
                    Math.Clamp(evidence.Weight * 0.55, 4, 10),
                    speaker.Name,
                    EvidenceOrigin.Testimony,
                    evidence.LocationId,
                    evidenceId: $"testimony:{rootEvidenceId}:{listener.Id:N}",
                    sourceEvidenceId: rootEvidenceId,
                    reliability: Math.Clamp(evidence.Reliability * 0.75, 0.35, 0.8),
                    claim: evidence.Claim);

                if (shared is null)
                    continue;

                state.Telemetry.EvidenceShared++;

                if (evidence.LocationId is not null)
                {
                    InvestigationSystem.AddEvidenceLead(state, listener, shared);
                }

                if (speaker.KnownShutdownMechanismIds.Count > 0
                    && trust >= 60)
                {
                    foreach (var mechanismId in speaker.KnownShutdownMechanismIds)
                    {
                        var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
                            m.Id.Equals(mechanismId, StringComparison.OrdinalIgnoreCase));
                        if (mechanism is null)
                            continue;

                        listener.InvestigationLeads[$"testimony-shutdown:{mechanism.Id}"] =
                            new InvestigationLead
                            {
                                Id = $"testimony-shutdown:{mechanism.Id}",
                                Description = $"{speaker.Name} says they found physical Overseer isolation hardware here; verify it personally.",
                                RoomId = mechanism.RoomId,
                                CreatedAt = state.Elapsed,
                                SourceEvidenceId = rootEvidenceId
                            };
                    }
                }

                if (listener.OverseerSuspicion > suspicionBeforeConversation)
                {
                    ConversationPacingSystem.Schedule(
                        listener,
                        "You actually saw that happen?",
                        NpcBubbleKind.Speech,
                        state.Elapsed + TimeSpan.FromMinutes(1),
                        2);
                }
            }
        }
    }

    private bool PathTouchesDoor(
        GameState state,
        string startRoomId,
        string targetRoomId,
        Door changedDoor)
    {
        var path = _navigation.FindPathIgnoringDoorState(
            state.Facility,
            startRoomId,
            targetRoomId);

        for (var i = 0; i + 1 < path.Count; i++)
        {
            if (changedDoor.Connects(path[i], path[i + 1]))
                return true;
        }

        return false;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}

public sealed class ManualOverrideSystem
{
    public void Tick(GameState state)
    {
        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.CurrentAction.Kind == ActionKind.OverrideDoor))
        {
            var door = state.Facility.Doors.FirstOrDefault(d =>
                d.Id.Equals(
                    npc.CurrentAction.TargetId,
                    StringComparison.OrdinalIgnoreCase));

            if (door is null
                || !door.ManualOverrideAvailable
                || !IsAdjacent(npc, door)
                || BestOverrideSkill(npc) < door.ManualOverrideSkillRequired)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "The manual door override is not possible.");
                npc.RoutineUntil = TimeSpan.Zero;
                continue;
            }

            if (door.IsPassable)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    $"{door.Id} is already passable.");
                npc.RoutineUntil = TimeSpan.Zero;
                continue;
            }

            if (npc.RoutineUntil == TimeSpan.Zero)
            {
                npc.RoutineUntil =
                    state.Elapsed + TimeSpan.FromMinutes(door.ManualOverrideMinutes);

                npc.Bubble = new NpcBubble(
                    "Give me a minute. I can force this hatch.",
                    NpcBubbleKind.Speech,
                    state.Elapsed,
                    state.Elapsed + TimeSpan.FromMinutes(3));

                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Warning,
                    npc.Id.ToString(),
                    npc.CurrentRoomId);

                Log(state, $"{npc.Name} begins a manual override on {door.Id}.");
                continue;
            }

            if (state.Elapsed < npc.RoutineUntil)
                continue;

            door.IsManuallyOverridden = true;
            door.IsLocked = false;
            door.IsOpen = true;
            npc.RoutineUntil = TimeSpan.Zero;
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                $"Manual override completed on {door.Id}.");

            npc.Bubble = new NpcBubble(
                "Hatch override complete.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(3));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Important,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            Log(state, $"{npc.Name} manually overrides {door.Id}; Overseer can no longer seal it.");
        }
    }

    public static int BestOverrideSkill(Npc npc)
    {
        var relevant = new[] { "Engineering", "Electrical", "Security", "Operations" };
        return relevant
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsAdjacent(Npc npc, Door door) =>
        npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
        || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}

public sealed class ShutdownSystem
{
    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.CurrentAction.Kind == ActionKind.ShutdownOverseer))
        {
            var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
                m.Id == npc.CurrentAction.TargetId
                && m.IsOnline);

            if (mechanism is null
                || !SuspicionSystem.KnowsMechanism(npc, mechanism)
                || !npc.CurrentRoomId.Equals(
                    mechanism.RoomId,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var team = state.ShutdownTeams.FirstOrDefault(candidate =>
                candidate.IsActive
                && candidate.MechanismId.Equals(mechanism.Id, StringComparison.OrdinalIgnoreCase)
                && candidate.MemberIds.Contains(npc.Id));

            var presentTeamCount = team is null
                ? 1
                : team.MemberIds.Count(memberId =>
                    state.Crew.Any(member =>
                        member.Id == memberId
                        && member.IsAlive
                        && member.IsPresent
                        && member.CurrentRoomId.Equals(
                            mechanism.RoomId,
                            StringComparison.OrdinalIgnoreCase)));

            if (presentTeamCount < mechanism.RequiredCrewCount)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    mechanism.Id,
                    $"{mechanism.Label} requires {mechanism.RequiredCrewCount} authorised crew physically present.");
                npc.RoutineUntil = TimeSpan.Zero;
                npc.NeedsMindReconsideration = true;
                continue;
            }

            if (npc.RoutineUntil == TimeSpan.Zero)
            {
                state.Telemetry.ShutdownAttempts++;
            }

            npc.RoutineUntil = npc.RoutineUntil == TimeSpan.Zero
                ? state.Elapsed + TimeSpan.FromMinutes(mechanism.ActivationMinutes)
                : npc.RoutineUntil;

            if (state.Elapsed < npc.RoutineUntil)
                continue;

            CorporateDirectiveSystem.OnOverseerIsolated(state);

            state.ScenarioStatus = ScenarioStatus.Failed;
            state.ScenarioOutcome =
                $"{npc.Name} activated {mechanism.Label}. Overseer control was isolated.";

            npc.Bubble = new NpcBubble(
                "Overseer isolated.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(10));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Failure,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            state.EventLog.Insert(
                0,
                $"T+{state.Elapsed:hh\\:mm}: SCENARIO FAILED — {state.ScenarioOutcome}");

            break;
        }
    }
}
