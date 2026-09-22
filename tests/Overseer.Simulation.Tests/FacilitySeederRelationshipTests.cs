using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class FacilitySeederRelationshipTests
{
    [Fact]
    public void DemoCrew_StartsWithVariedRelationshipsNotFlatFifty()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 909);

        var pairs = from npc in state.Crew
                    from relationship in npc.Relationships.Values
                    select relationship;

        Assert.Contains(pairs, r => r.Affinity < 40 && r.Trust < 40 && r.Resentment > 0);
        Assert.Contains(pairs, r => r.Affinity > 60 && r.Trust > 60);
    }

    [Fact]
    public void InitialBond_IsSymmetricArchetypeAcrossBothDirectionsOfAPair()
    {
        var crew = SeededCrewRosterGenerator.Generate(2468);
        var state = FacilitySeeder.CreateDefault(crew, stationSeed: 2468);

        foreach (var npc in state.Crew)
        {
            foreach (var other in state.Crew.Where(other => other.Id != npc.Id))
            {
                var forward = npc.Relationships[other.Name];
                var backward = other.Relationships[npc.Name];

                var forwardIsRival = forward.Resentment > 0;
                var backwardIsRival = backward.Resentment > 0;
                Assert.Equal(forwardIsRival, backwardIsRival);

                var forwardIsClose = forward.Affinity > 60 && forward.Trust > 55;
                var backwardIsClose = backward.Affinity > 60 && backward.Trust > 55;
                Assert.Equal(forwardIsClose, backwardIsClose);
            }
        }
    }

    [Fact]
    public void InitialBond_IsDeterministicForTheSameRosterAndSeed()
    {
        var firstCrew = SeededCrewRosterGenerator.Generate(13579);
        var firstState = FacilitySeeder.CreateDefault(firstCrew, stationSeed: 13579);

        var secondCrew = SeededCrewRosterGenerator.Generate(13579);
        var secondState = FacilitySeeder.CreateDefault(secondCrew, stationSeed: 13579);

        foreach (var npc in firstState.Crew)
        {
            var counterpart = secondState.Crew.Single(candidate => candidate.Name == npc.Name);

            foreach (var (name, relationship) in npc.Relationships)
            {
                var counterpartRelationship = counterpart.Relationships[name];
                Assert.Equal(relationship.Affinity, counterpartRelationship.Affinity);
                Assert.Equal(relationship.Trust, counterpartRelationship.Trust);
                Assert.Equal(relationship.Resentment, counterpartRelationship.Resentment);
            }
        }
    }

    [Fact]
    public void InitialBond_NeverProducesOutOfRangeValues()
    {
        var crew = SeededCrewRosterGenerator.Generate(24680);
        var state = FacilitySeeder.CreateDefault(crew, stationSeed: 24680);

        foreach (var npc in state.Crew)
        {
            foreach (var relationship in npc.Relationships.Values)
            {
                Assert.InRange(relationship.Affinity, 0, 100);
                Assert.InRange(relationship.Trust, 0, 100);
                Assert.InRange(relationship.Resentment, 0, 100);
            }
        }
    }
}
