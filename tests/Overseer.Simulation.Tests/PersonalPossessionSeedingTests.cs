using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class PersonalPossessionSeedingTests
{
    [Fact]
    public void EveryCrewMember_StartsWithOneOrTwoPossessionsHeldByThemself()
    {
        var crew = SeededCrewRosterGenerator.Generate(4242);
        var state = FacilitySeeder.CreateDefault(crew, stationSeed: 4242);

        foreach (var npc in state.Crew)
        {
            var owned = state.Possessions.Where(p => p.OwnerId == npc.Id).ToList();

            Assert.InRange(owned.Count(p => p.Kind != PossessionKind.Keycard), 1, 2);
            Assert.All(owned, possession =>
            {
                Assert.Equal(npc.Id, possession.CurrentHolderId);
                Assert.Null(possession.HiddenAtRoomId);
                Assert.Null(possession.HiddenAtFixtureLabel);
                Assert.False(possession.IsDestroyed);
                Assert.False(string.IsNullOrWhiteSpace(possession.Name));
            });
        }
    }

    [Fact]
    public void Owner_KnowsAboutTheirOwnPossessionsFromTheStart()
    {
        var crew = SeededCrewRosterGenerator.Generate(1357);
        var state = FacilitySeeder.CreateDefault(crew, stationSeed: 1357);

        foreach (var npc in state.Crew)
        {
            var owned = state.Possessions.Where(p => p.OwnerId == npc.Id);

            Assert.All(owned, possession => Assert.Contains(possession.Id, npc.KnownPossessions));
        }
    }

    [Fact]
    public void NoOneKnowsAboutAnotherCrewMembersPossessionsYet()
    {
        var crew = SeededCrewRosterGenerator.Generate(8642);
        var state = FacilitySeeder.CreateDefault(crew, stationSeed: 8642);

        foreach (var npc in state.Crew)
        {
            var othersPossessions = state.Possessions.Where(p => p.OwnerId != npc.Id);

            Assert.All(othersPossessions, possession => Assert.DoesNotContain(possession.Id, npc.KnownPossessions));
        }
    }

    [Fact]
    public void Seeding_IsDeterministicForTheSameRosterAndSeed()
    {
        var firstCrew = SeededCrewRosterGenerator.Generate(97531);
        var firstState = FacilitySeeder.CreateDefault(firstCrew, stationSeed: 97531);

        var secondCrew = SeededCrewRosterGenerator.Generate(97531);
        var secondState = FacilitySeeder.CreateDefault(secondCrew, stationSeed: 97531);

        foreach (var npc in firstState.Crew)
        {
            var counterpart = secondState.Crew.Single(candidate => candidate.Name == npc.Name);

            var firstNames = firstState.Possessions
                .Where(p => p.OwnerId == npc.Id)
                .Select(p => (p.Kind, p.Name))
                .OrderBy(p => p.Name)
                .ToList();
            var secondNames = secondState.Possessions
                .Where(p => p.OwnerId == counterpart.Id)
                .Select(p => (p.Kind, p.Name))
                .OrderBy(p => p.Name)
                .ToList();

            Assert.Equal(firstNames, secondNames);
        }
    }

    [Fact]
    public void DemoCrew_AlsoGetsSeededPossessions()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 606);

        Assert.NotEmpty(state.Possessions);
        Assert.All(state.Crew, npc => Assert.Contains(state.Possessions, p => p.OwnerId == npc.Id));
    }
}
