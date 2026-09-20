using Overseer.Domain;
using Overseer.Simulation;

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
}
