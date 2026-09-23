using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class SocialClusterSystemTests
{
    [Fact]
    public void MutualCloseFriends_ShareACliqueId_ThirdNeutralNpcStaysUngrouped()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 900001);
        var (first, second, third) = ThreeDistinctCrew(state);
        SetAffinity(first, second, 70, 70);
        SetAffinity(first, third, 50, 50);
        SetAffinity(second, third, 50, 50);

        new SocialClusterSystem().Tick(state);

        Assert.NotNull(first.CliqueId);
        Assert.Equal(first.CliqueId, second.CliqueId);
        Assert.Null(third.CliqueId);
    }

    [Fact]
    public void FriendshipIsTransitive_AcrossAChainOfMutualPairs()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 900002);
        var (a, b, c) = ThreeDistinctCrew(state);
        SetAffinity(a, b, 70, 70);
        SetAffinity(b, c, 70, 70);
        SetAffinity(a, c, 40, 40); // Never directly linked, only via b.

        new SocialClusterSystem().Tick(state);

        Assert.NotNull(a.CliqueId);
        Assert.Equal(a.CliqueId, b.CliqueId);
        Assert.Equal(b.CliqueId, c.CliqueId);
    }

    [Fact]
    public void OneSidedAffinity_DoesNotFormAFriendshipEdge()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 900003);
        var (first, second, _) = ThreeDistinctCrew(state);
        first.Relationships[second.Name].Affinity = 90;
        second.Relationships[first.Name].Affinity = 40;

        new SocialClusterSystem().Tick(state);

        Assert.Null(first.CliqueId);
        Assert.Null(second.CliqueId);
    }

    [Fact]
    public void AbsentCrewMember_IsExcludedFromClusteringEvenWithQualifyingAffinity()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 900004);
        var (first, second, _) = ThreeDistinctCrew(state);
        SetAffinity(first, second, 80, 80);
        second.IsPresent = false;

        new SocialClusterSystem().Tick(state);

        Assert.Null(first.CliqueId);
        Assert.Null(second.CliqueId);
    }

    [Fact]
    public void RecomputingAfterAffinityDrops_DissolvesTheClique()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 900005);
        var (first, second, _) = ThreeDistinctCrew(state);
        SetAffinity(first, second, 80, 80);
        var system = new SocialClusterSystem();
        system.Tick(state);
        Assert.NotNull(first.CliqueId);

        SetAffinity(first, second, 50, 50);
        system.Tick(state);

        Assert.Null(first.CliqueId);
        Assert.Null(second.CliqueId);
    }

    private static (Npc First, Npc Second, Npc Third) ThreeDistinctCrew(GameState state)
    {
        // FacilitySeeder randomly seeds ~12% of pairs as pre-existing close
        // bonds, which would otherwise incidentally link one of these three to
        // some other crew member and confuse a test about exactly these three.
        // Flatten every relationship to neutral first so only the affinities a
        // test explicitly sets can form an edge.
        foreach (var npc in state.Crew)
        foreach (var relationship in npc.Relationships.Values)
            relationship.Affinity = 50;

        var crew = state.Crew.Where(npc => npc.IsAlive && npc.IsPresent).Take(3).ToList();
        Assert.True(crew.Count >= 3, "Default facility seed must produce at least 3 crew for this test.");
        return (crew[0], crew[1], crew[2]);
    }

    private static void SetAffinity(Npc a, Npc b, double aToB, double bToA)
    {
        a.Relationships[b.Name].Affinity = aToB;
        b.Relationships[a.Name].Affinity = bToA;
    }
}
