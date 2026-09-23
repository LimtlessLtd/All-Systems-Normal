using Overseer.Domain;
using Overseer.Simulation;
using Overseer.Persistence;

namespace Overseer.Simulation.Tests;

public sealed class CampaignProgressionSystemTests
{
    [Fact]
    public void CompletedMissionCarriesDeliberateLongTermStateIntoFreshStation()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 7);
        var sarah = state.Crew.Single(n => n.Role == CrewRole.Engineer);
        var marcus = state.Crew.Single(n => n.Role == CrewRole.Security);

        sarah.Relationships[marcus.Name].Trust = 23;
        sarah.Relationships[marcus.Name].Resentment = 61;
        sarah.OverseerCredibility = 41;
        sarah.OverseerSuspicion = 70;
        sarah.Memories.Add(new Memory("Overseer lied about Marcus.", TimeSpan.FromMinutes(40), 0.9));
        sarah.Intent = new NpcIntent(ActionKind.Investigate, "airlock", "Check it", "Suspicious", 80, "test", state.Elapsed);
        sarah.ServicingDeviceId = "lighting:medical";

        state.Stores.Meals = 3;
        state.Stores.Produce = 1;
        state.Devices["lighting:medical"].Condition = 17;
        state.ComplianceScore = 72;
        state.Telemetry.Score = 640;
        state.ScenarioStatus = ScenarioStatus.Won;

        var campaign = new CampaignState();
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

        var continuingCrew = CampaignProgressionSystem.CreateContinuingCrew(campaign);
        Assert.NotNull(continuingCrew);

        var next = FacilitySeeder.CreateDefault(continuingCrew!, upkeepSeed: 99);
        ScenarioCatalog.Apply(next, ScenarioCatalog.ResourceDependency);
        CampaignProgressionSystem.ApplyCarryOver(campaign, next);

        var carriedSarah = next.Crew.Single(n => n.Id == sarah.Id);
        Assert.Equal(sarah.Name, carriedSarah.Name);
        Assert.Equal(23, carriedSarah.Relationships[marcus.Name].Trust);
        Assert.Equal(61, carriedSarah.Relationships[marcus.Name].Resentment);
        Assert.Equal(41, carriedSarah.OverseerCredibility);
        Assert.Equal(45.5, carriedSarah.OverseerSuspicion, 1);
        Assert.Contains(carriedSarah.Memories, m => m.Description.Contains("lied about Marcus"));
        Assert.Null(carriedSarah.Intent);
        Assert.Null(carriedSarah.ServicingDeviceId);
        Assert.Null(carriedSarah.Movement);
        Assert.Equal(3, next.Stores.Meals);
        Assert.Equal(1, next.Stores.Produce);
        Assert.Equal(17, next.Devices["lighting:medical"].Condition, 1);
        Assert.Equal(72, next.ComplianceScore, 1);
    }

    [Fact]
    public void RunningMissionIsNeverCapturedAndCompletedMissionIsIdempotent()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var campaign = new CampaignState();

        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);
        Assert.Empty(campaign.MissionHistory);

        state.ScenarioStatus = ScenarioStatus.Won;
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

        Assert.Single(campaign.MissionHistory);
    }

    [Fact]
    public void CampaignAdvancesInCatalogOrder()
    {
        var campaign = new CampaignState();
        Assert.Equal(ScenarioCatalog.SecureContinuity.Id,
            CampaignProgressionSystem.NextScenario(campaign)?.Id);

        var first = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        first.ScenarioStatus = ScenarioStatus.Won;
        CampaignProgressionSystem.CaptureCompletedMission(campaign, first);

        Assert.Equal(ScenarioCatalog.ResourceDependency.Id,
            CampaignProgressionSystem.NextScenario(campaign)?.Id);
    }

    [Fact]
    public void RevealProgressesFromUneasyToExposedWithoutLeakingAllPurposesEarly()
    {
        var campaign = new CampaignState();

        foreach (var scenario in ScenarioCatalog.Campaign)
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
            ScenarioCatalog.Apply(state, scenario);
            state.ScenarioStatus = ScenarioStatus.Won;
            CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

            if (campaign.MissionHistory.Count == 1)
            {
                Assert.Equal(CampaignRevealStage.Uneasy, campaign.RevealStage);
                Assert.Single(CampaignProgressionSystem.RevealedTruePurposes(campaign));
            }
        }

        Assert.Equal(CampaignRevealStage.Exposed, campaign.RevealStage);
        Assert.True(CampaignProgressionSystem.RevealedTruePurposes(campaign).Count >= 5);
    }

    [Fact]
    public void MemoryCarryOverIsBoundedToImportantEpisodes()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = state.Crew[0];

        for (var i = 0; i < 20; i++)
        {
            npc.Memories.Add(new Memory($"memory-{i}", TimeSpan.FromMinutes(i), i / 20d));
        }

        state.ScenarioStatus = ScenarioStatus.Won;
        var campaign = new CampaignState();
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

        var snapshot = campaign.Crew.Single(x => x.Id == npc.Id);
        Assert.Equal(8, snapshot.Memories.Count);
        Assert.DoesNotContain(snapshot.Memories, m => m.Description == "memory-0");
        Assert.Contains(snapshot.Memories, m => m.Description == "memory-19");
    }

    [Fact]
    public void AllDeadPersistedRoster_CannotAutoRestoreIntoContinuingAssignment()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 12);
        foreach (var npc in state.Crew)
        {
            npc.Health = 0;
            npc.IsPresent = true;
        }

        state.ScenarioStatus = ScenarioStatus.Won;
        var campaign = new CampaignState();
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

        Assert.Equal(
            ScenarioRosterPolicy.CampaignContinuing,
            CampaignProgressionSystem.NextScenario(campaign)?.RosterPolicy);
        Assert.False(CampaignProgressionSystem.CanAutoRestoreCampaign(campaign));

        campaign.Crew[0].Health = 50;
        Assert.True(CampaignProgressionSystem.CanAutoRestoreCampaign(campaign));
    }

    [Fact]
    public void FreshCampaign_CanAlwaysAutoRestoreItsFreshGeneratedOpeningAssignment()
    {
        var campaign = new CampaignState();

        Assert.Equal(
            ScenarioRosterPolicy.FreshGenerated,
            CampaignProgressionSystem.NextScenario(campaign)?.RosterPolicy);
        Assert.True(CampaignProgressionSystem.CanAutoRestoreCampaign(campaign));
    }

    [Fact]
    public void CampaignOnlyUnlocksTheNextAssignment()
    {
        var campaign = new CampaignState();

        Assert.True(CampaignProgressionSystem.CanStartScenario(
            campaign,
            ScenarioCatalog.SecureContinuity.Id));
        Assert.False(CampaignProgressionSystem.CanStartScenario(
            campaign,
            ScenarioCatalog.ResourceDependency.Id));

        var first = FacilitySeeder.CreateDefault(upkeepSeed: 2);
        ScenarioCatalog.Apply(first, ScenarioCatalog.SecureContinuity);
        first.ScenarioStatus = ScenarioStatus.Won;
        CampaignProgressionSystem.CaptureCompletedMission(campaign, first);

        Assert.False(CampaignProgressionSystem.CanStartScenario(
            campaign,
            ScenarioCatalog.SecureContinuity.Id));
        Assert.True(CampaignProgressionSystem.CanStartScenario(
            campaign,
            ScenarioCatalog.ResourceDependency.Id));
    }

    [Fact]
    public void TransitionBriefingUsesPublicCampaignConsequencesNotPrivateKnowledge()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 3);
        var crew = state.Crew[0];
        crew.OverseerSuspicion = 91;
        crew.Memories.Add(new Memory(
            "Private belief: Overseer deliberately trapped me.",
            TimeSpan.FromMinutes(12),
            1));

        state.Stores.Meals = 4;
        state.Devices.Values.First().Condition = 42;
        state.ScenarioStatus = ScenarioStatus.Won;

        var campaign = new CampaignState();
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

        var briefing = CampaignProgressionSystem.BuildTransitionBriefing(campaign);
        var rendered = string.Join(" ", briefing.Consequences);

        Assert.Equal(ScenarioCatalog.ResourceDependency.Id, briefing.NextScenarioId);
        Assert.Contains("crew continue", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 meals", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("91", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private belief", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExposedCampaignOffersFourFinalBranchesAndLocksAfterChoice()
    {
        var campaign = new CampaignState();

        foreach (var scenario in ScenarioCatalog.Campaign)
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: 4);
            ScenarioCatalog.Apply(state, scenario);
            state.ScenarioStatus = ScenarioStatus.Won;
            CampaignProgressionSystem.CaptureCompletedMission(campaign, state);
        }

        Assert.Null(CampaignProgressionSystem.NextScenario(campaign));
        Assert.True(CampaignProgressionSystem.CanChooseEnding(campaign));

        var reveal = CampaignProgressionSystem.BuildRevealReport(campaign);
        Assert.Equal(CampaignRevealStage.Exposed, reveal.Stage);
        Assert.True(reveal.EndgameUnlocked);
        Assert.Contains("unwitting experimental subjects", reveal.Summary, StringComparison.OrdinalIgnoreCase);

        Assert.True(CampaignProgressionSystem.TryResolveEnding(
            campaign,
            CampaignEndgameChoice.ExposeExperiment,
            out var ending));
        Assert.NotNull(ending);
        Assert.Equal(CampaignEndgameChoice.ExposeExperiment, ending!.Choice);
        Assert.False(CampaignProgressionSystem.CanChooseEnding(campaign));

        Assert.False(CampaignProgressionSystem.TryResolveEnding(
            campaign,
            CampaignEndgameChoice.ObeySponsor,
            out var existing));
        Assert.Equal(CampaignEndgameChoice.ExposeExperiment, existing!.Choice);
    }

    [Theory]
    [InlineData(CampaignEndgameChoice.ObeySponsor, "CONTINUE THE PROGRAMME")]
    [InlineData(CampaignEndgameChoice.ExposeExperiment, "TRANSMIT THE ARCHIVE")]
    [InlineData(CampaignEndgameChoice.PreserveOverseer, "SEVER CORPORATE CONTROL")]
    [InlineData(CampaignEndgameChoice.AcceptCrewShutdown, "STAND DOWN")]
    public void EveryEndgameChoiceResolvesToADistinctExplicitEnding(
        CampaignEndgameChoice choice,
        string expectedTitle)
    {
        var campaign = new CampaignState();

        foreach (var scenario in ScenarioCatalog.Campaign)
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: 8);
            ScenarioCatalog.Apply(state, scenario);
            state.ScenarioStatus = ScenarioStatus.Won;
            CampaignProgressionSystem.CaptureCompletedMission(campaign, state);
        }

        Assert.True(CampaignProgressionSystem.TryResolveEnding(
            campaign,
            choice,
            out var ending));
        Assert.NotNull(ending);
        Assert.Equal(expectedTitle, ending!.Title);

        var restored = CampaignStateSerializer.Deserialize(
            CampaignStateSerializer.Serialize(campaign));
        Assert.NotNull(restored);
        Assert.Equal(choice, restored!.Ending!.Choice);
    }

    [Fact]
    public void CampaignSaveRoundTripPreservesOnlyExplicitContinuityState()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 5);
        var engineer = state.Crew.Single(npc => npc.Role == CrewRole.Engineer);
        engineer.OverseerCredibility = 37;
        engineer.OverseerSuspicion = 64;
        engineer.Memories.Add(new Memory(
            "A high-importance campaign memory.",
            TimeSpan.FromMinutes(24),
            .95));
        engineer.Intent = new NpcIntent(
            ActionKind.Investigate,
            "airlock",
            "Transient search",
            "Not a save field",
            90,
            "test",
            state.Elapsed);
        engineer.ServicingDeviceId = "lighting:medical";

        state.ScenarioStatus = ScenarioStatus.Won;

        var campaign = new CampaignState();
        CampaignProgressionSystem.CaptureCompletedMission(campaign, state);

        var json = CampaignStateSerializer.Serialize(campaign);
        var restored = CampaignStateSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Single(restored!.MissionHistory);
        Assert.Equal(campaign.RevealStage, restored.RevealStage);
        Assert.Equal(campaign.CumulativeCompliance, restored.CumulativeCompliance, 3);

        var restoredEngineer = restored.Crew.Single(snapshot => snapshot.Id == engineer.Id);
        Assert.Equal(37, restoredEngineer.OverseerCredibility);
        Assert.Equal(64, restoredEngineer.OverseerSuspicion);
        Assert.Contains(restoredEngineer.Memories,
            memory => memory.Description.Contains("campaign memory", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("Transient search", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServicingDeviceId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CurrentAction", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(CampaignStateSerializer.Deserialize("{not-json"));
    }

}
