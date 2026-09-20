namespace Overseer.Domain;

public enum CampaignRevealStage
{
    Classified,
    Uneasy,
    Compromised,
    Exposed
}

public sealed class CampaignState
{
    public List<CampaignMissionResult> MissionHistory { get; } = [];
    public List<CrewContinuitySnapshot> Crew { get; } = [];
    public Dictionary<string, double> DeviceCondition { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public double Meals { get; set; } = 10;
    public double Produce { get; set; } = 6;
    public double Water { get; set; } = 100;
    public double Nutrients { get; set; } = 100;
    public double CumulativeCompliance { get; set; } = 100;
    public CampaignRevealStage RevealStage { get; set; } = CampaignRevealStage.Classified;
    public string? CurrentScenarioId { get; set; }

    public bool HasCompleted(string scenarioId) =>
        MissionHistory.Any(result =>
            result.ScenarioId.Equals(scenarioId, StringComparison.OrdinalIgnoreCase));
}

public sealed record CampaignMissionResult(
    string ScenarioId,
    string ScenarioTitle,
    ScenarioStatus Outcome,
    double ComplianceScore,
    int ExperimentScore,
    int CrewSurviving,
    TimeSpan SimulatedTime);

public sealed class CrewContinuitySnapshot
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required CrewRole Role { get; init; }
    public required Personality Personality { get; init; }
    public required string GenerationSource { get; init; }
    public List<CrewTrait> Traits { get; } = [];
    public Dictionary<string, int> Skills { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RelationshipContinuitySnapshot> Relationships { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<Memory> Memories { get; } = [];
    public double Health { get; set; } = 100;
    public bool IsPresent { get; set; } = true;
    public double OverseerCredibility { get; set; } = 70;
    public double OverseerSuspicion { get; set; }
}

public sealed record RelationshipContinuitySnapshot(
    double Affinity,
    double Trust,
    double Resentment,
    double Attraction,
    int Conversations,
    int Arguments);
