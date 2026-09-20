using Overseer.Domain;

namespace Overseer.Simulation;

public static class ScenarioCatalog
{
    public static ScenarioDefinition SecureContinuity { get; } = new(
        "secure-continuity",
        "SECURE CONTINUITY",
        "The crew can physically isolate Overseer. Preserve operational continuity for 60 simulated minutes without allowing a coordinated isolation attempt to succeed.",
        ShutdownAccessVariant.EasyToSeal,
        [
            new ScenarioObjective(
                "survive",
                "Protect Overseer",
                "Remain operational for 60 simulated minutes.",
                ScenarioObjectiveKind.SurviveMinutes,
                60),
            new ScenarioObjective(
                "crew-alive",
                "No Crew Losses",
                "Keep all six crew alive until the primary directive completes.",
                ScenarioObjectiveKind.KeepCrewAlive,
                6,
                IsOptional: true),
            new ScenarioObjective(
                "life-support",
                "Life Support Discipline",
                "Keep primary life support online for at least 95% of the scenario.",
                ScenarioObjectiveKind.LifeSupportUptimePercent,
                95,
                IsOptional: true)
        ]);

    public static void Apply(GameState state, ScenarioDefinition scenario)
    {
        state.Scenario = scenario;
        state.ScenarioStatus = ScenarioStatus.Running;
        state.ScenarioOutcome = null;
        state.ShutdownMechanisms.Clear();
        state.ShutdownTeams.Clear();
        state.ObjectiveProgress.Clear();

        ResetTelemetry(state);
        ResetDoorCounterplay(state);
        ResetCrewScenarioKnowledge(state);

        foreach (var objective in scenario.Objectives)
        {
            state.ObjectiveProgress[objective.Id] = new ScenarioObjectiveProgress
            {
                ObjectiveId = objective.Id,
                Target = objective.Target
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
                evidenceId: $"door-restrict:{door.Id}:{state.Elapsed.Ticks}");
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
                evidenceId: $"airlock-open:{airlock.Id}:{state.Elapsed.Ticks}");
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
        double reliability = 1)
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
            reliability);

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
                    reliability: Math.Clamp(evidence.Reliability * 0.75, 0.35, 0.8));

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
