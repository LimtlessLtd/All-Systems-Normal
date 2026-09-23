using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #3: the "surprised realization" when an owner finds their
/// hidden possession is no longer where they left it. Grounded in physical
/// presence: it fires only once the owner is standing in the room their own
/// belief places the stash, and never names a culprit (they saw nobody).
/// Their belief is then cleared, so the prompt shows the item as missing.
/// </summary>
public sealed class PossessionTheftNoticeSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var possession in state.Possessions.Where(candidate => !candidate.OwnerAwareOfCurrentState))
        {
            var owner = state.Crew.FirstOrDefault(npc => npc.Id == possession.OwnerId && npc.IsAlive && npc.IsPresent);
            if (owner is null
                || !owner.KnownPossessions.TryGetValue(possession.Id, out var belief)
                || belief.HiddenAtRoomId is null
                || !belief.HiddenAtRoomId.Equals(owner.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stillThere = !possession.IsDestroyed
                && possession.HiddenAtRoomId is not null
                && possession.HiddenAtRoomId.Equals(belief.HiddenAtRoomId, StringComparison.OrdinalIgnoreCase);
            if (stillThere)
            {
                possession.OwnerAwareOfCurrentState = true;
                continue;
            }

            // A destroyed item stays "unaware" so the owner keeps seeing it as
            // missing; they found it gone, not destroyed.
            if (!possession.IsDestroyed)
                possession.OwnerAwareOfCurrentState = true;
            owner.KnownPossessions.Remove(possession.Id);
            owner.Memories.Add(new Memory(
                $"I noticed {possession.Name} is missing from where I hid it.",
                state.Elapsed,
                0.5));
            owner.NeedsMindReconsideration = true;
        }
    }
}
