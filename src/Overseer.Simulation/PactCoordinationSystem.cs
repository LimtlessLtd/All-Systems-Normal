using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Turns an explicit ProposePact/AcceptPact/FulfillPact/BreakPact intention into
/// the deterministic pact record <see cref="CrewPactSystem"/> owns. Proposing
/// communicates an offer; it does not commit either party until the promisee's
/// own cognition decides to accept it. Fulfilling/breaking is likewise the
/// promisor's own resolution decision, only settled once they choose it.
/// </summary>
public sealed class PactCoordinationSystem
{
    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        ExpireOldProposals(state);

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.ProposePact))
        {
            ProcessProposal(state, npc);
        }

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.AcceptPact))
        {
            ProcessAccept(state, npc);
        }

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.FulfillPact))
        {
            ProcessSettle(state, npc, fulfill: true);
        }

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.BreakPact))
        {
            ProcessSettle(state, npc, fulfill: false);
        }
    }

    private static void ProcessProposal(GameState state, Npc proposer)
    {
        var target = state.Crew.FirstOrDefault(candidate =>
            candidate.IsAlive
            && candidate.IsPresent
            && candidate.Id != proposer.Id
            && candidate.Name.Equals(
                proposer.CurrentAction.TargetId,
                StringComparison.OrdinalIgnoreCase)
            && candidate.CurrentRoomId.Equals(
                proposer.CurrentRoomId,
                StringComparison.OrdinalIgnoreCase));

        var promiseText = proposer.CurrentAction.Reason;

        if (target is null || string.IsNullOrWhiteSpace(promiseText))
        {
            ClearAction(proposer, "There is no one here to make that promise to.");
            return;
        }

        target.PendingPactProposal = new PactProposal(
            proposer.Id,
            proposer.Name,
            CrewPactKind.Other,
            promiseText,
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);
        target.NeedsMindReconsideration = true;

        target.Memories.Add(new Memory(
            $"{proposer.Name} proposed: {promiseText}",
            state.Elapsed,
            0.6));
        proposer.Memories.Add(new Memory(
            $"I proposed to {target.Name}: {promiseText}",
            state.Elapsed,
            0.55));

        Log(state, $"{proposer.Name} proposes a pact to {target.Name}: {promiseText}");

        ClearAction(proposer, $"Waiting to hear whether {target.Name} agrees.");
    }

    private static void ProcessAccept(GameState state, Npc npc)
    {
        var proposal = npc.PendingPactProposal;
        if (proposal is null
            || !proposal.FromNpcName.Equals(
                npc.CurrentAction.TargetId,
                StringComparison.OrdinalIgnoreCase))
        {
            ClearAction(npc, "There is no matching pact proposal to accept.");
            return;
        }

        npc.PendingPactProposal = null;

        if (!CrewPactSystem.TryCreate(
                state,
                proposal.FromNpcId,
                npc.Id,
                proposal.Kind,
                proposal.PromiseText,
                proposal.TriggerAt,
                proposal.Deadline,
                out _,
                out var reason))
        {
            ClearAction(npc, reason);
            return;
        }

        npc.NeedsMindReconsideration = true;
        ClearAction(npc, $"Agreed to {proposal.FromNpcName}'s proposal.");
    }

    private static void ProcessSettle(GameState state, Npc npc, bool fulfill)
    {
        var pactId = npc.CurrentAction.TargetId;
        if (string.IsNullOrWhiteSpace(pactId))
        {
            ClearAction(npc, "There is no specific promise to settle.");
            return;
        }

        var pact = state.CrewPacts.FirstOrDefault(candidate =>
            candidate.Id.Equals(pactId, StringComparison.OrdinalIgnoreCase));
        if (pact is null || pact.Status != CrewPactStatus.Active || pact.PromisorId != npc.Id)
        {
            ClearAction(npc, "There is no matching active promise of their own to settle.");
            return;
        }

        var settlementNote = npc.CurrentAction.Reason;
        var settled = fulfill
            ? CrewPactSystem.TryFulfill(state, pactId, settlementNote, out var reason)
            : CrewPactSystem.TryBreak(state, pactId, settlementNote, out reason);

        if (settled)
        {
            var promisee = state.Crew.FirstOrDefault(candidate => candidate.Id == pact.PromiseeId);
            NotifyWitnesses(state, npc, promisee, pact);
        }

        npc.NeedsMindReconsideration = true;
        ClearAction(npc, reason);
    }

    /// <summary>
    /// Crew who happen to be with the promisor when they settle a pact pick up
    /// a memory of it too, so a fulfilled or broken promise can become gossip
    /// even for people who were never party to it, via the existing
    /// News-retelling path in <see cref="ConversationTopicSystem"/>. Nothing is
    /// omniscient: perception decides whether a witness can identify the
    /// promisor, exactly as <see cref="SocialSimulationSystem"/> does for a
    /// witnessed fight.
    /// </summary>
    private static void NotifyWitnesses(GameState state, Npc promisor, Npc? promisee, CrewPact pact)
    {
        var fulfilled = pact.Status == CrewPactStatus.Fulfilled;
        var promiseeName = promisee?.Name ?? "someone";

        foreach (var witness in state.Crew.Where(candidate =>
                     candidate.IsAlive
                     && candidate.IsPresent
                     && candidate.Id != promisor.Id
                     && (promisee is null || candidate.Id != promisee.Id)
                     && candidate.CurrentRoomId.Equals(promisor.CurrentRoomId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!PerceptionSystem.CanMakeOut(state, witness, promisor))
            {
                witness.Memories.Add(new Memory(
                    $"Overheard someone {(fulfilled ? "keep" : "break")} a promise to {promiseeName}: {pact.PromiseText}",
                    state.Elapsed,
                    0.5));
                continue;
            }

            witness.Memories.Add(new Memory(
                fulfilled
                    ? $"Witnessed {promisor.Name} keep their promise to {promiseeName}: {pact.PromiseText}"
                    : $"Witnessed {promisor.Name} break their promise to {promiseeName}: {pact.PromiseText}",
                state.Elapsed,
                0.6,
                MoralActorName: promisor.Name));
        }
    }

    private static void ExpireOldProposals(GameState state)
    {
        foreach (var npc in state.Crew.Where(npc => npc.PendingPactProposal is not null))
        {
            var proposal = npc.PendingPactProposal!;
            if (state.Elapsed - proposal.OfferedAt <= TimeSpan.FromMinutes(15))
                continue;

            npc.PendingPactProposal = null;
        }
    }

    private static void ClearAction(Npc npc, string reason)
    {
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
        npc.RoutineUntil = TimeSpan.Zero;
        npc.Intent = null;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
