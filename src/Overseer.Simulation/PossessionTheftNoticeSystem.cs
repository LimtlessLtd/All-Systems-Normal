using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #3, slice 4: the "surprised realization" moment when an
/// owner's possession was taken from a hidden stash while they were absent
/// — the one case nobody directly notifies them of. An owner already knows
/// their own possession's live current holder every tick (the YOUR PERSONAL
/// POSSESSIONS prompt block), but that alone never produces a discrete,
/// gossip-able memory of the moment they realized. Fires exactly once per
/// theft via <see cref="PersonalPossession.OwnerNoticedCurrentHolder"/>.
/// </summary>
public sealed class PossessionTheftNoticeSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var possession in state.Possessions.Where(candidate =>
                     !candidate.IsDestroyed
                     && candidate.CurrentHolderId is not null
                     && candidate.CurrentHolderId != candidate.OwnerId
                     && !candidate.OwnerNoticedCurrentHolder))
        {
            var owner = state.Crew.FirstOrDefault(npc => npc.Id == possession.OwnerId && npc.IsAlive);
            if (owner is null)
                continue;

            var thief = state.Crew.FirstOrDefault(npc => npc.Id == possession.CurrentHolderId);
            owner.Memories.Add(new Memory(
                thief is not null
                    ? $"I noticed {possession.Name} is missing from where I hid it — {thief.Name} must have taken it."
                    : $"I noticed {possession.Name} is missing from where I hid it.",
                state.Elapsed,
                0.5));
            owner.NeedsMindReconsideration = true;
            possession.OwnerNoticedCurrentHolder = true;
        }
    }
}
