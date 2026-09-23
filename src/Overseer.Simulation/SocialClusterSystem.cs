using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #13, slice 1: detects social clusters purely from the existing
/// relationship graph — no scripted "Engineering faction" or similar concept.
/// Two present crew are friendship-linked when their Affinity toward each
/// other is mutually high (the same &gt;= 65 threshold the UI already treats as
/// a "close" relationship in <c>Home.razor</c>, and <see cref="CrewRoutineSystem"/>
/// already reuses for mutual pairing). Cliques are the connected components of
/// that friendship graph, recomputed every tick so they track the relationships
/// as they actually evolve.
///
/// It never decides what an NPC wants. <see cref="ConversationTopicSystem"/>
/// weights in-clique gossip more heavily, and <see cref="SocialSimulationSystem"/>
/// lets a witnessing clique-mate side with their friend in an argument.
/// </summary>
public sealed class SocialClusterSystem
{
    public const double FriendshipAffinityThreshold = 65;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        var members = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent)
            .ToList();

        foreach (var npc in state.Crew)
        {
            npc.CliqueId = null;
        }

        var parent = members.ToDictionary(npc => npc.Id, npc => npc.Id);

        Guid Find(Guid id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }

            return id;
        }

        void Union(Guid a, Guid b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB)
                return;

            // Deterministic regardless of iteration order: the lexicographically
            // smaller id always becomes the root.
            if (string.CompareOrdinal(rootA.ToString(), rootB.ToString()) <= 0)
                parent[rootB] = rootA;
            else
                parent[rootA] = rootB;
        }

        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                if (IsMutualFriendship(members[i], members[j]))
                    Union(members[i].Id, members[j].Id);
            }
        }

        var cliqueRoots = members
            .Select(npc => Find(npc.Id))
            .GroupBy(root => root)
            .Where(group => group.Count() >= 2)
            .Select(group => group.Key)
            .OrderBy(root => root.ToString(), StringComparer.Ordinal)
            .ToList();

        var cliqueIdByRoot = cliqueRoots
            .Select((root, index) => (root, index))
            .ToDictionary(pair => pair.root, pair => pair.index);

        foreach (var npc in members)
        {
            var root = Find(npc.Id);
            if (cliqueIdByRoot.TryGetValue(root, out var cliqueId))
                npc.CliqueId = cliqueId;
        }
    }

    public static bool SharesClique(Npc first, Npc second) =>
        first.CliqueId is { } clique && second.CliqueId == clique;

    private static bool IsMutualFriendship(Npc first, Npc second) =>
        first.Relationships.TryGetValue(second.Name, out var firstToSecond)
        && second.Relationships.TryGetValue(first.Name, out var secondToFirst)
        && firstToSecond.Affinity >= FriendshipAffinityThreshold
        && secondToFirst.Affinity >= FriendshipAffinityThreshold;
}
