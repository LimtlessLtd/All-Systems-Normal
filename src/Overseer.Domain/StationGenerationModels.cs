namespace Overseer.Domain;

public enum StationArchetype
{
    Linear,
    CentralHub,
    Branching,
    Ring,
    AsymmetricIndustrial,
    Compact,
    Sprawling,
    MultiSpine,
    Retrofit
}

public enum StationPurpose
{
    Research,
    Mining,
    Industrial,
    Habitat,
    Logistics,
    Security,
    Mixed
}

public enum StationBudgetClass
{
    Frugal,
    Standard,
    Premium
}

public enum StationSizeClass
{
    Compact,
    Standard,
    Large
}

public enum StationExpansionHistory
{
    PurposeBuilt,
    LightlyExpanded,
    HeavilyRetrofitted
}

public sealed record StationIdentity(
    StationPurpose Purpose,
    int AgeYears,
    StationBudgetClass Budget,
    StationSizeClass Size,
    int CrewCapacity,
    int IndustrialIntensity,
    int SecurityLevel,
    int MaintenanceCondition,
    StationExpansionHistory ExpansionHistory);

public sealed record StationAdjacencyConstraint(
    string FirstRoomId,
    string SecondRoomId);

public sealed record StationSeparationConstraint(
    string FirstRoomId,
    string SecondRoomId,
    double MinimumMapDistance);

/// <summary>
/// An authored room definition whose physical location is still chosen by the
/// procedural packer. This is the normal set-piece hook for constrained missions.
/// </summary>
public sealed record StationRoomDefinition(
    string Id,
    string Name,
    RoomType Type,
    double MinWidth = 9,
    double MaxWidth = 16,
    double MinHeight = 10,
    double MaxHeight = 20);

public sealed record StationAuthoredGeometryRoom(
    string Id,
    string Name,
    RoomType Type,
    double MapX,
    double MapY,
    double MapWidth,
    double MapHeight);

public sealed record StationAuthoredConnection(
    string FirstRoomId,
    string SecondRoomId);

public sealed class StationRoomEnvironmentOverride
{
    public bool? IsPowered { get; init; }
    public bool? CameraOnline { get; init; }
    public bool? LightsOn { get; init; }
    public double? TemperatureC { get; init; }
    public double? OxygenPercent { get; init; }
    public double? CarbonDioxidePercent { get; init; }
    public double? PressureKpa { get; init; }
    public bool? VentilationEnabled { get; init; }
    public bool? HasTemperatureControl { get; init; }
    public bool? IsTemperatureAiControllable { get; init; }
    public bool? HasVentilationControl { get; init; }
    public bool? IsVentilationAiControllable { get; init; }
}

/// <summary>
/// Hard campaign/scenario requirements. Procedural preferences never override
/// values expressed here. The same simulation consumes fully procedural,
/// constrained, partially authored and fully authored stations.
/// </summary>
public sealed class StationGenerationConstraints
{
    public HashSet<string> RequiredRoomIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ForbiddenRoomIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<StationRoomDefinition> AuthoredRooms { get; } = [];
    public List<StationAdjacencyConstraint> RequiredAdjacency { get; } = [];
    public List<StationSeparationConstraint> RequiredSeparation { get; } = [];

    public StationArchetype? ForcedArchetype { get; init; }
    public StationPurpose? ForcedPurpose { get; init; }
    public StationBudgetClass? ForcedBudget { get; init; }
    public StationSizeClass? ForcedSize { get; init; }
    public StationExpansionHistory? ForcedExpansionHistory { get; init; }
    public int? ForcedSecurityLevel { get; init; }

    public int? MinimumFunctionalRoomCount { get; init; }
    public int? MaximumFunctionalRoomCount { get; init; }
    public int? RequiredAirlockCount { get; init; }
    public List<string> RequiredAirlockRoomIds { get; } = [];
    public int? RequiredTurretCount { get; init; }
    public List<string> RequiredTurretRoomIds { get; } = [];
    public int? RequiredRobotCount { get; init; }
    public List<string> RequiredRobotRoomIds { get; } = [];

    /// <summary>
    /// Crew population the physical station should normally sustain. Scenarios
    /// can set this independently of the generated identity.
    /// </summary>
    public int? PlannedCrewCount { get; init; }

    /// <summary>
    /// Scales the normally sufficient hydroponics footprint. Values below 1 are
    /// an explicit scenario/corporate undersupply override.
    /// </summary>
    public double HydroponicsCapacityMultiplier { get; init; } = 1;

    /// <summary>
    /// Optional campaign/corporate seed-supply contract. Empty means all crop
    /// kinds may be generated; otherwise only these crops can receive seed stock
    /// or be requested for planting.
    /// </summary>
    public HashSet<CropKind> AllowedCropKinds { get; } = [];

    public bool? RequireRedundantPaths { get; init; }
    public bool? ForbidRedundantPaths { get; init; }
    public int? RequiredChokepointCount { get; init; }
    public bool ReactorMustBeIsolated { get; init; }
    public bool MedicalMustBeNearHabitat { get; init; }
    public string? RequiredShutdownRoomId { get; init; }

    public HashSet<string> InitiallyAccessibleRoomIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> InitiallyInaccessibleRoomIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, StationRoomEnvironmentOverride> EnvironmentOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Exceptional mission path: bypass procedural spatial packing and use the
    /// supplied room/connection geometry directly.
    /// </summary>
    public bool FullyAuthoredGeometry { get; init; }
    public List<StationAuthoredGeometryRoom> FixedRooms { get; } = [];
    public List<StationAuthoredConnection> FixedConnections { get; } = [];

    /// <summary>
    /// Returns an independent copy with a different <see cref="PlannedCrewCount"/>,
    /// so a run can size provisioning to its actual roster without mutating the
    /// shared scenario constraints. Collection elements are immutable records
    /// or init-only objects, so they are shared rather than cloned.
    /// </summary>
    public StationGenerationConstraints WithPlannedCrewCount(int? plannedCrewCount)
    {
        var copy = new StationGenerationConstraints
        {
            ForcedArchetype = ForcedArchetype,
            ForcedPurpose = ForcedPurpose,
            ForcedBudget = ForcedBudget,
            ForcedSize = ForcedSize,
            ForcedExpansionHistory = ForcedExpansionHistory,
            ForcedSecurityLevel = ForcedSecurityLevel,
            MinimumFunctionalRoomCount = MinimumFunctionalRoomCount,
            MaximumFunctionalRoomCount = MaximumFunctionalRoomCount,
            RequiredAirlockCount = RequiredAirlockCount,
            RequiredTurretCount = RequiredTurretCount,
            RequiredRobotCount = RequiredRobotCount,
            PlannedCrewCount = plannedCrewCount,
            HydroponicsCapacityMultiplier = HydroponicsCapacityMultiplier,
            RequireRedundantPaths = RequireRedundantPaths,
            ForbidRedundantPaths = ForbidRedundantPaths,
            RequiredChokepointCount = RequiredChokepointCount,
            ReactorMustBeIsolated = ReactorMustBeIsolated,
            MedicalMustBeNearHabitat = MedicalMustBeNearHabitat,
            RequiredShutdownRoomId = RequiredShutdownRoomId,
            FullyAuthoredGeometry = FullyAuthoredGeometry
        };

        copy.RequiredRoomIds.UnionWith(RequiredRoomIds);
        copy.ForbiddenRoomIds.UnionWith(ForbiddenRoomIds);
        copy.AuthoredRooms.AddRange(AuthoredRooms);
        copy.RequiredAdjacency.AddRange(RequiredAdjacency);
        copy.RequiredSeparation.AddRange(RequiredSeparation);
        copy.RequiredAirlockRoomIds.AddRange(RequiredAirlockRoomIds);
        copy.RequiredTurretRoomIds.AddRange(RequiredTurretRoomIds);
        copy.RequiredRobotRoomIds.AddRange(RequiredRobotRoomIds);
        copy.AllowedCropKinds.UnionWith(AllowedCropKinds);
        copy.InitiallyAccessibleRoomIds.UnionWith(InitiallyAccessibleRoomIds);
        copy.InitiallyInaccessibleRoomIds.UnionWith(InitiallyInaccessibleRoomIds);
        foreach (var (roomId, environment) in EnvironmentOverrides)
        {
            copy.EnvironmentOverrides[roomId] = environment;
        }

        copy.FixedRooms.AddRange(FixedRooms);
        copy.FixedConnections.AddRange(FixedConnections);
        return copy;
    }
}

public sealed class StationGenerationMetadata
{
    public required int Seed { get; init; }
    public required StationArchetype Archetype { get; init; }
    public required StationIdentity Identity { get; init; }
    public string PrimaryCorridorId { get; init; } = "corridor";
    public bool CampaignOverridesApplied { get; init; }
    public List<string> Diagnostics { get; } = [];
}
