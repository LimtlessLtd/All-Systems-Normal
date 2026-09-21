using Overseer.Domain;
using Overseer.Persistence;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ScenarioRosterPolicyTests
{
    [Fact]
    public void CampaignCatalog_ExplicitlyStartsFreshThenContinuesCrew()
    {
        Assert.Equal(
            ScenarioRosterPolicy.FreshGenerated,
            ScenarioCatalog.SecureContinuity.RosterPolicy);

        Assert.All(
            ScenarioCatalog.Campaign.Skip(1),
            scenario => Assert.Equal(
                ScenarioRosterPolicy.CampaignContinuing,
                scenario.RosterPolicy));
    }

    [Fact]
    public void SeededBrowserRoster_SameSeedRepeatsIdentitySkillsAndTraits()
    {
        var first = SeededCrewRosterGenerator.Generate(481516);
        var second = SeededCrewRosterGenerator.Generate(481516);

        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Fact]
    public void SeededBrowserRoster_DifferentSeedsProduceMeaningfulVariation()
    {
        var first = SeededCrewRosterGenerator.Generate(1001);
        var second = SeededCrewRosterGenerator.Generate(2002);

        Assert.NotEqual(Snapshot(first), Snapshot(second));
    }

    [Fact]
    public void SeededBrowserRoster_IsCompleteUniqueAndMechanicallyMeaningful()
    {
        var crew = SeededCrewRosterGenerator.Generate(7429);

        Assert.Equal(12, crew.Count);
        Assert.Equal(6, crew.Select(npc => npc.Role).Distinct().Count());
        Assert.Equal(
            12,
            crew.Select(npc => npc.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.All(crew, npc =>
        {
            Assert.Equal("Seeded browser roster", npc.GenerationSource);
            Assert.NotEmpty(npc.Skills);
            Assert.InRange(npc.Traits.Count, 1, 3);
            Assert.All(npc.Traits, trait =>
            {
                Assert.False(string.IsNullOrWhiteSpace(trait.Name));
                Assert.False(string.IsNullOrWhiteSpace(trait.Description));
                Assert.NotEmpty(trait.Effects);
                Assert.All(
                    trait.Effects,
                    effect => Assert.InRange(effect.Modifier, -15, 15));
            });
        });
    }

    [Fact]
    public void FreshPolicyDoesNotReuseCampaignCrewEvenWhenSnapshotExists()
    {
        var campaign = CaptureOpeningMission(110);
        var fresh = CampaignProgressionSystem.CreateCrewForScenario(
            campaign,
            ScenarioCatalog.SecureContinuity);

        Assert.Null(fresh);
    }

    [Fact]
    public void ContinuingPolicyRequiresAndDeterministicallyRebuildsCampaignCrew()
    {
        var campaign = CaptureOpeningMission(220);

        var first = CampaignProgressionSystem.CreateCrewForScenario(
            campaign,
            ScenarioCatalog.ResourceDependency);
        var second = CampaignProgressionSystem.CreateCrewForScenario(
            campaign,
            ScenarioCatalog.ResourceDependency);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(Snapshot(first!), Snapshot(second!));
        Assert.Equal(
            campaign.Crew.Select(snapshot => snapshot.Id).OrderBy(id => id),
            first!.Select(npc => npc.Id).OrderBy(id => id));

        var emptyCampaign = new CampaignState();
        Assert.Throws<InvalidOperationException>(() =>
            CampaignProgressionSystem.CreateCrewForScenario(
                emptyCampaign,
                ScenarioCatalog.ResourceDependency));
    }

    [Fact]
    public void FreshRosterDoesNotReceiveCrewSpecificCarryOverByRole()
    {
        var campaign = CaptureOpeningMission(330);
        var carriedEngineer = campaign.Crew
            .First(snapshot => snapshot.Role == CrewRole.Engineer);
        carriedEngineer.OverseerSuspicion = 94;
        carriedEngineer.OverseerCredibility = 12;

        var freshCrew = SeededCrewRosterGenerator.Generate(331);
        var state = FacilitySeeder.CreateDefault(freshCrew);
        ScenarioCatalog.Apply(state, ScenarioCatalog.SecureContinuity);

        CampaignProgressionSystem.ApplyCarryOver(campaign, state);

        Assert.All(
            state.Crew.Where(npc => npc.Role == CrewRole.Engineer),
            engineer =>
            {
                Assert.Equal(0, engineer.OverseerSuspicion);
                Assert.Equal(70, engineer.OverseerCredibility);
            });
    }

    [Fact]
    public void ContinuingRosterSurvivesCampaignPersistenceRoundTrip()
    {
        var campaign = CaptureOpeningMission(440);
        var expected = CampaignProgressionSystem.CreateCrewForScenario(
            campaign,
            ScenarioCatalog.ResourceDependency);

        var restored = CampaignStateSerializer.Deserialize(
            CampaignStateSerializer.Serialize(campaign));

        Assert.NotNull(restored);

        var actual = CampaignProgressionSystem.CreateCrewForScenario(
            restored!,
            ScenarioCatalog.ResourceDependency);

        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(Snapshot(expected!), Snapshot(actual!));
    }

    [Fact]
    public void RuntimeSourcesUsePolicyGateInsteadOfImplicitFreshFallback()
    {
        var root = FindRepositoryRoot();
        var server = File.ReadAllText(Path.Combine(
            root,
            "src/Overseer.Web/Services/GameSession.cs"));
        var client = File.ReadAllText(Path.Combine(
            root,
            "src/Overseer.Web.Client/Services/GameSession.cs"));

        Assert.Contains("CreateCrewForScenarioAsync", server);
        Assert.Contains(
            "CampaignProgressionSystem.CreateCrewForScenario(Campaign, scenario)",
            server);
        Assert.Contains("_crewGenerator.GenerateAsync(cancellationToken)", server);
        Assert.DoesNotContain(
            "CreateContinuingCrew(Campaign)",
            server);

        Assert.Contains(
            "CampaignProgressionSystem.CreateCrewForScenario(campaign, scenario)",
            client);
        Assert.Contains("SeededCrewRosterGenerator.Generate(rosterSeed)", client);
        Assert.DoesNotContain(
            "CreateContinuingCrew(Campaign)",
            client);
        Assert.DoesNotContain(
            "continuingCrew is null",
            client);
    }

    private static CampaignState CaptureOpeningMission(int seed)
    {
        var crew = SeededCrewRosterGenerator.Generate(seed);
        var state = FacilitySeeder.CreateDefault(crew);
        ScenarioCatalog.Apply(state, ScenarioCatalog.SecureContinuity);

        var engineer = state.Crew
            .Where(npc => npc.Role == CrewRole.Engineer)
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .First();
        engineer.Skills["Engineering"] = 99;
        engineer.Memories.Add(new Memory(
            "Opening assignment continuity memory.",
            TimeSpan.FromMinutes(30),
            .95));
        engineer.OverseerSuspicion = 70;
        state.ScenarioStatus = ScenarioStatus.Won;

        var campaign = new CampaignState();
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);
        return campaign;
    }

    private static string Snapshot(IReadOnlyList<Npc> crew) =>
        string.Join(
            "\n",
            crew.OrderBy(npc => npc.Role)
                .Select(npc =>
                    string.Join(
                        "|",
                        npc.Id,
                        npc.Name,
                        npc.Role,
                        npc.Personality.Empathy,
                        npc.Personality.Temper,
                        npc.Personality.Sociability,
                        npc.Personality.Courage,
                        npc.GenerationSource,
                        string.Join(
                            ",",
                            npc.Skills.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .Select(pair => $"{pair.Key}:{pair.Value}")),
                        string.Join(
                            ",",
                            npc.Traits.Select(trait =>
                                $"{trait.Name}[{string.Join(";", trait.Effects.Select(effect => $"{effect.Kind}:{effect.Modifier}"))}]")))));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Overseer.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Overseer.slnx from test output.");
    }
}
