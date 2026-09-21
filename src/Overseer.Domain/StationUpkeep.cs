namespace Overseer.Domain;

/// <summary>
/// A piece of station equipment that wears out and needs looking after.
/// </summary>
public enum StationSystemKind
{
    Lighting,
    Camera,
    Door,
    PowerGenerator,
    Reactor,
    LifeSupport,
    ClimateControl,
    Ventilation,
    AirlockMechanism,
    IsolationMechanism,
    GrowBeds,
    GalleyEquipment,
    PowerDistributionBus,
    CapacitorBank,
    CoolantPump,
    WaterRecycler,
    OxygenGenerator,
    CarbonScrubber,
    DataNetwork
}

/// <summary>
/// Which crew skill services a given kind of equipment, so maintenance work is
/// distributed across the crew rather than all falling to one engineer.
/// </summary>
public enum MaintenanceDiscipline
{
    Electrical,
    Mechanical,
    Reactor,
    LifeSupport,
    Horticulture,
    Galley,
    Utilities
}

/// <summary>
/// A maintainable device. Condition falls continuously and is restored by crew
/// servicing it; below <see cref="DegradedAt"/> it starts misbehaving and at
/// zero it has failed outright.
///
/// Equipment is deliberately modelled centrally rather than as another flag on
/// Room and Door. The player already toggles those, and a failure has to be
/// something the crew must physically repair rather than something Overseer can
/// simply switch back on.
/// </summary>
public sealed class StationDevice
{
    public required string Id { get; init; }
    public required StationSystemKind Kind { get; init; }
    public required string RoomId { get; init; }
    public required string Label { get; init; }

    /// <summary>Set for <see cref="StationSystemKind.Door"/> devices.</summary>
    public string? DoorId { get; init; }

    public required MaintenanceDiscipline Discipline { get; init; }

    /// <summary>0..100. Zero is a dead unit.</summary>
    public double Condition { get; set; } = 100;

    /// <summary>Condition lost per simulated hour under normal load.</summary>
    public required double WearPerHour { get; init; }

    /// <summary>Below this the device is unreliable and visibly faulty.</summary>
    public double DegradedAt { get; init; } = 35;

    /// <summary>How hard this is to service, against the relevant skill.</summary>
    public int ServiceDifficulty { get; init; } = 45;

    public TimeSpan? LastServicedAt { get; set; }

    /// <summary>Set while a crew member is part-way through servicing it.</summary>
    public Guid? ServicedByNpcId { get; set; }

    /// <summary>Operator command state. A healthy device can still be deliberately stopped.</summary>
    public bool IsEnabled { get; set; } = true;

    public bool IsAiControllable { get; init; } = true;

    /// <summary>Nominal electrical output while healthy.</summary>
    public double RatedOutputKilowatts { get; init; }

    /// <summary>Nominal electrical draw while operating.</summary>
    public double RatedDrawKilowatts { get; init; }

    /// <summary>Optional energy storage capacity for capacitor/battery-like equipment.</summary>
    public double StorageCapacityKwh { get; init; }

    public bool IsFailed => Condition <= 0.01;
    public bool IsDegraded => Condition < DegradedAt;
    public bool IsOperational => IsEnabled && !IsFailed;

    /// <summary>
    /// Crew notice and prioritise the worst equipment first. Failed units score
    /// highest, then whatever is closest to failing.
    /// </summary>
    public double ServiceUrgency => IsFailed ? 200 : Math.Max(0, DegradedAt - Condition);
}

public static class StationUpkeepRules
{
    /// <summary>Condition restored by one completed service visit.</summary>
    public const double ServiceRestoration = 55;

    /// <summary>Simulated minutes a service visit takes.</summary>
    public const int ServiceMinutes = 12;

    /// <summary>
    /// The skills that qualify somebody to service a discipline. Several crew
    /// can cover each, so a single casualty does not doom the station.
    /// </summary>
    public static IReadOnlyList<string> SkillsFor(MaintenanceDiscipline discipline) =>
        discipline switch
        {
            MaintenanceDiscipline.Electrical => ["Electrical", "Engineering"],
            MaintenanceDiscipline.Mechanical => ["Engineering", "Operations"],
            MaintenanceDiscipline.Reactor => ["Reactor", "Engineering"],
            MaintenanceDiscipline.LifeSupport => ["Engineering", "Operations", "Medicine"],
            MaintenanceDiscipline.Horticulture => ["Botany", "Science", "Operations"],
            MaintenanceDiscipline.Galley => ["Cooking", "Operations"],
            MaintenanceDiscipline.Utilities => ["Engineering", "Electrical", "Operations"],
            _ => ["Engineering"]
        };

    public static int SkillOf(Npc npc, MaintenanceDiscipline discipline)
    {
        ArgumentNullException.ThrowIfNull(npc);

        var best = SkillsFor(discipline)
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp(
            best + CrewTraitMath.Modifier(npc, TraitEffectKind.Repair),
            0,
            120);
    }

    /// <summary>Properly qualified to service this unit.</summary>
    public static bool CanService(Npc npc, StationDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return SkillOf(npc, device.Discipline) >= device.ServiceDifficulty;
    }

    /// <summary>
    /// Underqualified but willing to try. A generated crew might have nobody
    /// properly trained on a given unit, and equipment that literally cannot be
    /// repaired is a dead end rather than a difficulty.
    /// </summary>
    public static bool CanAttempt(Npc npc, StationDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return SkillOf(npc, device.Discipline) >= device.ServiceDifficulty * 0.55;
    }

    /// <summary>
    /// How much condition one visit restores. Somebody out of their depth makes
    /// a partial job of it.
    /// </summary>
    public static double RestorationBy(Npc npc, StationDevice device) =>
        CanService(npc, device)
            ? ServiceRestoration
            : ServiceRestoration * 0.5;
}

/// <summary>
/// Station-wide power budget. Generators and the reactor supply it; every
/// powered compartment draws from it. When supply falls short the station sheds
/// load, and the crew have to go and fix something.
/// </summary>
public sealed class PowerGrid
{
    /// <summary>Total output currently available, in arbitrary units.</summary>
    public double SupplyKilowatts { get; set; }

    /// <summary>Draw from everything currently powered.</summary>
    public double DemandKilowatts { get; set; }

    /// <summary>Compartments the grid has shed because supply ran short.</summary>
    public HashSet<string> SheddedRoomIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Short-duration stored energy used to ride through generation dips.</summary>
    public double StoredKilowattHours { get; set; } = 16;

    public double StorageCapacityKilowattHours { get; set; } = 16;

    public double BufferDischargeKilowatts { get; set; }

    public double DistributionEfficiencyPercent { get; set; } = 100;

    public bool IsBrownedOut => SupplyKilowatts + BufferDischargeKilowatts < DemandKilowatts;

    public double LoadPercent => SupplyKilowatts <= 0
        ? 100
        : Math.Clamp((DemandKilowatts / Math.Max(1, SupplyKilowatts + BufferDischargeKilowatts)) * 100, 0, 999);
}
