using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Governs how crew suspicion of Overseer <em>falls</em>, and where it goes
/// when it does not land on Overseer.
///
/// Suspicion used to be a one-way ratchet: evidence only ever accumulated, so
/// the only viable strategy was never to be seen. That makes a stealth game.
/// This system makes suspicion contestable, which is what makes it a
/// manipulation game:
///
/// <list type="bullet">
/// <item>evidence fades, and hearsay fades faster than what was witnessed;</item>
/// <item>a claim an NPC can personally check is discredited when the station
/// contradicts it — and the crew member who passed it on loses credibility;</item>
/// <item>visibly benign Overseer action, observed first-hand, buys back trust;</item>
/// <item>a failure with a plausible human culprit may be blamed on a
/// crewmate instead of on Overseer.</item>
/// </list>
///
/// Every one of these is grounded in what the NPC can actually perceive. None
/// of them lets the player edit a belief directly.
/// </summary>
public sealed class SuspicionDynamicsSystem
{
    /// <summary>Weight below which a piece of evidence is forgotten entirely.</summary>
    private const double ForgetThreshold = 1.5;

    /// <summary>Fraction of weight a discredited claim retains as lingering doubt.</summary>
    private const double DiscreditedResidue = 0.25;

    private static readonly TimeSpan MisattributionWindow = TimeSpan.FromMinutes(45);

    /// <summary>
    /// How recently a crew member must already hold evidence about a compartment
    /// for a newly noticed fault there to count as the same incident.
    /// </summary>
    private static readonly TimeSpan RecentEvidenceWindow = TimeSpan.FromMinutes(20);

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsSimulationLive)
        {
            return;
        }

        var minutes = delta.TotalMinutes;

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            ObserveLocalFaults(state, npc);
            DecayEvidence(npc, minutes);
            CheckClaimsAgainstReality(state, npc);
            RecomputeSuspicion(state, npc);
        }
    }

    /// <summary>
    /// A fault the crew can plausibly read as either sabotage or bad
    /// maintenance. Anything here must be noticeable from inside the room.
    /// </summary>
    private readonly record struct Fault(
        string Key,
        string Description,
        double Weight,
        EvidenceClaim Claim,
        bool CanBlameACrewmate);

    /// <summary>
    /// Crew notice what is wrong with the compartment they are standing in, and
    /// decide who is responsible. This is the entry point for both suspicion and
    /// misdirection: the same broken heater can become evidence against Overseer
    /// or a grudge against the engineer who was last seen working on it.
    /// </summary>
    private static void ObserveLocalFaults(GameState state, Npc npc)
    {
        if (!state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return;
        }

        foreach (var fault in DetectFaults(state, room))
        {
            var key = $"{room.Id}:{fault.Key}";

            if (!npc.ObservedFaults.Add(key))
            {
                continue;
            }

            // SuspicionSystem.ObservePlayerRoomSystemChange already charges
            // anybody standing in the room when the player broke it. Witnessing
            // the act blames Overseer; only discovering the aftermath is
            // ambiguous enough to be pinned on a colleague. Without this guard a
            // single command would be counted twice against the same person.
            if (npc.OverseerEvidence.Any(evidence =>
                    string.Equals(
                        evidence.LocationId,
                        room.Id,
                        StringComparison.OrdinalIgnoreCase)
                    && state.Elapsed - evidence.ObservedAt <= RecentEvidenceWindow))
            {
                continue;
            }

            if (fault.CanBlameACrewmate
                && TryMisattribute(state, npc, room.Id, fault.Description))
            {
                continue;
            }

            SuspicionSystem.AddEvidence(
                state,
                npc,
                $"I found {fault.Description} in {room.Name} with no announced cause.",
                fault.Weight,
                origin: EvidenceOrigin.Inference,
                claim: fault.Claim,
                locationId: room.Id);
        }

        ForgetClearedFaults(state, npc, room);
    }

    private static IEnumerable<Fault> DetectFaults(GameState state, Room room)
    {
        if (!room.IsPowered)
        {
            yield return new Fault(
                "power",
                "the power dead",
                10,
                EvidenceClaim.PowerCut,
                true);
        }
        else if (!room.LightsOn)
        {
            yield return new Fault(
                "dark",
                "the lights killed",
                5,
                EvidenceClaim.None,
                true);
        }

        if (room.HasVentilationControl && !room.VentilationEnabled)
        {
            yield return new Fault(
                "vent",
                "the air loop shut off",
                8,
                EvidenceClaim.None,
                true);
        }

        if (room.TemperatureC is < 16 or > 28)
        {
            yield return new Fault(
                "temperature",
                room.TemperatureC < 16 ? "the compartment freezing" : "the compartment overheating",
                6,
                EvidenceClaim.None,
                true);
        }

        if (!state.LifeSupport.IsOnline)
        {
            // Nobody blames a colleague for the whole station losing air.
            yield return new Fault(
                "life-support",
                "primary life support stopped",
                16,
                EvidenceClaim.LifeSupportDisabled,
                false);
        }
    }

    /// <summary>
    /// Once a fault is genuinely fixed the NPC stops holding it as current, so a
    /// repeat failure later reads as a fresh incident.
    /// </summary>
    private static void ForgetClearedFaults(GameState state, Npc npc, Room room)
    {
        var live = DetectFaults(state, room)
            .Select(fault => $"{room.Id}:{fault.Key}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        npc.ObservedFaults.RemoveWhere(key =>
            key.StartsWith($"{room.Id}:", StringComparison.OrdinalIgnoreCase)
            && !live.Contains(key));
    }

    /// <summary>
    /// Records something the player did that a present crew member would read as
    /// Overseer helping them. Called from the session when a command visibly
    /// improves a compartment the crew are standing in.
    /// </summary>
    /// <param name="magnitude">Suspicion points of relief before perception checks.</param>
    public static void RecordBenignAct(
        GameState state,
        string roomId,
        string description,
        double magnitude)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsSimulationLive
            || !state.Facility.Rooms.ContainsKey(roomId))
        {
            return;
        }

        var witnesses = state.Crew
            .Where(npc => npc.IsAlive
                && npc.IsPresent
                && npc.CurrentRoomId.Equals(roomId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (witnesses.Count == 0)
        {
            return;
        }

        foreach (var npc in witnesses)
        {
            // A crew member who is already convinced reads a favour as
            // manipulation. Relief is strongest on the merely uneasy.
            var receptiveness = npc.OverseerSuspicion switch
            {
                >= 70 => 0.25,
                >= 45 => 0.6,
                _ => 1.0
            };

            var relief = magnitude * receptiveness;

            if (relief <= 0)
            {
                continue;
            }

            SoftenEvidence(npc, relief);
            npc.OverseerSuspicion = Math.Clamp(npc.OverseerSuspicion - relief, 0, 100);

            npc.Memories.Add(new Memory(
                description,
                state.Elapsed,
                0.3));
        }

        Log(
            state,
            $"{description} Observed by {string.Join(", ", witnesses.Select(w => w.Name))}.");
    }

    /// <summary>
    /// Offers an alternative culprit for a station failure. If the observer
    /// personally saw a plausible crewmate near the fault recently, they may
    /// blame that person instead of Overseer — the seed of a feud the player
    /// never has to admit starting.
    /// </summary>
    /// <returns>True when blame landed on a crew member rather than Overseer.</returns>
    public static bool TryMisattribute(
        GameState state,
        Npc observer,
        string roomId,
        string faultDescription)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observer);

        if (!state.Facility.Rooms.TryGetValue(roomId, out var room))
        {
            return false;
        }

        // Only sightings the observer personally made count. This must never
        // read global crew positions.
        var candidate = observer.LastSeenCrew.Values
            .Where(sighting =>
                sighting.PersonId != observer.Id
                && sighting.RoomId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
                && state.Elapsed - sighting.SeenAt <= MisattributionWindow)
            .OrderByDescending(sighting => sighting.SeenAt)
            .Select(sighting => state.Crew.FirstOrDefault(npc => npc.Id == sighting.PersonId))
            .FirstOrDefault(npc => npc is not null && npc.IsAlive);

        if (candidate is null)
        {
            return false;
        }

        // Blame follows existing feeling. Someone already resented is a far
        // easier suspect than a trusted friend.
        var relationship = observer.Relationships.TryGetValue(candidate.Name, out var existing)
            ? existing
            : null;

        var trust = relationship?.Trust ?? 50;
        var resentment = relationship?.Resentment ?? 0;

        var blameScore =
            (100 - trust) * 0.5
            + resentment * 0.8
            + Math.Clamp(CrewTraitMath.Modifier(observer, TraitEffectKind.SuspicionSensitivity), -20, 20);

        if (blameScore < 45)
        {
            return false;
        }

        if (relationship is not null)
        {
            relationship.Trust = Math.Clamp(relationship.Trust - 6, 0, 100);
            relationship.Resentment = Math.Clamp(relationship.Resentment + 9, 0, 100);
        }

        observer.Beliefs.RemoveAll(belief =>
            belief.Subject.Equals(
                $"{candidate.Name} competence",
                StringComparison.OrdinalIgnoreCase));

        observer.Beliefs.Add(new Belief(
            $"{candidate.Name} competence",
            $"{candidate.Name} is responsible for {faultDescription} in {room.Name}.",
            Math.Clamp(blameScore / 100d, 0.3, 0.9)));

        observer.Memories.Add(new Memory(
            $"{candidate.Name} was the last person I saw in {room.Name} before {faultDescription}.",
            state.Elapsed,
            0.6));

        Log(
            state,
            $"{observer.Name} blames {candidate.Name} for {faultDescription} in {room.Name}.");

        return true;
    }

    /// <summary>
    /// Evidence loses weight with age. What somebody was told fades roughly
    /// twice as fast as what they saw themselves.
    /// </summary>
    private static void DecayEvidence(Npc npc, double minutes)
    {
        foreach (var evidence in npc.OverseerEvidence)
        {
            var rate = evidence.Origin switch
            {
                EvidenceOrigin.Testimony => 0.10,
                EvidenceOrigin.Inference => 0.07,
                _ => 0.045
            };

            // Conviction slows forgetting: a strongly held memory is rehearsed.
            if (evidence.CurrentWeight > 18)
            {
                rate *= 0.6;
            }

            evidence.CurrentWeight = Math.Max(0, evidence.CurrentWeight - (rate * minutes));
        }

        npc.OverseerEvidence.RemoveAll(evidence => evidence.CurrentWeight < ForgetThreshold);
    }

    /// <summary>
    /// Where an NPC can personally observe the subject of a claim they hold,
    /// they find out whether it is still true. A falsified claim is discredited,
    /// and if somebody passed it to them, that person loses credibility.
    /// </summary>
    private static void CheckClaimsAgainstReality(GameState state, Npc npc)
    {
        for (var i = 0; i < npc.OverseerEvidence.Count; i++)
        {
            var evidence = npc.OverseerEvidence[i];

            if (evidence.IsDiscredited
                || evidence.Claim == EvidenceClaim.None
                || !CanVerify(state, npc, evidence))
            {
                continue;
            }

            if (ClaimStillHolds(state, evidence))
            {
                continue;
            }

            npc.OverseerEvidence[i] = evidence with
            {
                IsDiscredited = true,
                CurrentWeight = evidence.CurrentWeight * DiscreditedResidue
            };

            npc.Memories.Add(new Memory(
                $"I checked for myself: {ClaimSummary(state, evidence)} That is not what I was led to believe.",
                state.Elapsed,
                0.55));

            // The payoff. A rumour that fails inspection costs the teller, not
            // Overseer — which is exactly the wedge the player is looking for.
            if (evidence.Origin == EvidenceOrigin.Testimony
                && evidence.SourceNpcName is { } teller
                && npc.Relationships.TryGetValue(teller, out var relationship))
            {
                relationship.Trust = Math.Clamp(relationship.Trust - 11, 0, 100);
                relationship.Resentment = Math.Clamp(relationship.Resentment + 7, 0, 100);

                ConversationPacingSystem.Schedule(
                    npc,
                    $"{teller.Split(' ')[0]}, that is not what I'm looking at.",
                    NpcBubbleKind.Speech,
                    state.Elapsed + TimeSpan.FromMinutes(1),
                    2);

                Log(
                    state,
                    $"{npc.Name} finds {teller}'s account contradicted by the station itself.");
            }
            else
            {
                Log(state, $"{npc.Name} re-examines an assumption about Overseer and doubts it.");
            }
        }
    }

    /// <summary>
    /// An NPC can only test a claim about a compartment they are standing in,
    /// or one they can see the status of from a powered panel where they are.
    /// </summary>
    private static bool CanVerify(GameState state, Npc npc, OverseerEvidence evidence)
    {
        if (evidence.Claim == EvidenceClaim.LifeSupportDisabled)
        {
            return state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var here)
                && here.IsPowered;
        }

        return evidence.LocationId is { } subject
            && npc.CurrentRoomId.Equals(subject, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ClaimStillHolds(GameState state, OverseerEvidence evidence)
    {
        switch (evidence.Claim)
        {
            case EvidenceClaim.LifeSupportDisabled:
                return !state.LifeSupport.IsOnline;

            case EvidenceClaim.PowerCut:
                return evidence.LocationId is { } powerRoom
                    && state.Facility.Rooms.TryGetValue(powerRoom, out var room)
                    && !room.IsPowered;

            case EvidenceClaim.HatchOpened:
                return evidence.LocationId is { } hatchRoom
                    && state.Facility.Rooms.TryGetValue(hatchRoom, out var airlock)
                    && (airlock.ExteriorHatchOpen || airlock.AirlockAlarmActive);

            case EvidenceClaim.AccessRestricted:
                return evidence.LocationId is { } sealedRoom
                    && state.Facility.Doors.Any(door =>
                        (door.RoomAId.Equals(sealedRoom, StringComparison.OrdinalIgnoreCase)
                            || door.RoomBId.Equals(sealedRoom, StringComparison.OrdinalIgnoreCase))
                        && !door.IsPassable);

            default:
                return true;
        }
    }

    private static string ClaimSummary(GameState state, OverseerEvidence evidence)
    {
        var roomName = evidence.LocationId is { } id
            && state.Facility.Rooms.TryGetValue(id, out var room)
                ? room.Name
                : "the compartment";

        return evidence.Claim switch
        {
            EvidenceClaim.AccessRestricted => $"{roomName} is not sealed.",
            EvidenceClaim.PowerCut => $"{roomName} has power.",
            EvidenceClaim.HatchOpened => $"the {roomName} hatch is secure.",
            EvidenceClaim.LifeSupportDisabled => "life support is running.",
            _ => "the station does not match the account."
        };
    }

    /// <summary>
    /// Spreads a relief amount across an NPC's strongest live evidence, so that
    /// reassurance erodes the reasons for suspicion rather than only the number.
    /// </summary>
    private static void SoftenEvidence(Npc npc, double relief)
    {
        var remaining = relief;

        foreach (var evidence in npc.OverseerEvidence
                     .OrderByDescending(e => e.CurrentWeight)
                     .ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            var reduction = Math.Min(evidence.CurrentWeight, remaining * 0.5);
            evidence.CurrentWeight -= reduction;
            remaining -= reduction;
        }

        npc.OverseerEvidence.RemoveAll(evidence => evidence.CurrentWeight < ForgetThreshold);
    }

    /// <summary>
    /// Suspicion is the sum of what an NPC currently believes, not an
    /// independent counter. Recomputing keeps the number honest about the
    /// evidence actually standing behind it.
    /// </summary>
    private static void RecomputeSuspicion(GameState state, Npc npc)
    {
        var total = npc.OverseerEvidence.Sum(evidence => evidence.CurrentWeight);
        npc.OverseerSuspicion = Math.Clamp(total, 0, 100);

        npc.Beliefs.RemoveAll(belief =>
            belief.Subject.Equals("Overseer hostility", StringComparison.OrdinalIgnoreCase));

        var strongest = npc.OverseerEvidence
            .Where(evidence => !evidence.IsDiscredited)
            .OrderByDescending(evidence => evidence.CurrentWeight)
            .FirstOrDefault();

        if (strongest is not null)
        {
            npc.Beliefs.Add(new Belief(
                "Overseer hostility",
                strongest.Description,
                npc.OverseerSuspicion / 100d));
        }
        else if (npc.OverseerEvidence.Count > 0)
        {
            npc.Beliefs.Add(new Belief(
                "Overseer hostility",
                "Something happened, but I no longer trust my account of it.",
                npc.OverseerSuspicion / 100d));
        }
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
