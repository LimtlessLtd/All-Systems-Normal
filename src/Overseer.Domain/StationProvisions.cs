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

/// <summary>
/// One planted bed in the hydroponics bay. Crops need water and nutrients to
/// grow, both of which drain and have to be topped up by somebody.
/// </summary>
public sealed class CropBed
{
    public required string Id { get; init; }
    public required string RoomId { get; init; }
    public required string Label { get; init; }
    public CropKind Crop { get; init; } = CropKind.Wheat;

    /// <summary>0..100. At 100 the bed is ready to harvest.</summary>
    public double Growth { get; set; }

    /// <summary>0..100. Growth stops dry and the crop starts dying.</summary>
    public double Water { get; set; } = 100;

    /// <summary>0..100. Same again for feed.</summary>
    public double Nutrients { get; set; } = 100;

    /// <summary>A bed left too long without water has to be replanted.</summary>
    public bool IsDead { get; set; }

    public bool IsReadyToHarvest => !IsDead && Growth >= 100;

    /// <summary>Whether this bed needs somebody's attention, and how badly.</summary>
    public double TendUrgency => IsDead
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

    /// <summary>Produce yielded by harvesting one mature bed.</summary>
    public const double YieldPerHarvest = 6;

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

    public const int HarvestMinutes = 10;

    public const int CookMinutes = 14;
}
