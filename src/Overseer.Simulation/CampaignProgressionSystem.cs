using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deliberately transfers only campaign-scale state between missions. Runtime
/// movement, intents, jobs, investigation leads, bubbles and room-local state
/// are never retained.
/// </summary>
public static class CampaignProgressionSystem
{
    private const int MemoriesPerCrew = 8;

    public static void CaptureCompletedMission(CampaignState campaign, GameState state)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(state);

        if (state.Scenario is null || state.ScenarioStatus == ScenarioStatus.Running)
        {
            return;
        }

        if (campaign.HasCompleted(state.Scenario.Id))
        {
            return;
        }

        campaign.MissionHistory.Add(new CampaignMissionResult(
            state.Scenario.Id,
            state.Scenario.Title,
            state.ScenarioStatus,
            state.ComplianceScore,
            state.Telemetry.Score,
            state.Crew.Count(npc => npc.IsAlive && npc.IsPresent),
            state.Elapsed));

        campaign.CumulativeCompliance = campaign.MissionHistory.Average(x => x.ComplianceScore);
        campaign.CurrentScenarioId = state.Scenario.Id;
        CaptureCrew(campaign, state.Crew);
        CaptureStation(campaign, state);
        campaign.RevealStage = RevealFor(campaign.MissionHistory.Count);
    }

    public static IReadOnlyList<Npc>? CreateContinuingCrew(CampaignState campaign)
    {
        if (campaign.Crew.Count == 0)
        {
            return null;
        }

        return campaign.Crew.Select(snapshot =>
        {
            var npc = new Npc
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
                Role = snapshot.Role,
                CurrentRoomId = StartRoom(snapshot.Role),
                Personality = snapshot.Personality,
                GenerationSource = snapshot.GenerationSource,
                Health = snapshot.Health,
                IsPresent = snapshot.IsPresent,
                OverseerCredibility = snapshot.OverseerCredibility,
                // A new assignment creates some distance, but serious distrust
                // does not vanish because the scenery changed.
                OverseerSuspicion = Math.Clamp(snapshot.OverseerSuspicion * 0.65, 0, 100)
            };

            npc.Traits.AddRange(snapshot.Traits);
            foreach (var (skill, value) in snapshot.Skills)
            {
                npc.Skills[skill] = value;
            }

            foreach (var memory in snapshot.Memories)
            {
                npc.Memories.Add(memory);
            }

            return npc;
        }).ToList();
    }

    public static void ApplyCarryOver(CampaignState campaign, GameState state)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(state);

        state.ComplianceScore = campaign.CumulativeCompliance;

        foreach (var npc in state.Crew)
        {
            var snapshot = campaign.Crew.FirstOrDefault(x => x.Id == npc.Id)
                ?? campaign.Crew.FirstOrDefault(x => x.Role == npc.Role);

            if (snapshot is null)
            {
                continue;
            }

            npc.OverseerCredibility = snapshot.OverseerCredibility;
            npc.OverseerSuspicion = Math.Clamp(snapshot.OverseerSuspicion * 0.65, 0, 100);

            foreach (var (otherName, relationship) in snapshot.Relationships)
            {
                if (!npc.Relationships.TryGetValue(otherName, out var live))
                {
                    continue;
                }

                live.Affinity = relationship.Affinity;
                live.Trust = relationship.Trust;
                live.Resentment = relationship.Resentment;
                live.Attraction = relationship.Attraction;
                live.Conversations = relationship.Conversations;
                live.Arguments = relationship.Arguments;
            }
        }

        foreach (var (deviceId, condition) in campaign.DeviceCondition)
        {
            if (state.Devices.TryGetValue(deviceId, out var device))
            {
                device.Condition = Math.Clamp(condition, 0, 100);
            }
        }

        state.Stores.Meals = Math.Max(0, campaign.Meals);
        state.Stores.Produce = Math.Max(0, campaign.Produce);
        state.Stores.Water = Math.Max(0, campaign.Water);
        state.Stores.Nutrients = Math.Max(0, campaign.Nutrients);
    }

    public static ScenarioDefinition? NextScenario(CampaignState campaign)
    {
        foreach (var scenario in ScenarioCatalog.Campaign)
        {
            if (!campaign.HasCompleted(scenario.Id))
            {
                return scenario;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> RevealedTruePurposes(CampaignState campaign)
    {
        var count = campaign.RevealStage switch
        {
            CampaignRevealStage.Classified => 0,
            CampaignRevealStage.Uneasy => 1,
            CampaignRevealStage.Compromised => 3,
            CampaignRevealStage.Exposed => int.MaxValue,
            _ => 0
        };

        return campaign.MissionHistory
            .SelectMany(result => ScenarioCatalog.Find(result.ScenarioId)?.Directives ?? [])
            .Where(directive => !string.IsNullOrWhiteSpace(directive.TruePurpose))
            .Take(count)
            .Select(directive => directive.TruePurpose)
            .ToList();
    }

    private static void CaptureCrew(CampaignState campaign, IReadOnlyList<Npc> crew)
    {
        campaign.Crew.Clear();

        foreach (var npc in crew)
        {
            var snapshot = new CrewContinuitySnapshot
            {
                Id = npc.Id,
                Name = npc.Name,
                Role = npc.Role,
                Personality = npc.Personality,
                GenerationSource = npc.GenerationSource,
                Health = npc.Health,
                IsPresent = npc.IsPresent,
                OverseerCredibility = npc.OverseerCredibility,
                OverseerSuspicion = npc.OverseerSuspicion
            };

            snapshot.Traits.AddRange(npc.Traits);
            foreach (var (skill, value) in npc.Skills)
            {
                snapshot.Skills[skill] = value;
            }

            foreach (var (name, relationship) in npc.Relationships)
            {
                snapshot.Relationships[name] = new RelationshipContinuitySnapshot(
                    relationship.Affinity,
                    relationship.Trust,
                    relationship.Resentment,
                    relationship.Attraction,
                    relationship.Conversations,
                    relationship.Arguments);
            }

            snapshot.Memories.AddRange(
                npc.Memories
                    .OrderByDescending(memory => memory.Importance)
                    .ThenByDescending(memory => memory.OccurredAt)
                    .Take(MemoriesPerCrew));

            campaign.Crew.Add(snapshot);
        }
    }

    private static void CaptureStation(CampaignState campaign, GameState state)
    {
        campaign.DeviceCondition.Clear();
        foreach (var (id, device) in state.Devices)
        {
            campaign.DeviceCondition[id] = device.Condition;
        }

        campaign.Meals = state.Stores.Meals;
        campaign.Produce = state.Stores.Produce;
        campaign.Water = state.Stores.Water;
        campaign.Nutrients = state.Stores.Nutrients;
    }

    private static CampaignRevealStage RevealFor(int completedMissions) => completedMissions switch
    {
        >= 5 => CampaignRevealStage.Exposed,
        >= 3 => CampaignRevealStage.Compromised,
        >= 1 => CampaignRevealStage.Uneasy,
        _ => CampaignRevealStage.Classified
    };

    private static string StartRoom(CrewRole role) => role switch
    {
        CrewRole.Engineer or CrewRole.Technician => "engineering",
        CrewRole.Doctor => "medical",
        CrewRole.Commander => "control",
        CrewRole.Security => "corridor",
        CrewRole.Scientist => "hydroponics",
        _ => "quarters"
    };
}
