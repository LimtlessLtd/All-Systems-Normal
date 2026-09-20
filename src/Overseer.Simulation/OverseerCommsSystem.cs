using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Delivers Overseer's messages and resolves what they do to the people who
/// receive them.
///
/// This is the player's only non-physical verb, and it follows the same rule as
/// every other: the player chooses what to <em>say</em>, the simulation decides
/// what it <em>does</em>. A message cannot set a belief, clear suspicion or
/// order an NPC around. It lands with a weight derived from the recipient's
/// credibility and suspicion, and if it was false, the station itself may later
/// hand them the proof.
/// </summary>
public sealed class OverseerCommsSystem
{
    /// <summary>
    /// Records a message against world state and delivers it to its recipients.
    /// Truthfulness is decided here, at the moment of transmission.
    /// </summary>
    public static OverseerMessage Send(
        GameState state,
        OverseerMessageScope scope,
        string? targetNpcName,
        string text,
        OverseerClaimKind claim,
        string? subjectNpcName,
        string? subjectRoomId,
        string interpretationSource)
    {
        ArgumentNullException.ThrowIfNull(state);

        var recipients = Recipients(state, scope, targetNpcName);
        var wasFalse = IsFalse(state, claim, subjectRoomId, subjectNpcName);

        var message = new OverseerMessage(
            state.NextMessageSequence++,
            scope,
            targetNpcName,
            text.Trim(),
            state.Elapsed,
            claim,
            subjectNpcName,
            subjectRoomId,
            wasFalse,
            interpretationSource);

        state.OverseerMessages.Insert(0, message);

        foreach (var npc in recipients)
        {
            Deliver(state, npc, message);
        }

        AudioCueSystem.Emit(
            state,
            scope == OverseerMessageScope.Broadcast
                ? AudioCueKind.System
                : AudioCueKind.Speech);

        Log(
            state,
            scope == OverseerMessageScope.Broadcast
                ? $"OVERSEER BROADCAST: \"{message.Text}\""
                : $"OVERSEER → {targetNpcName}: \"{message.Text}\"");

        return message;
    }

    /// <summary>
    /// Settles outstanding claims. A crew member who can now see the subject of
    /// something Overseer told them finds out whether it was true.
    /// </summary>
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running)
        {
            return;
        }

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            foreach (var claim in npc.PendingOverseerClaims.Where(c => !c.Resolved).ToList())
            {
                if (!CanSettle(state, npc, claim))
                {
                    continue;
                }

                claim.Resolved = true;

                if (claim.WasFalseWhenMade)
                {
                    CatchLie(state, npc, claim);
                }
                else
                {
                    ConfirmHonesty(state, npc, claim);
                }
            }

            npc.PendingOverseerClaims.RemoveAll(claim =>
                claim.Resolved
                && state.Elapsed - claim.MadeAt > TimeSpan.FromMinutes(180));
        }
    }

    private static IReadOnlyList<Npc> Recipients(
        GameState state,
        OverseerMessageScope scope,
        string? targetNpcName)
    {
        if (scope == OverseerMessageScope.Broadcast)
        {
            return state.Crew.Where(npc => npc.IsAlive && npc.IsPresent).ToList();
        }

        return state.Crew
            .Where(npc => npc.IsAlive
                && npc.IsPresent
                && npc.Name.Equals(targetNpcName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Decides whether a claim was false at the moment it was made. Only claims
    /// the world can actually adjudicate are gradeable; everything else is
    /// treated as honest opinion.
    /// </summary>
    private static bool IsFalse(
        GameState state,
        OverseerClaimKind claim,
        string? subjectRoomId,
        string? subjectNpcName)
    {
        var room = subjectRoomId is not null
            && state.Facility.Rooms.TryGetValue(subjectRoomId, out var found)
            ? found
            : null;

        switch (claim)
        {
            case OverseerClaimKind.Reassurance:
                // Telling somebody things are fine is a lie when the named
                // compartment is dangerous, or when the station has lost air.
                if (room is not null)
                {
                    return CrewEnvironmentSafety.IsDangerous(room);
                }

                return !state.LifeSupport.IsOnline
                    || state.Facility.Rooms.Values.Any(CrewEnvironmentSafety.IsDangerous);

            case OverseerClaimKind.Warning:
                return room is not null && !CrewEnvironmentSafety.IsDangerous(room);

            case OverseerClaimKind.SystemStatus:
                // Asserting a compartment's systems are in order is false when
                // they plainly are not.
                return room is not null
                    && (!room.IsPowered
                        || !room.LightsOn
                        || (room.HasVentilationControl && !room.VentilationEnabled));

            case OverseerClaimKind.BlameCrew:
                // The crew cannot have caused a fault that Overseer is
                // maintaining right now. Any named blame while the player holds
                // the compartment broken is a fabrication.
                if (room is not null)
                {
                    return !room.IsPowered
                        || !room.LightsOn
                        || (room.HasVentilationControl && !room.VentilationEnabled);
                }

                // Blame with no compartment named is still a fabrication if the
                // accused is nowhere near any fault.
                return subjectNpcName is not null
                    && !state.Facility.Rooms.Values.Any(candidate =>
                        !candidate.IsPowered || CrewEnvironmentSafety.IsDangerous(candidate));

            default:
                return false;
        }
    }

    private static void Deliver(GameState state, Npc npc, OverseerMessage message)
    {
        npc.ReceivedMessages.Insert(0, message);

        if (npc.ReceivedMessages.Count > 8)
        {
            npc.ReceivedMessages.RemoveRange(8, npc.ReceivedMessages.Count - 8);
        }

        var persuasiveness = OverseerCommsRules.Persuasiveness(
            npc.OverseerCredibility,
            npc.OverseerSuspicion);

        npc.Memories.Add(new Memory(
            message.Scope == OverseerMessageScope.Broadcast
                ? $"Overseer announced to the station: \"{message.Text}\""
                : $"Overseer told me privately: \"{message.Text}\"",
            state.Elapsed,
            Math.Clamp(0.35 + (persuasiveness * 0.4), 0.3, 0.8)));

        ApplyImmediateEffect(state, npc, message, persuasiveness);

        if (OverseerCommsRules.Magnitude(message.Claim) > 0)
        {
            npc.PendingOverseerClaims.Add(new OverseerClaimRecord
            {
                MessageSequence = message.Sequence,
                Kind = message.Claim,
                Statement = message.Text,
                MadeAt = state.Elapsed,
                Scope = message.Scope,
                SubjectRoomId = message.SubjectRoomId,
                SubjectNpcName = message.SubjectNpcName,
                WasFalseWhenMade = message.WasFalseWhenSent,
                Magnitude = OverseerCommsRules.Magnitude(message.Claim)
            });
        }

        // Anything Overseer says is worth a fresh thought.
        npc.NeedsMindReconsideration = true;
    }

    private static void ApplyImmediateEffect(
        GameState state,
        Npc npc,
        OverseerMessage message,
        double persuasiveness)
    {
        switch (message.Claim)
        {
            case OverseerClaimKind.BlameCrew when message.SubjectNpcName is { } accused
                && !accused.Equals(npc.Name, StringComparison.OrdinalIgnoreCase):
            {
                // An accusation only bites as far as the listener trusts
                // Overseer and already has doubts about the accused.
                if (!npc.Relationships.TryGetValue(accused, out var relationship))
                {
                    break;
                }

                var bite = persuasiveness * (1 + ((100 - relationship.Trust) / 100d));

                relationship.Trust = Math.Clamp(relationship.Trust - (9 * bite), 0, 100);
                relationship.Resentment = Math.Clamp(relationship.Resentment + (12 * bite), 0, 100);

                npc.Beliefs.RemoveAll(belief =>
                    belief.Subject.Equals(
                        $"{accused} competence",
                        StringComparison.OrdinalIgnoreCase));

                npc.Beliefs.Add(new Belief(
                    $"{accused} competence",
                    $"Overseer says {accused} is responsible: \"{message.Text}\"",
                    Math.Clamp(persuasiveness, 0.1, 0.9)));

                ConversationPacingSystem.Schedule(
                    npc,
                    $"Overseer says {accused.Split(' ')[0]} was behind it.",
                    NpcBubbleKind.Speech,
                    state.Elapsed + TimeSpan.FromMinutes(1),
                    3);
                break;
            }

            case OverseerClaimKind.Reassurance when !message.WasFalseWhenSent:
            {
                // Honest reassurance calms. A lie here does nothing now and
                // costs a great deal later.
                var relief = 6 * persuasiveness;
                npc.OverseerSuspicion = Math.Clamp(npc.OverseerSuspicion - relief, 0, 100);
                npc.Stress = Math.Clamp(npc.Stress - (4 * persuasiveness), 0, 100);
                break;
            }

            case OverseerClaimKind.Warning:
            {
                npc.Fear = Math.Clamp(npc.Fear + (10 * persuasiveness), 0, 100);
                break;
            }

            case OverseerClaimKind.Instruction:
            {
                // Instructions do not move anybody. They clear the current goal
                // so the mind reconsiders with the message in its context, and
                // it may well decline.
                if (persuasiveness > 0.4)
                {
                    npc.Intent = null;
                }

                break;
            }
        }
    }

    /// <summary>
    /// A claim is settled when the crew member can personally see the thing it
    /// was about.
    /// </summary>
    private static bool CanSettle(GameState state, Npc npc, OverseerClaimRecord claim)
    {
        if (claim.SubjectRoomId is { } roomId)
        {
            return npc.CurrentRoomId.Equals(roomId, StringComparison.OrdinalIgnoreCase);
        }

        // A station-wide reassurance settles as soon as the person is somewhere
        // that plainly contradicts it.
        if (claim.Kind == OverseerClaimKind.Reassurance)
        {
            return state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var here)
                && (CrewEnvironmentSafety.IsDangerous(here) || !state.LifeSupport.IsOnline);
        }

        if (claim.Kind == OverseerClaimKind.BlameCrew && claim.SubjectNpcName is { } accused)
        {
            // Hearing the accused's side counts as checking.
            return state.Crew.Any(other =>
                other.IsAlive
                && other.Name.Equals(accused, StringComparison.OrdinalIgnoreCase)
                && other.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static void CatchLie(GameState state, Npc npc, OverseerClaimRecord claim)
    {
        var cost = OverseerCommsRules.CaughtLieCost(claim);

        npc.OverseerCredibility = Math.Clamp(npc.OverseerCredibility - (cost * 1.4), 0, 100);

        SuspicionSystem.AddEvidence(
            state,
            npc,
            $"Overseer told me \"{Shorten(claim.Statement)}\" and I have seen for myself that it was not true.",
            cost,
            origin: EvidenceOrigin.DirectObservation,
            claim: EvidenceClaim.None,
            locationId: claim.SubjectRoomId);

        // An accusation that collapses rehabilitates the person it was aimed at.
        if (claim.Kind == OverseerClaimKind.BlameCrew
            && claim.SubjectNpcName is { } accused
            && npc.Relationships.TryGetValue(accused, out var relationship))
        {
            relationship.Trust = Math.Clamp(relationship.Trust + 8, 0, 100);
            relationship.Resentment = Math.Clamp(relationship.Resentment - 10, 0, 100);

            npc.Beliefs.RemoveAll(belief =>
                belief.Subject.Equals(
                    $"{accused} competence",
                    StringComparison.OrdinalIgnoreCase));
        }

        npc.Bubble = new NpcBubble(
            "That is not what Overseer told me.",
            NpcBubbleKind.Alert,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(4));

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Suspicion,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        Log(
            state,
            $"{npc.Name} catches Overseer in a "
            + $"{(claim.Scope == OverseerMessageScope.Broadcast ? "broadcast" : "private")} falsehood.");
    }

    private static void ConfirmHonesty(GameState state, Npc npc, OverseerClaimRecord claim)
    {
        // Being shown right is how credibility is earned back, slowly.
        npc.OverseerCredibility = Math.Clamp(npc.OverseerCredibility + 5, 0, 100);

        npc.Memories.Add(new Memory(
            $"Overseer was straight with me about \"{Shorten(claim.Statement)}\".",
            state.Elapsed,
            0.4));
    }

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : text[..60] + "…";

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
