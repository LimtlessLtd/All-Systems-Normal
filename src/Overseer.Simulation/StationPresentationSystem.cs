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
            ' ',
            PurposeClass,
            BudgetClass,
            ExpansionClass,
            AgeClass,
            MaintenanceClass,
            HullClass);
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
                StationPurpose.Mixed,
                AgeYears: 35,
                StationBudgetClass.Standard,
                StationSizeClass.Standard,
                CrewCapacity: 12,
                IndustrialIntensity: 50,
                SecurityLevel: 50,
                MaintenanceCondition: 70,
                StationExpansionHistory.LightlyExpanded);

        var rooms = facility.Rooms.Values.ToList();
        var minX = rooms.Count == 0
            ? 5
            : rooms.Min(room => room.MapX - (room.MapWidth / 2));
        var maxX = rooms.Count == 0
            ? 95
            : rooms.Max(room => room.MapX + (room.MapWidth / 2));
        var minY = rooms.Count == 0
            ? 5
            : rooms.Min(room => room.MapY - (room.MapHeight / 2));
        var maxY = rooms.Count == 0
            ? 95
            : rooms.Max(room => room.MapY + (room.MapHeight / 2));

        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);

        // Leave enough breathing room for labels, crew nameplates and hatch art,
        // while making compact/off-centre procedural shapes occupy the viewport.
        var fitScale = Math.Clamp(
            Math.Min(90d / spanX, 86d / spanY),
            0.84,
            1.18);

        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;

        var offsetX = Math.Clamp((50 - centerX) * fitScale, -10, 10);
        var offsetY = Math.Clamp((50 - centerY) * fitScale, -10, 10);

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
            $"hull-variant-{Math.Abs(metadata?.Seed ?? 0) % 4}",
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

    private static string Slug<T>(T value)
        where T : struct, Enum =>
        value.ToString()
            .Replace("HeavilyRetrofitted", "heavily-retrofitted", StringComparison.Ordinal)
            .Replace("LightlyExpanded", "lightly-expanded", StringComparison.Ordinal)
            .Replace("PurposeBuilt", "purpose-built", StringComparison.Ordinal)
            .ToLowerInvariant();
}
