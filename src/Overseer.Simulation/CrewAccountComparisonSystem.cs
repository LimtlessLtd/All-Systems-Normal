using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Crew who are in the same compartment compare what Overseer has been telling
/// each of them.
///
/// Without this, a lie is only ever caught by the person who happens to walk
/// into the room that disproves it, which makes broadcasting safer than it
/// should be and makes telling different people different things free. Two
/// things can now happen over a shared table:
///
/// <list type="bullet">
/// <item><b>Inconsistency.</b> Overseer told these two people incompatible
/// things about the same compartment. Neither of them has to have been there —
/// the statements refute each other.</item>
/// <item><b>Corroboration.</b> One of them already went and looked. They can
/// settle the matter for the other, who never has to make the trip.</item>
/// </list>
///
/// Both are gated on trust and on people actually being in the same room. A
/// crew member who does not believe their colleague learns nothing.
/// </summary>
public sealed class CrewAccountComparisonSystem
{
    /// <summary>Minimum trust before somebody will take a colleague's account seriously.</summary>
    private const double TrustFloor = 35;

    /// <summary>Comparisons resolved per compartment per tick, to keep the log readable.</summary>
    private const int ComparisonsPerRoomPerTick = 1;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running)
        {
            return;
        }

        var groups = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.PendingOverseerClaims.Count > 0)
            .GroupBy(npc => npc.CurrentRoomId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var present = group
                .OrderBy(npc => npc.Name, StringComparer.Ordinal)
                .ToList();

            if (present.Count < 2)
            {
                continue;
            }

            var resolved = 0;

            for (var i = 0; i < present.Count && resolved < ComparisonsPerRoomPerTick; i++)
            {
                for (var j = i + 1; j < present.Count && resolved < ComparisonsPerRoomPerTick; j++)
                {
                    if (Compare(state, present[i], present[j]))
                    {
                        resolved++;
                    }
                }
            }
        }
    }

    private static bool Compare(GameState state, Npc first, Npc second)
    {
        if (!Trusts(first, second) || !Trusts(second, first))
        {
            return false;
        }

        return TryCatchInconsistency(state, first, second)
            || TryCorroborate(state, first, second)
            || TryCorroborate(state, second, first);
    }

    private static bool Trusts(Npc npc, Npc other) =>
        (npc.Relationships.TryGetValue(other.Name, out var relationship)
            ? relationship.Trust
            : 50) >= TrustFloor;

    /// <summary>
    /// Overseer told these two people opposite things about the same
    /// compartment. Neither needs to have been there: the accounts refute each
    /// other on their own.
    /// </summary>
    private static bool TryCatchInconsistency(GameState state, Npc first, Npc second)
    {
        foreach (var mine in first.PendingOverseerClaims.Where(c => !c.Resolved))
        {
            foreach (var theirs in second.PendingOverseerClaims.Where(c => !c.Resolved))
            {
                if (!OverseerCommsRules.Contradict(mine, theirs))
                {
                    continue;
                }

                var key = PairKey(mine, theirs);

                // Both sides are marked even if only one had it on record, so a
                // settled conversation is never replayed from either end.
                var alreadyDiscussed = !first.ComparedAccounts.Add(key);
                alreadyDiscussed |= !second.ComparedAccounts.Add(key);

                if (alreadyDiscussed)
                {
                    continue;
                }

                var cost = OverseerCommsRules.InconsistencyCost(mine, theirs);
                var room = RoomName(state, mine.SubjectRoomId);

                mine.Resolved = true;
                theirs.Resolved = true;

                foreach (var npc in new[] { first, second })
                {
                    npc.OverseerCredibility = Math.Clamp(
                        npc.OverseerCredibility - (cost * 1.5),
                        0,
                        100);

                    SuspicionSystem.AddEvidence(
                        state,
                        npc,
                        $"Overseer gave {first.Name} and {second.Name} opposite accounts of {room}.",
                        cost,
                        origin: EvidenceOrigin.DirectObservation,
                        locationId: mine.SubjectRoomId,
                        evidenceId: $"inconsistency:{key}:{npc.Id:N}");
                }

                // Comparing notes draws people together even as it turns them
                // against Overseer.
                Reinforce(first, second);
                Reinforce(second, first);

                ConversationPacingSystem.Schedule(
                    first,
                    $"That is not what Overseer told me about {room}.",
                    NpcBubbleKind.Speech,
                    state.Elapsed + TimeSpan.FromMinutes(1),
                    3);

                ConversationPacingSystem.Schedule(
                    second,
                    "Then one of us is being handled.",
                    NpcBubbleKind.Alert,
                    state.Elapsed + TimeSpan.FromMinutes(2),
                    3);

                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Suspicion,
                    first.Id.ToString(),
                    first.CurrentRoomId);

                Log(
                    state,
                    $"{first.Name} and {second.Name} compare accounts and find "
                    + $"Overseer's story about {room} does not hold.");

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The witness has already settled this exact statement for themselves.
    /// They pass on what they found, so the listener does not have to go and
    /// look. This is what makes a broadcast lie genuinely dangerous: it only
    /// takes one person to check.
    /// </summary>
    private static bool TryCorroborate(GameState state, Npc witness, Npc listener)
    {
        foreach (var settled in witness.PendingOverseerClaims.Where(c => c.Resolved))
        {
            var open = listener.PendingOverseerClaims.FirstOrDefault(c =>
                !c.Resolved && c.MessageSequence == settled.MessageSequence);

            if (open is null)
            {
                continue;
            }

            var key = $"corroborate:{settled.MessageSequence}:{witness.Id:N}";

            if (!listener.ComparedAccounts.Add(key))
            {
                continue;
            }

            open.Resolved = true;
            var room = RoomName(state, open.SubjectRoomId);

            if (settled.WasFalseWhenMade)
            {
                // Second-hand, so it lands softer than seeing it yourself, but
                // it lands.
                var cost = OverseerCommsRules.CaughtLieCost(open) * 0.7;

                listener.OverseerCredibility = Math.Clamp(
                    listener.OverseerCredibility - (cost * 1.2),
                    0,
                    100);

                SuspicionSystem.AddEvidence(
                    state,
                    listener,
                    $"{witness.Name} checked {room} personally. Overseer's account was false.",
                    cost,
                    source: witness.Name,
                    origin: EvidenceOrigin.Testimony,
                    locationId: open.SubjectRoomId,
                    evidenceId: $"corroborated:{open.MessageSequence}:{listener.Id:N}",
                    sourceEvidenceId: $"corroborated:{open.MessageSequence}");

                ConversationPacingSystem.Schedule(
                    listener,
                    $"You went and looked? And Overseer said the opposite?",
                    NpcBubbleKind.Speech,
                    state.Elapsed + TimeSpan.FromMinutes(1),
                    3);

                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Suspicion,
                    listener.Id.ToString(),
                    listener.CurrentRoomId);

                Log(
                    state,
                    $"{witness.Name} tells {listener.Name} that Overseer's account of {room} was false.");
            }
            else
            {
                // Being independently confirmed honest is how Overseer earns
                // back the benefit of the doubt without saying anything.
                listener.OverseerCredibility = Math.Clamp(
                    listener.OverseerCredibility + 4,
                    0,
                    100);

                listener.Memories.Add(new Memory(
                    $"{witness.Name} checked {room}. Overseer had it right.",
                    state.Elapsed,
                    0.35));
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// People who discover they were told different stories trust each other a
    /// little more, and the station a little less.
    /// </summary>
    private static void Reinforce(Npc npc, Npc other)
    {
        if (!npc.Relationships.TryGetValue(other.Name, out var relationship))
        {
            return;
        }

        relationship.Trust = Math.Clamp(relationship.Trust + 5, 0, 100);
        relationship.Conversations++;
    }

    private static string PairKey(OverseerClaimRecord first, OverseerClaimRecord second)
    {
        var low = Math.Min(first.MessageSequence, second.MessageSequence);
        var high = Math.Max(first.MessageSequence, second.MessageSequence);
        return $"{low}:{high}";
    }

    private static string RoomName(GameState state, string? roomId) =>
        roomId is not null && state.Facility.Rooms.TryGetValue(roomId, out var room)
            ? room.Name
            : "the station";

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
