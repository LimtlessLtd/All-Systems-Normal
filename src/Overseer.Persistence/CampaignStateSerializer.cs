using System.Text.Json;
using Overseer.Domain;

namespace Overseer.Persistence;

/// <summary>
/// Versioned campaign-only save format. It deliberately excludes GameState,
/// movement, intents, jobs, investigation leads and other transient simulation
/// objects.
/// </summary>
public static class CampaignStateSerializer
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        var document = new CampaignDocument
        {
            Version = CurrentVersion,
            MissionHistory = campaign.MissionHistory.ToList(),
            Crew = campaign.Crew.Select(ToDocument).ToList(),
            DeviceCondition = new Dictionary<string, double>(
                campaign.DeviceCondition,
                StringComparer.OrdinalIgnoreCase),
            Meals = campaign.Meals,
            Produce = campaign.Produce,
            Water = campaign.Water,
            Nutrients = campaign.Nutrients,
            CumulativeCompliance = campaign.CumulativeCompliance,
            RevealStage = campaign.RevealStage,
            CurrentScenarioId = campaign.CurrentScenarioId,
            Ending = campaign.Ending
        };

        return JsonSerializer.Serialize(document, Options);
    }

    public static CampaignState? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<CampaignDocument>(json, Options);
            if (document is null || document.Version != CurrentVersion)
            {
                return null;
            }

            var campaign = new CampaignState
            {
                Meals = Math.Max(0, document.Meals),
                Produce = Math.Max(0, document.Produce),
                Water = Math.Max(0, document.Water),
                Nutrients = Math.Max(0, document.Nutrients),
                CumulativeCompliance = Math.Clamp(document.CumulativeCompliance, 0, 100),
                RevealStage = document.RevealStage,
                CurrentScenarioId = document.CurrentScenarioId,
                Ending = document.Ending
            };

            campaign.MissionHistory.AddRange(document.MissionHistory ?? []);

            foreach (var snapshot in document.Crew ?? [])
            {
                campaign.Crew.Add(FromDocument(snapshot));
            }

            foreach (var (deviceId, condition) in document.DeviceCondition
                         ?? new Dictionary<string, double>())
            {
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    campaign.DeviceCondition[deviceId] = Math.Clamp(condition, 0, 100);
                }
            }

            return campaign;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static CrewDocument ToDocument(CrewContinuitySnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        Name = snapshot.Name,
        Role = snapshot.Role,
        Personality = snapshot.Personality,
        GenerationSource = snapshot.GenerationSource,
        Traits = snapshot.Traits.ToList(),
        Skills = new Dictionary<string, int>(snapshot.Skills, StringComparer.OrdinalIgnoreCase),
        Relationships = new Dictionary<string, RelationshipContinuitySnapshot>(
            snapshot.Relationships,
            StringComparer.OrdinalIgnoreCase),
        Memories = snapshot.Memories.ToList(),
        Health = snapshot.Health,
        IsPresent = snapshot.IsPresent,
        OverseerCredibility = snapshot.OverseerCredibility,
        OverseerSuspicion = snapshot.OverseerSuspicion,
        IsPrisoner = snapshot.IsPrisoner,
        PrisonerDangerLevel = snapshot.PrisonerDangerLevel,
        PrisonerViolenceBias = snapshot.PrisonerViolenceBias
    };

    private static CrewContinuitySnapshot FromDocument(CrewDocument document)
    {
        var snapshot = new CrewContinuitySnapshot
        {
            Id = document.Id,
            Name = string.IsNullOrWhiteSpace(document.Name) ? "Unknown Crew" : document.Name,
            Role = document.Role,
            Personality = document.Personality ?? new Personality(50, 50, 50, 50),
            GenerationSource = string.IsNullOrWhiteSpace(document.GenerationSource)
                ? "Persisted"
                : document.GenerationSource,
            Health = Math.Clamp(document.Health, 0, 100),
            IsPresent = document.IsPresent,
            OverseerCredibility = Math.Clamp(document.OverseerCredibility, 0, 100),
            OverseerSuspicion = Math.Clamp(document.OverseerSuspicion, 0, 100),
            IsPrisoner = document.IsPrisoner,
            PrisonerDangerLevel = document.PrisonerDangerLevel,
            PrisonerViolenceBias = document.PrisonerViolenceBias
        };

        snapshot.Traits.AddRange(document.Traits ?? []);

        foreach (var (skill, value) in document.Skills ?? new Dictionary<string, int>())
        {
            snapshot.Skills[skill] = value;
        }

        foreach (var (name, relationship) in document.Relationships
                     ?? new Dictionary<string, RelationshipContinuitySnapshot>())
        {
            snapshot.Relationships[name] = relationship;
        }

        snapshot.Memories.AddRange(document.Memories ?? []);
        return snapshot;
    }

    private sealed class CampaignDocument
    {
        public int Version { get; set; }
        public List<CampaignMissionResult>? MissionHistory { get; set; }
        public List<CrewDocument>? Crew { get; set; }
        public Dictionary<string, double>? DeviceCondition { get; set; }
        public double Meals { get; set; }
        public double Produce { get; set; }
        public double Water { get; set; }
        public double Nutrients { get; set; }
        public double CumulativeCompliance { get; set; }
        public CampaignRevealStage RevealStage { get; set; }
        public string? CurrentScenarioId { get; set; }
        public CampaignEnding? Ending { get; set; }
    }

    private sealed class CrewDocument
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public CrewRole Role { get; set; }
        public Personality? Personality { get; set; }
        public string? GenerationSource { get; set; }
        public List<CrewTrait>? Traits { get; set; }
        public Dictionary<string, int>? Skills { get; set; }
        public Dictionary<string, RelationshipContinuitySnapshot>? Relationships { get; set; }
        public List<Memory>? Memories { get; set; }
        public double Health { get; set; } = 100;
        public bool IsPresent { get; set; } = true;
        public double OverseerCredibility { get; set; } = 70;
        public double OverseerSuspicion { get; set; }
        public bool IsPrisoner { get; set; }
        public PrisonerDangerLevel PrisonerDangerLevel { get; set; } = PrisonerDangerLevel.Low;
        public double PrisonerViolenceBias { get; set; }
    }
}
