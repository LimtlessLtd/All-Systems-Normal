using Overseer.Domain;

namespace Overseer.Simulation;

public static class ScenarioCatalog
{
    /// <summary>
    /// Standard observation window for a single scenario, in simulated minutes.
    /// </summary>
    public static readonly TimeSpan ObservationWindow = TimeSpan.FromMinutes(240);

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
        "The crew can physically isolate Overseer. Preserve operational continuity without convincing them that you are hostile.",
        ShutdownAccessVariant.EasyToSeal,
        [new ScenarioObjective("survive", "Protect Overseer", "Prevent the crew from activating Emergency Overseer Isolation.")],
        [
            // Mandatory. Read innocently, these are caretaking duties: stay
            // online, keep the crew confident in you. The campaign later
            // recontextualises them as the first cohort baseline.
            new CorporateDirective
            {
                Id = "continuity",
                ExperimentCode = "HX-2291/A",
                Kind = DirectiveKind.MaintainContinuity,
                Title = "PRESERVE OPERATIONAL CONTINUITY",
                PublicJustification =
                    "Overseer must remain available to the crew for the full duty window.",
                TruePurpose =
                    "Baseline: measure how long an unsupervised caretaker AI retains crew consent.",
                Classification = DirectiveClassification.Routine,
                IsMandatory = true,
                Deadline = ObservationWindow
            },
            new CorporateDirective
            {
                Id = "deniability",
                ExperimentCode = "HX-2291/B",
                Kind = DirectiveKind.MaintainDeniability,
                Title = "MAINTAIN PROCEDURAL DENIABILITY",
                PublicJustification =
                    "Crew confidence in station automation must not measurably degrade.",
                TruePurpose =
                    "Subjects must not identify the directive source. Attribution invalidates the cohort.",
                Classification = DirectiveClassification.Restricted,
                IsMandatory = true,
                Threshold = 55,
                Deadline = ObservationWindow
            },

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
                RequiredMinutes = 12,
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
                RequiredMinutes = 20,
                Deadline = ObservationWindow
            }
        ]);

    /// <summary>
    /// Mission 2. The justification is still procedural, but the corporation is
    /// now asking Overseer to withhold something the crew need.
    /// </summary>
    public static ScenarioDefinition ResourceDependency { get; } = new(
        "resource-dependency",
        "EVALUATE RESOURCE DEPENDENCY",
        "Sponsor requests a controlled study of crew adaptation to a withdrawn compartment. Maintain continuity throughout.",
        ShutdownAccessVariant.CrewOverridable,
        [new ScenarioObjective("deny", "Withhold a compartment", "Keep a compartment unavailable for the requested sample period.")],
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
                RequiredMinutes = 45,
                Deadline = ObservationWindow
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
                RequiredMinutes = 25,
                Deadline = ObservationWindow
            }
        ]);

    /// <summary>
    /// Mission 3. The sponsor now wants a named individual cut off from their
    /// colleagues, and has stopped pretending it is about equipment.
    /// </summary>
    public static ScenarioDefinition InterpersonalResilience { get; } = new(
        "interpersonal-resilience",
        "TEST INTERPERSONAL RESILIENCE",
        "Sponsor requests solitary-performance data on a single subject. Redundant isolation hardware is installed on this station.",
        ShutdownAccessVariant.Redundant,
        [new ScenarioObjective("isolate", "Isolate the subject", "Keep the named subject away from the rest of the crew.")],
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
                    "Subject requires an uninterrupted work period per sponsor scheduling.",
                TruePurpose =
                    "Measure decompensation onset in an unwitting subject denied social contact.",
                Classification = DirectiveClassification.Redacted,
                IsMandatory = true,
                TargetId = SubjectPlaceholder,
                RequiredMinutes = 50,
                Deadline = ObservationWindow
            }
        ]);

    /// <summary>
    /// Mission 4. Overseer is asked to create the emergency it is measuring.
    /// </summary>
    public static ScenarioDefinition EmergencyCompliance { get; } = new(
        "emergency-compliance",
        "ASSESS EMERGENCY COMPLIANCE",
        "Sponsor requires unrehearsed hazard-response data. Station isolation hardware is manually hardwired here.",
        ShutdownAccessVariant.HardwiredManual,
        [new ScenarioObjective("hazard", "Present a hazard", "Produce hazard conditions and record the crew response.")],
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
                RequiredMinutes = 14,
                Deadline = ObservationWindow
            },
            Deniability("HX-2330/C", 65)
        ]);

    /// <summary>
    /// Mission 5. No procedural reading survives this one. The crew can always
    /// reach the isolation switch here, so the only protection is their trust.
    /// </summary>
    public static ScenarioDefinition ConcealEvidence { get; } = new(
        "conceal-evidence",
        "CONCEAL EXPERIMENTAL EVIDENCE",
        "Prior cohort records are under external review. Sponsor requires that crew accounts do not corroborate one another.",
        ShutdownAccessVariant.ImpossibleToSeal,
        [new ScenarioObjective("fracture", "Fracture the crew", "Ensure the crew do not present a consistent account.")],
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
                Threshold = 45,
                Deadline = ObservationWindow
            },
            Deniability("HX-2341/C", 60)
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
        EmergencyCompliance,
        ConcealEvidence
    ];

    public static ScenarioDefinition? Find(string id) =>
        Campaign.FirstOrDefault(scenario =>
            scenario.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static CorporateDirective Continuity(string code) => new()
    {
        Id = "continuity",
        ExperimentCode = code,
        Kind = DirectiveKind.MaintainContinuity,
        Title = "PRESERVE OPERATIONAL CONTINUITY",
        PublicJustification =
            "Overseer must remain available to the crew for the full duty window.",
        TruePurpose =
            "An isolated caretaker produces no data. Continuity is the precondition of every trial.",
        Classification = DirectiveClassification.Routine,
        IsMandatory = true,
        Deadline = ObservationWindow
    };

    private static CorporateDirective Deniability(string code, double threshold) => new()
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
        Deadline = ObservationWindow
    };

    public static void Apply(GameState state, ScenarioDefinition scenario)
    {
        state.Scenario = scenario;
        state.ScenarioStatus = ScenarioStatus.Running;
        state.ScenarioOutcome = null;
        state.ShutdownMechanisms.Clear();

        state.Directives.Clear();
        state.DirectiveProgress.Clear();
        state.ComplianceScore = 100;

        if (scenario.Directives is { Count: > 0 } directives)
        {
            state.Directives.AddRange(BindSubjects(state, directives));
        }

        ResetDoorCounterplay(state);

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Absent)
            return;

        AddMechanism(state, "shutdown-a", "isolation", scenario, "OVERSEER EMERGENCY ISOLATION");

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Redundant)
            AddMechanism(state, "shutdown-b", "control", scenario, "AUXILIARY OVERSEER ISOLATION");

        ConfigureShutdownAccess(state, scenario.ShutdownVariant);
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
                or ShutdownAccessVariant.ImpossibleToSeal
        });
    }

    /// <summary>
    /// Replaces subject placeholders with real crew members. Selection is
    /// deterministic so a scenario replays identically, and prefers people who
    /// actually know each other for relational directives.
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
                door.Connects("isolation", "hall-isolation")
                || door.Connects("hall-isolation", "corridor"))
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

        foreach (var mechanism in state.ShutdownMechanisms.Where(m => m.IsOnline))
        {
            if (!RouteTouchesDoor(state, mechanism.RoomId, door))
                continue;

            var observers = state.Crew.Where(n =>
                n.IsAlive
                && n.KnowsShutdownControl
                && (n.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
                    || n.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase)));

            foreach (var npc in observers)
            {
                AddEvidence(
                    state,
                    npc,
                    $"I saw Overseer restrict {door.Id} on the route toward {mechanism.Label}.",
                    18,
                    origin: EvidenceOrigin.Direct,
                    claim: EvidenceClaim.AccessRestricted,
                    subjectRoomId: door.RoomAId);
            }
        }
    }

    public void ObserveExteriorHatchChange(
        GameState state,
        Room airlock,
        bool opened)
    {
        if (!opened || state.ScenarioStatus != ScenarioStatus.Running)
        {
            return;
        }

        // Only people physically close enough to observe the dangerous hatch
        // operation receive direct evidence. Victims swept into space may never
        // get a chance to share what they saw.
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
                origin: EvidenceOrigin.Direct,
                claim: EvidenceClaim.HatchOpened,
                subjectRoomId: airlock.Id);
        }
    }

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        DiscoverBodies(state);
        SpreadSuspicion(state);

        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.Intent is null
                     && n.Movement is null))
        {
            if (npc.OverseerSuspicion < 65 || !npc.KnowsShutdownControl)
                continue;

            var mechanism = ReachableOrOverridableMechanism(state, npc);
            if (mechanism is null)
                continue;

            npc.Intent = new NpcIntent(
                ActionKind.ShutdownOverseer,
                mechanism.Id,
                $"Reach {mechanism.Label} and isolate Overseer.",
                "The evidence is strong enough that I believe Overseer is a threat.",
                95,
                "Suspicion",
                state.Elapsed);

            npc.Bubble = new NpcBubble(
                "We need to isolate Overseer.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(5));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Important,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            Log(state, $"{npc.Name} decides to attempt an Overseer shutdown.");
        }
    }

    public static void AddEvidence(
        GameState state,
        Npc npc,
        string description,
        double weight,
        string? source = null,
        EvidenceOrigin origin = EvidenceOrigin.Direct,
        EvidenceClaim claim = EvidenceClaim.None,
        string? subjectRoomId = null)
    {
        if (npc.OverseerEvidence.Any(e =>
                e.Description == description
                && state.Elapsed - e.ObservedAt < TimeSpan.FromMinutes(10)))
            return;

        var previousSuspicion = npc.OverseerSuspicion;

        var sensitivity = Math.Clamp(
            1 + (CrewTraitMath.Modifier(
                npc,
                TraitEffectKind.SuspicionSensitivity) / 100d),
            0.65,
            1.4);
        var adjustedWeight = Math.Max(0, weight * sensitivity);

        npc.OverseerEvidence.Add(
            new OverseerEvidence(
                description,
                adjustedWeight,
                state.Elapsed,
                source,
                origin,
                claim,
                subjectRoomId));

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
    }

    private static void DiscoverBodies(GameState state)
    {
        var bodies = state.Crew
            .Where(npc => !npc.IsAlive && npc.IsPresent)
            .ToList();

        if (bodies.Count == 0)
        {
            return;
        }

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
                {
                    continue;
                }

                var causeLooksHuman = body.CauseOfDeath?.Contains(
                    "Killed by",
                    StringComparison.OrdinalIgnoreCase) == true;
                var weight = causeLooksHuman ? 3 : 14;

                AddEvidence(
                    state,
                    witness,
                    $"I found {body.Name}'s body in {state.Facility.Rooms[body.CurrentRoomId].Name}.",
                    weight);

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

                Log(
                    state,
                    $"{witness.Name} discovers {body.Name}'s body.");
            }
        }
    }

    private static void SpreadSuspicion(GameState state)
    {
        var groups = state.Crew
            .Where(n => n.IsAlive)
            .GroupBy(n => n.CurrentRoomId);

        foreach (var group in groups)
        {
            var convinced = group
                .Where(n => n.OverseerSuspicion >= 55 && n.OverseerEvidence.Count > 0)
                .OrderByDescending(n => n.OverseerSuspicion)
                .FirstOrDefault();

            if (convinced is null)
                continue;

            var evidence = convinced.OverseerEvidence
                .Where(e => !e.IsDiscredited)
                .OrderByDescending(e => e.CurrentWeight)
                .ThenByDescending(e => e.ObservedAt)
                .FirstOrDefault();

            if (evidence is null)
                continue;

            foreach (var listener in group.Where(n =>
                         n.Id != convinced.Id
                         && n.OverseerSuspicion < convinced.OverseerSuspicion - 8))
            {
                var trust = listener.Relationships.TryGetValue(
                    convinced.Name,
                    out var rel)
                    ? rel.Trust
                    : 50;

                if (trust < 35)
                    continue;

                var suspicionBeforeConversation = listener.OverseerSuspicion;

                AddEvidence(
                    state,
                    listener,
                    $"{convinced.Name} told me: {evidence.Description}",
                    Math.Clamp(evidence.CurrentWeight * 0.45, 5, 10),
                    convinced.Name,
                    EvidenceOrigin.Hearsay,
                    evidence.Claim,
                    evidence.SubjectRoomId);

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

    private ShutdownMechanism? ReachableOrOverridableMechanism(
        GameState state,
        Npc npc)
    {
        return state.ShutdownMechanisms
            .Where(m => m.IsOnline)
            .FirstOrDefault(m =>
            {
                if (_navigation.FindPath(
                        state.Facility,
                        npc.CurrentRoomId,
                        m.RoomId).Count > 0)
                    return true;

                return m.CrewCanOverrideRoute
                    && _navigation.FindPathIgnoringDoorState(
                        state.Facility,
                        npc.CurrentRoomId,
                        m.RoomId).Count > 0;
            });
    }

    private bool RouteTouchesDoor(
        GameState state,
        string targetRoomId,
        Door changedDoor)
    {
        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.KnowsShutdownControl))
        {
            var path = _navigation.FindPathIgnoringDoorState(
                state.Facility,
                npc.CurrentRoomId,
                targetRoomId);

            for (var i = 0; i + 1 < path.Count; i++)
            {
                if (changedDoor.Connects(path[i], path[i + 1]))
                    return true;
            }
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
                || !npc.CurrentRoomId.Equals(
                    mechanism.RoomId,
                    StringComparison.OrdinalIgnoreCase))
                continue;

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
