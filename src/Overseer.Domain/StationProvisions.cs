namespace Overseer.Domain;

public enum CropKind
{
    Tomato,
    Potato,
    Apple,
    Grape,
    Banana,
    Tobacco,
    Wheat
}

public static class CropRules
{
    public static bool IsEdibleRaw(CropKind crop) => crop is not CropKind.Tobacco;

    public static double RawHungerReliefPerMinute(CropKind crop) => crop switch
    {
        CropKind.Potato or CropKind.Wheat => 0.58,
        CropKind.Tomato or CropKind.Grape => 0.68,
        CropKind.Apple or CropKind.Banana => 0.78,
        _ => 0
    };
}

public static class FoodPreferenceRules
{
    public static void EnsureDefaults(Npc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (npc.FoodLikes.Count > 0 || npc.FoodDislikes.Count > 0)
            return;

        var edible = Enum.GetValues<CropKind>().Where(CropRules.IsEdibleRaw).ToArray();
        var first = StablePick(npc.Name, edible.Length, 17);
        var second = StablePick(npc.Name, edible.Length, 43);
        if (second == first) second = (second + 1) % edible.Length;
        var dislike = StablePick(npc.Name, edible.Length, 89);
        while (dislike == first || dislike == second)
            dislike = (dislike + 1) % edible.Length;

        npc.FoodLikes.Add(edible[first]);
        npc.FoodLikes.Add(edible[second]);
        npc.FoodDislikes.Add(edible[dislike]);
    }

    public static int PreferenceScore(Npc npc, CropKind crop)
    {
        EnsureDefaults(npc);
        if (npc.FoodLikes.Contains(crop)) return 2;
        if (npc.FoodDislikes.Contains(crop)) return -2;
        return 0;
    }

    private static int StablePick(string value, int count, int salt)
    {
        unchecked
        {
            uint hash = 2166136261u + (uint)salt;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return (int)(hash % (uint)count);
        }
    }
}

public enum CropLifecycleState
{
    Empty,
    Planting,
    Seedling,
    Maturing,
    ReadyToHarvest,
    Harvesting,
    Dead
}

/// <summary>
/// One physical grow bay. Lifecycle, capacity and shutdown consequences are
/// deterministic simulation state; rendering only reflects these fields.
/// </summary>
public sealed class CropBed
{
    public required string Id { get; init; }
    public required string RoomId { get; init; }
    public required string Label { get; init; }
    public required string FixtureLabel { get; init; }

    public CropKind Crop { get; set; } = CropKind.Wheat;
    public CropKind? RequestedCrop { get; set; }
    public CropLifecycleState Lifecycle { get; set; } = CropLifecycleState.Empty;
    public bool IsEnabled { get; set; } = true;

    /// <summary>Physical grow-area capacity relative to one standard bay.</summary>
    public double Capacity { get; init; } = 1;

    /// <summary>0..100 biological maturity after planting.</summary>
    public double Growth { get; set; }

    public double Water { get; set; } = 100;
    public double Nutrients { get; set; } = 100;
    public TimeSpan? DisabledSince { get; set; }
    public TimeSpan? LifecycleChangedAt { get; set; }

    public bool IsDead
    {
        get => Lifecycle == CropLifecycleState.Dead;
        set
        {
            if (value) Lifecycle = CropLifecycleState.Dead;
            else if (Lifecycle == CropLifecycleState.Dead) Lifecycle = CropLifecycleState.Empty;
        }
    }

    public bool IsReadyToHarvest => Lifecycle == CropLifecycleState.ReadyToHarvest;

    public double HarvestYield =>
        Math.Max(0.5, Capacity) * StationProvisionRules.YieldPerCapacityUnit;

    public double TendUrgency => Lifecycle is CropLifecycleState.Empty or CropLifecycleState.Planting
        ? 0
        : Lifecycle == CropLifecycleState.Dead
            ? 60
            : Math.Max(0, 60 - Math.Min(Water, Nutrients));
}

/// <summary>
/// What the station has in hand. Produce comes out of hydroponics, meals come
/// out of the galley, and the crew eat the meals.
/// </summary>
public sealed class StationStores
{
    /// <summary>Harvested produce available to the galley.</summary>
    public double Produce { get; set; } = 6;

    /// <summary>
    /// Typed harvested crops. This drives raw eating and crop-specific UI while
    /// Produce remains the galley's aggregate cooking stock for compatibility.
    /// </summary>
    public Dictionary<CropKind, double> RawCrops { get; } =
        Enum.GetValues<CropKind>().ToDictionary(crop => crop, _ => 0d);

    /// <summary>Generated planting inventory; a planting job consumes one unit.</summary>
    public Dictionary<CropKind, double> Seeds { get; } =
        Enum.GetValues<CropKind>().ToDictionary(crop => crop, _ => 0d);

    /// <summary>Ready to eat.</summary>
    public double Meals { get; set; } = 10;

    /// <summary>Irrigation supply, replenished from the station's reclaim loop.</summary>
    public double Water { get; set; } = 100;

    /// <summary>Feed stock for the grow beds.</summary>
    public double Nutrients { get; set; } = 100;

    public bool HasMeal => Meals >= 1;
}

public static class StationProvisionRules
{
    /// <summary>Growth per simulated hour in good conditions.</summary>
    public const double GrowthPerHour = 5.5;

    /// <summary>Water and nutrient draw per bed per hour.</summary>
    public const double ConsumptionPerHour = 2.4;

    /// <summary>Produce yielded per standard unit of physical grow capacity.</summary>
    public const double YieldPerCapacityUnit = 6;

    /// <summary>Compatibility alias for one standard bay.</summary>
    public const double YieldPerHarvest = YieldPerCapacityUnit;

    /// <summary>A disabled planted bay dies after this deterministic interval.</summary>
    public const double DisabledCropDeathHours = 12;

    /// <summary>Seedling becomes maturing at this biological maturity.</summary>
    public const double SeedlingEndsAtGrowth = 35;

    /// <summary>Produce consumed and meals produced by one cooking session.</summary>
    public const double ProducePerCookingSession = 3;

    public const double MealsPerCookingSession = 9;

    /// <summary>Hunger relieved by one meal.</summary>
    public const double HungerPerMeal = 42;

    /// <summary>
    /// Raw crops are emergency food: they are consumed more slowly, relieve
    /// substantially less hunger and usually make morale/stress worse.
    /// </summary>
    public const double RawCropUnitsPerMinute = 0.045;
    public const double RawFoodStressPerMinute = 0.24;

    /// <summary>Hunger above which somebody goes looking for food.</summary>
    public const double HungryAt = 45;

    /// <summary>
    /// Meal stock below which the galley is worth firing up. Six people eating
    /// through a day get through a lot, so the crew cook well before the
    /// cupboard is bare.
    /// </summary>
    public const double RestockMealsBelow = 14;

    /// <summary>Simulated minutes each job takes.</summary>
    public const int TendMinutes = 8;

    public const int PlantMinutes = 8;

    public const int HarvestMinutes = 10;

    public const int CookMinutes = 14;
}
