using Overseer.Domain;

namespace Overseer.AI;

public interface IAiDecisionService
{
    Task<NpcIntent> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default);
}
