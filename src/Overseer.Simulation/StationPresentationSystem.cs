using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Pure presentation metadata derived from authoritative station generation/state.
/// This never invents geometry: it only tells both UIs how to frame and skin the
/// rooms, corridors and entities that already exist in the simulation.
/// </summary>
public sealed record StationPresentationProfile(
    string PurposeClass,
    string BudgetClass,
    string ExpansionClass,
    string AgeClass,
    string MaintenanceClass,
    string HullClass,
    double FitScale,
    double FitOffsetX,
    double FitOffsetY)
{
    public string CssClasses =>
        string.Join(
            " ",
            new[]
            {
                PurposeClass,
                BudgetClass,
                ExpansionClass,
                AgeClass,
                MaintenanceClass,
                HullClass
            });
}

public static class StationPresentationSystem
{
    public static StationPresentationProfile Build(GameState state) =>
        Build(state.Facility, state.StationGeneration);

    public static StationPresentationProfile Build(
        Facility facility,
        StationGenerationMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(facility);

        var identity = metadata?.Identity
            ?? new StationIdentity(
                Purpose: StationPurpose.Mixed,
                AgeYears: 35,
                Budget: StationBudgetClass.Standard,
                Size: StationSizeClass.Standard,
                CrewCapacity: 12,
                IndustrialIntensity: 50,
                SecurityLevel: 50,
                MaintenanceCondition: 70,
                ExpansionHistory: StationExpansionHistory.LightlyExpanded);

        // V0.10D uses a deliberately large virtual deck and a pannable camera.
        // Do not auto-fit the authoritative station back into the viewport:
        // doing so made every procedurally large room visually tiny again.
        const double fitScale = 1;
        const double offsetX = 0;
        const double offsetY = 0;

        return new StationPresentationProfile(
            $"purpose-{Slug(identity.Purpose)}",
            $"budget-{Slug(identity.Budget)}",
            $"expansion-{Slug(identity.ExpansionHistory)}",
            identity.AgeYears >= 80
                ? "age-legacy"
                : identity.AgeYears >= 35
                    ? "age-mature"
                    : "age-modern",
            identity.MaintenanceCondition < 40
                ? "maintenance-battered"
                : identity.MaintenanceCondition < 70
                    ? "maintenance-worn"
                    : "maintenance-maintained",
            $"hull-variant-{unchecked((uint)(metadata?.Seed ?? 0)) % 4}",
            fitScale,
            offsetX,
            offsetY);
    }

    public static string RoomScaleClass(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        if (room.Type == RoomType.Corridor)
        {
            return "room-circulation";
        }

        var area = room.MapWidth * room.MapHeight;
        if (area < 125)
        {
            return "room-small";
        }

        if (area > 245)
        {
            return "room-large";
        }

        return "room-medium";
    }

    private const string GeneratedFixturePrefix = "Generated ";

    /// <summary>
    /// Player-facing fixture name. Identity-driven fixtures keep their internal
    /// "Generated {Type} {n}" label because generation tests and map styling key
    /// off it; players should see "Utility panel 1" rather than that bookkeeping.
    /// </summary>
    public static string FixtureDisplayName(RoomFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (!fixture.Label.StartsWith(GeneratedFixturePrefix, StringComparison.Ordinal))
        {
            return fixture.Label;
        }

        var typeName = fixture.Type.ToString();
        var remainder = fixture.Label[GeneratedFixturePrefix.Length..];
        var suffix = remainder.StartsWith(typeName, StringComparison.Ordinal)
            ? remainder[typeName.Length..]
            : "";

        return HumanizeIdentifier(typeName) + suffix;
    }

    private static string HumanizeIdentifier(string identifier)
    {
        var builder = new System.Text.StringBuilder(identifier.Length + 4);

        for (var index = 0; index < identifier.Length; index++)
        {
            var character = identifier[index];

            if (index > 0 && char.IsUpper(character))
            {
                builder.Append(' ');
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string Slug<T>(T value)
        where T : struct, Enum =>
        value.ToString()
            .Replace("HeavilyRetrofitted", "heavily-retrofitted", StringComparison.Ordinal)
            .Replace("LightlyExpanded", "lightly-expanded", StringComparison.Ordinal)
            .Replace("PurposeBuilt", "purpose-built", StringComparison.Ordinal)
            .ToLowerInvariant();
}
