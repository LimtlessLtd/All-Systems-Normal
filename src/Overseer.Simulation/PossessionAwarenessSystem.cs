using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Grants observer-specific knowledge of personal possessions (owner idea
/// #3), mirroring <see cref="MedicalEvidenceSystem"/>'s NoticeBlood pattern:
/// a possession someone is visibly holding becomes known to any co-located
/// crew member who can actually perceive the holder. A hidden possession is
/// never noticed this way — only by witnessing the hide/borrow/steal act
/// itself (see <c>ActionResolver.NotifyPossessionWitnesses</c>) or a future
/// search affordance. Never omniscient.
/// </summary>
public sealed class PossessionAwarenessSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var possession in state.Possessions.Where(candidate =>
                     !candidate.IsDestroyed && candidate.CurrentHolderId is not null))
        {
            var holder = state.Crew.FirstOrDefault(npc =>
                npc.Id == possession.CurrentHolderId && npc.IsAlive && npc.IsPresent);

            if (holder is null)
                continue;

            foreach (var observer in state.Crew.Where(npc =>
                         npc.IsAlive
                         && npc.IsPresent
                         && npc.Id != holder.Id
                         && !npc.KnownPossessionIds.Contains(possession.Id)
                         && npc.CurrentRoomId.Equals(holder.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                         && PerceptionSystem.CanMakeOut(state, npc, holder)))
            {
                observer.KnownPossessionIds.Add(possession.Id);
            }
        }
    }
}
