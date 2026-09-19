using Overseer.Domain;

namespace Overseer.AI;

public interface IAiDecisionService
{
    Task<NpcAction> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default);
}
