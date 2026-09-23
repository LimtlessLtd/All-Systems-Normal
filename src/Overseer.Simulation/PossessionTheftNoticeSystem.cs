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

            var other = state.Crew.FirstOrDefault(npc => npc.Id == possession.CurrentHolderId);
            owner.Memories.Add(new Memory(
                possession.IsDestroyed
                    ? other is not null
                        ? $"I noticed {possession.Name} is gone — {other.Name} must have destroyed it."
                        : $"I noticed {possession.Name} is gone."
                    : other is not null
                        ? $"I noticed {possession.Name} is missing from where I hid it — {other.Name} must have taken it."
                        : $"I noticed {possession.Name} is missing from where I hid it.",
                state.Elapsed,
                0.5));
            owner.NeedsMindReconsideration = true;
            possession.OwnerAwareOfCurrentState = true;
        }
    }
}
