using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Builds observer-specific knowledge about unexplained absence. Authoritative
/// state is consulted only for direct physical perception in the same room.
/// </summary>
public sealed class MissingPersonSystem
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShareCooldown = TimeSpan.FromMinutes(30);

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ObserveCoLocatedCrew(state);
        ResolveKnownBodiesAndReunions(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);
        if (minute <= 0 || minute % (int)ScanInterval.TotalMinutes != 0)
            return;

        foreach (var observer in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            UpdateSearchKnowledge(state, observer);
            NoticeMissedExpectations(state, observer);
        }

        ShareConcerns(state);
    }

    private static void ObserveCoLocatedCrew(GameState state)
    {
        foreach (var group in state.Crew
                     .Where(npc => npc.IsAlive && npc.IsPresent)
                     .GroupBy(npc => npc.CurrentRoomId))
        {
            var people = group.ToList();

            foreach (var observer in people)
            {
                foreach (var other in people.Where(other => other.Id != observer.Id))
                {
                    observer.LastSeenCrew[other.Id] = new CrewSighting(
                        other.Id,
                        other.Name,
                        observer.CurrentRoomId,
                        state.Elapsed);
                }
            }
        }
    }

    private static void ResolveKnownBodiesAndReunions(GameState state)
    {
        foreach (var observer in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            foreach (var concern in observer.MissingPersonConcerns.Values.ToList())
            {
                var target = state.Crew.FirstOrDefault(candidate => candidate.Id == concern.PersonId);
                if (target is null)
                {
                    observer.MissingPersonConcerns.Remove(concern.PersonId);
                    continue;
                }

                if (observer.DiscoveredBodies.Contains(target.Id))
                {
                    observer.MissingPersonConcerns.Remove(target.Id);
                    RemoveWhereaboutsBelief(observer, target.Id);
                    continue;
                }

                var physicallyHere =
                    target.IsPresent
                    && target.CurrentRoomId.Equals(observer.CurrentRoomId, StringComparison.OrdinalIgnoreCase);

                if (!physicallyHere || !target.IsAlive)
                    continue;

                observer.MissingPersonConcerns.Remove(target.Id);
                RemoveWhereaboutsBelief(observer, target.Id);
                observer.NeedsMindReconsideration = true;
                observer.Memories.Add(new Memory(
                    $"Found {target.Name} safe in {state.Facility.Rooms[target.CurrentRoomId].Name}.",
                    state.Elapsed,
                    .42));
                observer.Bubble = new NpcBubble(
                    $"There you are, {FirstName(target.Name)}.",
                    NpcBubbleKind.Speech,
                    state.Elapsed,
                    state.Elapsed + TimeSpan.FromMinutes(3));

                Log(state, $"{observer.Name} finds {target.Name} safe and drops the missing-person concern.");
            }
        }
    }

    private static void NoticeMissedExpectations(GameState state, Npc observer)
    {
        foreach (var target in state.Crew.Where(target =>
                     target.Id != observer.Id
                     && !observer.DiscoveredBodies.Contains(target.Id)
                     && !observer.MissingPersonConcerns.ContainsKey(target.Id)))
        {
            var targetPhysicallyHere =
                target.IsPresent
                && target.CurrentRoomId.Equals(observer.CurrentRoomId, StringComparison.OrdinalIgnoreCase);

            if (targetPhysicallyHere)
                continue;

            var expectedRoomId = CrewDutySchedule.ExpectedDutyRoomId(target.Role, state.Elapsed);
            var sighting = observer.LastSeenCrew.GetValueOrDefault(target.Id);
            var unseenFor = sighting is null ? state.Elapsed : state.Elapsed - sighting.SeenAt;
            var directDangerEvidence = HasDirectDangerEvidence(
                state,
                observer,
                target,
                sighting);
            var threshold = directDangerEvidence
                ? TimeSpan.FromMinutes(30)
                : TimeSpan.FromHours(12);

            var personallyNoticedMissedDuty =
                observer.CurrentRoomId.Equals(expectedRoomId, StringComparison.OrdinalIgnoreCase)
                && unseenFor >= threshold;

            var missedExpectedCheckIn =
                ShouldExpectCheckIn(observer, target)
                && unseenFor >= threshold;

            if (!personallyNoticedMissedDuty && !missedExpectedCheckIn)
                continue;

            StartConcern(
                state,
                observer,
                target,
                expectedRoomId,
                sighting,
                sourceNpcName: null,
                personallyNoticedMissedDuty);
        }
    }

    private static void StartConcern(
        GameState state,
        Npc observer,
        Npc target,
        string expectedRoomId,
        CrewSighting? sighting,
        string? sourceNpcName,
        bool personallyNoticedMissedDuty)
    {
        var concern = new MissingPersonConcern
        {
            PersonId = target.Id,
            PersonName = target.Name,
            LastSeenAt = sighting?.SeenAt,
            LastKnownRoomId = sighting?.RoomId,
            ExpectedRoomId = expectedRoomId,
            FirstConcernAt = state.Elapsed,
            LastUpdatedAt = state.Elapsed,
            SourceNpcName = sourceNpcName
        };

        observer.MissingPersonConcerns[target.Id] = concern;
        // Ordinary absence is information, not an emergency. A person can be
        // elsewhere for hours without this pre-empting work, meals or repairs.
        observer.Stress = Math.Clamp(observer.Stress + 1, 0, 100);

        var expectedRoom = state.Facility.Rooms[expectedRoomId].Name;
        var statement = personallyNoticedMissedDuty
            ? $"{target.Name} was expected around {expectedRoom}, but I could not find them there."
            : $"{target.Name} has missed an expected check-in and I have not seen them recently.";

        SetWhereaboutsBelief(observer, target.Id, statement, .55);
        observer.Memories.Add(new Memory(statement, state.Elapsed, .58));
        observer.Bubble = new NpcBubble(
            $"Has anyone seen {FirstName(target.Name)}?",
            NpcBubbleKind.Speech,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(3));

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Important,
            observer.Id.ToString(),
            observer.CurrentRoomId);

        Log(state, $"{observer.Name} becomes concerned that {target.Name} is missing.");
    }

    private static void UpdateSearchKnowledge(GameState state, Npc observer)
    {
        foreach (var concern in observer.MissingPersonConcerns.Values.ToList())
        {
            var target = state.Crew.FirstOrDefault(candidate => candidate.Id == concern.PersonId);
            if (target is null)
                continue;

            var targetPhysicallyHere =
                target.IsPresent
                && target.CurrentRoomId.Equals(observer.CurrentRoomId, StringComparison.OrdinalIgnoreCase);

            if (targetPhysicallyHere)
                continue;

            var isPlausibleCheckRoom =
                observer.CurrentRoomId.Equals(concern.ExpectedRoomId, StringComparison.OrdinalIgnoreCase)
                || (concern.LastKnownRoomId is not null
                    && observer.CurrentRoomId.Equals(concern.LastKnownRoomId, StringComparison.OrdinalIgnoreCase))
                || (observer.CurrentAction.Kind == ActionKind.Investigate
                    && observer.CurrentAction.TargetId is not null
                    && observer.CurrentRoomId.Equals(
                        observer.CurrentAction.TargetId,
                        StringComparison.OrdinalIgnoreCase));

            if (isPlausibleCheckRoom && concern.CheckedRoomIds.Add(observer.CurrentRoomId))
            {
                concern.Stage = concern.CheckedRoomIds.Count >= 2
                    ? MissingPersonConcernStage.Escalated
                    : MissingPersonConcernStage.Searching;
                concern.LastUpdatedAt = state.Elapsed;
                observer.NeedsMindReconsideration = true;
                observer.Stress = Math.Clamp(observer.Stress + 2, 0, 100);

                var roomName = state.Facility.Rooms[observer.CurrentRoomId].Name;
                var statement = $"I checked {roomName}; {concern.PersonName} was not there.";
                SetWhereaboutsBelief(
                    observer,
                    concern.PersonId,
                    statement,
                    concern.Stage == MissingPersonConcernStage.Escalated ? .82 : .68);

                observer.Memories.Add(new Memory(
                    statement,
                    state.Elapsed,
                    concern.Stage == MissingPersonConcernStage.Escalated ? .72 : .55));
                observer.Bubble = new NpcBubble(
                    $"{FirstName(concern.PersonName)} isn't here.",
                    NpcBubbleKind.Alert,
                    state.Elapsed,
                    state.Elapsed + TimeSpan.FromMinutes(3));

                Log(state, $"{observer.Name} checks {roomName} while looking for {concern.PersonName}.");

                if (concern.Stage == MissingPersonConcernStage.Escalated)
                    AddContextualOverseerSuspicion(state, observer, concern);
            }

            if (concern.Stage != MissingPersonConcernStage.Escalated
                && concern.CheckedRoomIds.Count > 0
                && state.Elapsed - concern.FirstConcernAt >= TimeSpan.FromMinutes(90))
            {
                concern.Stage = MissingPersonConcernStage.Escalated;
                concern.LastUpdatedAt = state.Elapsed;
                observer.NeedsMindReconsideration = true;

                SetWhereaboutsBelief(
                    observer,
                    concern.PersonId,
                    $"{concern.PersonName} is now seriously overdue; at least one likely location has been checked.",
                    .82);

                AddContextualOverseerSuspicion(state, observer, concern);
            }
        }
    }

    private static void AddContextualOverseerSuspicion(
        GameState state,
        Npc observer,
        MissingPersonConcern concern)
    {
        var witnessedUnsafeAirlock = observer.OverseerEvidence.Any(evidence =>
            state.Elapsed - evidence.ObservedAt <= TimeSpan.FromMinutes(45)
            && evidence.Description.Contains(
                "exterior airlock hatch open",
                StringComparison.OrdinalIgnoreCase));

        if (!witnessedUnsafeAirlock)
            return;

        var sourceEvidence = observer.OverseerEvidence
            .Where(evidence =>
                state.Elapsed - evidence.ObservedAt <= TimeSpan.FromMinutes(45)
                && evidence.Description.Contains(
                    "exterior airlock hatch open",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(evidence => evidence.ObservedAt)
            .FirstOrDefault();

        SuspicionSystem.AddEvidence(
            state,
            observer,
            $"{concern.PersonName} is missing after I witnessed the exterior airlock hatch open.",
            9,
            origin: EvidenceOrigin.Inference,
            locationId: concern.LastKnownRoomId,
            evidenceId: $"missing-airlock-inference:{concern.PersonId:N}:{observer.Id:N}",
            sourceEvidenceId: sourceEvidence?.EvidenceId,
            reliability: .8);
    }

    private static void ShareConcerns(GameState state)
    {
        foreach (var group in state.Crew
                     .Where(npc => npc.IsAlive && npc.IsPresent)
                     .GroupBy(npc => npc.CurrentRoomId))
        {
            var people = group.OrderBy(npc => npc.Name).ToList();

            foreach (var source in people)
            {
                var concern = source.MissingPersonConcerns.Values
                    .OrderByDescending(item => item.Stage)
                    .ThenBy(item => item.FirstConcernAt)
                    .FirstOrDefault();

                if (concern is null
                    || concern.Stage == MissingPersonConcernStage.Concerned
                    || (concern.LastSharedAt is { } sharedAt
                        && state.Elapsed - sharedAt < ShareCooldown))
                {
                    continue;
                }

                var listener = people
                    .Where(candidate =>
                        candidate.Id != source.Id
                        && candidate.Id != concern.PersonId
                        && !candidate.MissingPersonConcerns.ContainsKey(concern.PersonId))
                    .OrderByDescending(candidate =>
                        candidate.Relationships.TryGetValue(source.Name, out var relationship)
                            ? relationship.Trust
                            : 0)
                    .ThenBy(candidate => candidate.Name)
                    .FirstOrDefault(candidate =>
                        candidate.Relationships.TryGetValue(source.Name, out var relationship)
                        && relationship.Trust >= 35);

                if (listener is null)
                    continue;

                listener.MissingPersonConcerns[concern.PersonId] =
                    new MissingPersonConcern
                    {
                        PersonId = concern.PersonId,
                        PersonName = concern.PersonName,
                        LastSeenAt = concern.LastSeenAt,
                        LastKnownRoomId = concern.LastKnownRoomId,
                        ExpectedRoomId = concern.ExpectedRoomId,
                        FirstConcernAt = state.Elapsed,
                        LastUpdatedAt = state.Elapsed,
                        SourceNpcName = source.Name,
                        Stage = MissingPersonConcernStage.Concerned
                    };

                concern.LastSharedAt = state.Elapsed;
                listener.Memories.Add(new Memory(
                    $"{source.Name} told me they cannot find {concern.PersonName}.",
                    state.Elapsed,
                    .5));
                SetWhereaboutsBelief(
                    listener,
                    concern.PersonId,
                    $"{source.Name} says {concern.PersonName} is unexpectedly absent.",
                    .5);

                ConversationPacingSystem.Schedule(
                    source,
                    $"I can't find {FirstName(concern.PersonName)} anywhere.",
                    NpcBubbleKind.Speech,
                    state.Elapsed,
                    3);
                ConversationPacingSystem.Schedule(
                    listener,
                    "I'll keep an eye out.",
                    NpcBubbleKind.Speech,
                    state.Elapsed + TimeSpan.FromMinutes(1),
                    2);

                Log(state, $"{source.Name} tells {listener.Name} that {concern.PersonName} is missing.");
            }
        }
    }

    private static bool HasDirectDangerEvidence(
        GameState state,
        Npc observer,
        Npc target,
        CrewSighting? sighting)
    {
        // A visible injury trace from this exact person is genuine evidence and
        // can justify checking much sooner than the ordinary 12-hour absence rule.
        var observedTargetBlood = state.BloodEvidence.Any(evidence =>
            evidence.SourceNpcId == target.Id
            && observer.ObservedBloodEvidenceIds.Contains(evidence.Id)
            && state.Elapsed - evidence.CreatedAt <= TimeSpan.FromHours(2));

        if (observedTargetBlood)
            return true;

        // An unsafe exterior hatch is only relevant to this person's absence if
        // the observer personally saw them in that airlock recently. This avoids
        // turning a generic station alarm into omniscient missing-person panic.
        if (sighting is null
            || state.Elapsed - sighting.SeenAt > TimeSpan.FromHours(2)
            || !state.Facility.Rooms.TryGetValue(sighting.RoomId, out var lastRoom)
            || lastRoom.Type != RoomType.Airlock)
        {
            return false;
        }

        return observer.OverseerEvidence.Any(evidence =>
            state.Elapsed - evidence.ObservedAt <= TimeSpan.FromHours(2)
            && evidence.Description.Contains(
                "exterior airlock hatch open",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldExpectCheckIn(Npc observer, Npc target)
    {
        if (observer.Role is CrewRole.Commander or CrewRole.Security)
            return true;

        return observer.Relationships.TryGetValue(target.Name, out var relationship)
            && relationship.Trust + relationship.Affinity >= 130;
    }

    private static void SetWhereaboutsBelief(
        Npc observer,
        Guid personId,
        string statement,
        double confidence)
    {
        var subject = BeliefSubject(personId);
        observer.Beliefs.RemoveAll(belief =>
            belief.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase));
        observer.Beliefs.Add(new Belief(
            subject,
            statement,
            Math.Clamp(confidence, 0, 1)));
    }

    private static void RemoveWhereaboutsBelief(Npc observer, Guid personId)
    {
        var subject = BeliefSubject(personId);
        observer.Beliefs.RemoveAll(belief =>
            belief.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase));
    }

    private static string BeliefSubject(Guid personId) =>
        $"Crew whereabouts:{personId:N}";

    private static string FirstName(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? name;

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
