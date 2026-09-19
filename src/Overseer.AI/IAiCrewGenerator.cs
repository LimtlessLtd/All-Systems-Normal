using Overseer.Domain;

namespace Overseer.AI;

public interface IAiCrewGenerator
{
    Task<IReadOnlyList<Npc>> GenerateAsync(
        CancellationToken cancellationToken = default);
}
