namespace Overseer.Domain;

/// <summary>
/// One planted bed in the hydroponics bay. Crops need water and nutrients to
/// grow, both of which drain and have to be topped up by somebody.
/// </summary>
public sealed class CropBed
{
    public required string Id { get; init; }
    public required string RoomId { get; init; }
    public required string Label { get; init; }

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
    /// <summary>Harvested but uncooked. Useless until somebody cooks it.</summary>
    public double Produce { get; set; } = 6;

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
