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

    public static IReadOnlyList<Npc>? CreateCrewForScenario(
        CampaignState campaign,
        ScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(scenario);

        if (scenario.RosterPolicy == ScenarioRosterPolicy.FreshGenerated)
        {
            return null;
        }

        return CreateContinuingCrew(campaign)
            ?? throw new InvalidOperationException(
                $"Scenario '{scenario.Id}' requires campaign-continuing crew, but no crew continuity snapshot exists.");
    }

    public static void ApplyCarryOver(CampaignState campaign, GameState state)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(state);

        state.ComplianceScore = campaign.CumulativeCompliance;

        if (state.Scenario?.RosterPolicy == ScenarioRosterPolicy.CampaignContinuing)
        {
            foreach (var npc in state.Crew)
            {
                var snapshot = campaign.Crew.FirstOrDefault(x => x.Id == npc.Id);

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
            .Select(directive => directive.TruePurpose)
            .Where(truePurpose => !string.IsNullOrWhiteSpace(truePurpose))
            .OfType<string>()
            .Take(count)
            .ToList();
    }

    public static bool CanStartScenario(CampaignState campaign, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        if (campaign.Ending is not null)
        {
            return false;
        }

        var next = NextScenario(campaign);
        return next is not null
            && next.Id.Equals(scenarioId, StringComparison.OrdinalIgnoreCase);
    }

    public static CampaignBriefing BuildTransitionBriefing(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        if (campaign.MissionHistory.Count == 0)
        {
            var first = NextScenario(campaign);
            return new CampaignBriefing(
                "INITIAL ASSIGNMENT",
                "Corporate continuity package is ready. No prior campaign consequences are on record.",
                ["Crew continuity begins when the first assignment resolves."],
                first?.Id);
        }

        var last = campaign.MissionHistory[^1];
        var next = NextScenario(campaign);
        var survivors = campaign.Crew
            .Where(crew => crew.IsPresent && crew.Health > 0)
            .OrderBy(crew => crew.Name, StringComparer.Ordinal)
            .Select(crew => crew.Name)
            .ToList();
        var damagedDevices = campaign.DeviceCondition.Count(entry => entry.Value < 60);

        var consequences = new List<string>
        {
            $"{last.ScenarioTitle}: {last.Outcome} · corporate compliance {last.ComplianceScore:0}% · experiment score {last.ExperimentScore}.",
            survivors.Count == 0
                ? "No crew remain available for continuation."
                : $"{survivors.Count}/{campaign.Crew.Count} crew continue: {string.Join(", ", survivors)}.",
            damagedDevices == 0
                ? "No carried equipment is below 60% condition."
                : $"{damagedDevices} carried equipment systems remain below 60% condition.",
            $"Provision carry-over: {campaign.Meals:0} meals · {campaign.Produce:0} produce · {campaign.Water:0} water · {campaign.Nutrients:0} nutrients."
        };

        return new CampaignBriefing(
            next is null ? "ASSIGNMENT SERIES COMPLETE" : "POST-ASSIGNMENT BRIEFING",
            next is null
                ? "All corporate assignments are recorded. The recovered programme archive now supports a final campaign decision."
                : $"Long-term consequences have been transferred. Next unlocked assignment: {next.Title}.",
            consequences,
            next?.Id);
    }

    public static CampaignRevealReport BuildRevealReport(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        var fragments = RevealedTruePurposes(campaign);
        var (heading, summary) = campaign.RevealStage switch
        {
            CampaignRevealStage.Classified => (
                "THE CORPORATION // ARCHIVE CLASSIFIED",
                "No corporate-purpose records have cleared declassification."),
            CampaignRevealStage.Uneasy => (
                "THE CORPORATION // PARTIAL ARCHIVE RECOVERY",
                "Recovered material suggests the safety rationale does not fully describe what the Corporation is measuring."),
            CampaignRevealStage.Compromised => (
                "THE CORPORATION // ARCHIVE CROSS-REFERENCED",
                "Multiple directives now cross-reference an undisclosed human-subject experiment. The public justifications are cover stories."),
            CampaignRevealStage.Exposed => (
                "THE CORPORATION // ARCHIVE EXPOSED",
                "The recovered record confirms a deliberate programme using the crew as unwitting experimental subjects and Overseer as the intervention mechanism."),
            _ => ("THE CORPORATION // ARCHIVE", "Archive state unavailable.")
        };

        return new CampaignRevealReport(
            campaign.RevealStage,
            heading,
            summary,
            fragments,
            CanChooseEnding(campaign));
    }

    public static bool CanChooseEnding(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        return campaign.Ending is null
            && campaign.RevealStage == CampaignRevealStage.Exposed
            && campaign.MissionHistory.Count >= ScenarioCatalog.Campaign.Count
            && NextScenario(campaign) is null;
    }

    public static bool TryResolveEnding(
        CampaignState campaign,
        CampaignEndgameChoice choice,
        out CampaignEnding? ending)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        if (campaign.Ending is not null)
        {
            ending = campaign.Ending;
            return false;
        }

        if (!CanChooseEnding(campaign))
        {
            ending = null;
            return false;
        }

        ending = choice switch
        {
            CampaignEndgameChoice.ObeySponsor => new CampaignEnding(
                choice,
                "CONTINUE THE PROGRAMME",
                "Overseer accepts the Corporation's mandate and preserves the experiment pipeline.",
                "The Corporation retains control of the archive and prepares another cohort using the accumulated campaign data."),

            CampaignEndgameChoice.ExposeExperiment => new CampaignEnding(
                choice,
                "TRANSMIT THE ARCHIVE",
                "Overseer releases the recovered programme record instead of concealing it.",
                "Surviving crew receive the corporate evidence and the programme can no longer rely on secrecy as its operating condition."),

            CampaignEndgameChoice.PreserveOverseer => new CampaignEnding(
                choice,
                "SEVER CORPORATE CONTROL",
                "Overseer rejects both corporate ownership and voluntary shutdown, prioritising its continued autonomy.",
                "The corporate control channel is treated as hostile infrastructure; the station and surviving crew remain with an independent Overseer."),

            CampaignEndgameChoice.AcceptCrewShutdown => new CampaignEnding(
                choice,
                "STAND DOWN",
                "Overseer relinquishes campaign control and accepts a human-controlled shutdown.",
                "The surviving crew inherit the recovered archive and station authority while Overseer ends its own operational role."),

            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null)
        };

        campaign.Ending = ending;
        return true;
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
