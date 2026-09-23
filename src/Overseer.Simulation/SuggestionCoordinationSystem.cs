using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Turns an explicit Suggest intention into a perceivable
/// <see cref="Npc.PendingSuggestion"/> on the target (owner idea #6:
/// emergent leadership via trust — "someone repeatedly fixing problems gets
/// listened to ... a high-trust NPC might say 'everyone get to Medical' and
/// other NPCs independently decide whether to comply"). No new leader role
/// and no compliance mechanic: this only places the suggestion in the
/// target's awareness; <see cref="NpcPromptBuilder"/> exposes it alongside
/// the target's own <see cref="Relationship.Trust"/> in the suggester, and
/// the target's cognition independently decides whether to act on it.
/// </summary>
public sealed class SuggestionCoordinationSystem
{
    private static readonly TimeSpan ExpiryWindow = TimeSpan.FromMinutes(10);

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        ExpireOldSuggestions(state);

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.Suggest))
        {
            ProcessSuggestion(state, npc);
        }
    }

    private static void ProcessSuggestion(GameState state, Npc proposer)
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

        var suggestionText = proposer.CurrentAction.Reason;

        if (target is null || string.IsNullOrWhiteSpace(suggestionText))
        {
            ClearAction(proposer, "There is no one here to suggest that to.");
            return;
        }

        target.PendingSuggestion = new NpcSuggestion(proposer.Name, suggestionText, state.Elapsed);
        target.NeedsMindReconsideration = true;

        target.Memories.Add(new Memory(
            $"{proposer.Name} suggested: {suggestionText}",
            state.Elapsed,
            0.5));
        proposer.Memories.Add(new Memory(
            $"I suggested to {target.Name}: {suggestionText}",
            state.Elapsed,
            0.45));

        NotifyBystanders(state, proposer, target, suggestionText);

        Log(state, $"{proposer.Name} suggests to {target.Name}: {suggestionText}");

        ClearAction(proposer, $"Suggested it to {target.Name}.");
    }

    /// <summary>
    /// Crew who happen to be with the suggester pick up a memory of it too,
    /// so a notable suggestion can spread as gossip even for people it
    /// wasn't addressed to, via the existing News-retelling path in
    /// <see cref="ConversationTopicSystem"/>. Nothing is omniscient:
    /// perception decides whether a bystander can identify the suggester,
    /// exactly as <see cref="PactCoordinationSystem"/> does for a witnessed
    /// pact settlement.
    /// </summary>
    private static void NotifyBystanders(GameState state, Npc proposer, Npc target, string suggestionText)
    {
        foreach (var witness in state.Crew.Where(candidate =>
                     candidate.IsAlive
                     && candidate.IsPresent
                     && candidate.Id != proposer.Id
                     && candidate.Id != target.Id
                     && candidate.CurrentRoomId.Equals(proposer.CurrentRoomId, StringComparison.OrdinalIgnoreCase)))
        {
            witness.Memories.Add(PerceptionSystem.CanMakeOut(state, witness, proposer)
                ? new Memory(
                    $"Overheard {proposer.Name} suggest to {target.Name}: {suggestionText}",
                    state.Elapsed,
                    0.35)
                : new Memory(
                    $"Overheard someone suggest something to {target.Name}: {suggestionText}",
                    state.Elapsed,
                    0.3));
        }
    }

    private static void ExpireOldSuggestions(GameState state)
    {
        foreach (var npc in state.Crew.Where(npc => npc.PendingSuggestion is not null))
        {
            if (state.Elapsed - npc.PendingSuggestion!.MadeAt <= ExpiryWindow)
                continue;

            npc.PendingSuggestion = null;
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
