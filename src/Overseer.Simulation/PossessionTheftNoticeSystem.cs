using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #3, slices 4-5: the "surprised realization" moment when an
/// owner's possession was taken from a hidden stash or destroyed while they
/// were absent — the one case nobody directly notifies them of. An owner
/// already knows their own possession's live current state every tick (the
/// YOUR PERSONAL POSSESSIONS prompt block), but that alone never produces a
/// discrete, gossip-able memory of the moment they realized. Fires exactly
/// once per event via <see cref="PersonalPossession.OwnerAwareOfCurrentState"/>.
/// </summary>
public sealed class PossessionTheftNoticeSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var possession in state.Possessions.Where(candidate =>
                     !candidate.OwnerAwareOfCurrentState
                     && (candidate.IsDestroyed
                         || (candidate.CurrentHolderId is not null && candidate.CurrentHolderId != candidate.OwnerId))))
        {
            var owner = state.Crew.FirstOrDefault(npc => npc.Id == possession.OwnerId && npc.IsAlive);
            if (owner is null)
                continue;

            // The owner never omnisciently learns who did this from live
            // global state — only their own prior sighting counts as
            // evidence. If they independently witnessed (or were told of) a
            // sighting that still matches who currently holds it, name that
            // person; otherwise this is exactly the "nobody told them
            // anything" case, so the memory states only that the item is
            // missing/gone.
            var identifiedCulprit =
                owner.KnownPossessions.TryGetValue(possession.Id, out var belief)
                && belief.HolderId is not null
                && belief.HolderId == possession.CurrentHolderId
                    ? state.Crew.FirstOrDefault(npc => npc.Id == belief.HolderId)
                    : null;

            owner.Memories.Add(new Memory(
                possession.IsDestroyed
                    ? identifiedCulprit is not null
                        ? $"I noticed {possession.Name} is gone — {identifiedCulprit.Name} must have destroyed it."
                        : $"I noticed {possession.Name} is gone."
                    : identifiedCulprit is not null
                        ? $"I noticed {possession.Name} is missing from where I hid it — {identifiedCulprit.Name} must have taken it."
                        : $"I noticed {possession.Name} is missing from where I hid it.",
                state.Elapsed,
                0.5));
            owner.NeedsMindReconsideration = true;
            possession.OwnerAwareOfCurrentState = true;
        }
    }
}
