using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Where the crew's food actually comes from.
///
/// Hunger used to fall the moment somebody decided to eat, which meant the
/// kitchen and hydroponics bay were scenery. Now crops grow in beds that need
/// watering and feeding, somebody has to harvest them, somebody has to cook the
/// produce, and only then is there a meal to eat.
///
/// That chain is the point. It gives Overseer a supply line to interfere with
/// that has nothing to do with opening hatches: cut power to hydroponics and
/// the crop stops growing; seal the galley and the produce never becomes food;
/// keep the botanist busy elsewhere and the beds dry out. None of it looks like
/// an attack, and all of it eventually shows up as a hungry, irritable crew.
/// </summary>
public sealed class CrewProvisioningSystem
{
    private readonly NavigationSystem _navigation = new();

    /// <summary>An existing goal this urgent is not interrupted for chores.</summary>
    private const int ProtectedUrgency = 70;

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running || state.CropBeds.Count == 0)
        {
            return;
        }

        GrowCrops(state, delta.TotalHours);
        ReplenishSupply(state, delta.TotalHours);

        foreach (var npc in state.Crew.Where(n => n.IsAlive && n.IsPresent))
        {
            ProgressJob(state, npc);
        }

        AssignWork(state);
    }

    /// <summary>
    /// Plants the bay out. Beds start staggered so the station has a rolling
    /// harvest rather than everything ripening at once.
    /// </summary>
    public static void Plant(
        GameState state,
        int seed,
        IReadOnlySet<CropKind>? allowedCrops = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.CropBeds.Clear();
        var random = new Random(seed);
        var allCrops = Enum.GetValues<CropKind>();
        var crops = allowedCrops is { Count: > 0 }
            ? allCrops.Where(allowedCrops.Contains).ToArray()
            : allCrops;

        if (crops.Length == 0)
            throw new InvalidOperationException("Hydroponics seed-supply contract permits no crop kinds.");

        var totalBeds = state.Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Hydroponics)
            .Sum(room => room.Fixtures.Count(fixture => fixture.Type == FixtureType.GrowBed));
        var baselineSeedsPerCrop = (int)Math.Ceiling(totalBeds / (double)crops.Length) + 2;

        // Seed inventory is authoritative and generated. Explicit mission or
        // corporate constraints can remove crop types entirely; planting cannot
        // invent seed stock the station was never supplied.
        foreach (var crop in allCrops)
            state.Stores.Seeds[crop] = 0;
        foreach (var crop in crops)
            state.Stores.Seeds[crop] = baselineSeedsPerCrop + random.Next(4);

        foreach (var room in state.Facility.Rooms.Values
                     .Where(r => r.Type == RoomType.Hydroponics)
                     .OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var fixtures = room.Fixtures
                .Where(fixture => fixture.Type == FixtureType.GrowBed)
                .OrderBy(fixture => fixture.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < fixtures.Count; index++)
            {
                var fixture = fixtures[index];
                var requested = crops[index % crops.Length];

                // Fixture coordinates are percentages of this generated room.
                // Convert them back into actual map area so a visibly larger bay
                // really does have greater productive capacity and yield.
                var physicalArea =
                    (fixture.Width / 100d * room.MapWidth)
                    * (fixture.Height / 100d * room.MapHeight);

                state.CropBeds.Add(new CropBed
                {
                    Id = $"bed:{room.Id}:{index + 1}",
                    RoomId = room.Id,
                    Label = fixture.Label,
                    FixtureLabel = fixture.Label,
                    Crop = requested,
                    RequestedCrop = requested,
                    Lifecycle = CropLifecycleState.Empty,
                    Capacity = Math.Round(Math.Max(0.25, physicalArea / StationProvisionRules.StandardGrowAreaMapUnits), 2),
                    Water = Math.Round(70 + (random.NextDouble() * 30), 1),
                    Nutrients = Math.Round(70 + (random.NextDouble() * 30), 1),
                    LifecycleChangedAt = state.Elapsed
                });
            }
        }
    }

    // ---------------------------------------------------------------- crops --

    private static void GrowCrops(GameState state, double hours)
    {
        foreach (var bed in state.CropBeds)
        {
            if (bed.Lifecycle is CropLifecycleState.Empty
                or CropLifecycleState.Planting
                or CropLifecycleState.Harvesting
                or CropLifecycleState.Dead)
            {
                continue;
            }

            if (!bed.IsEnabled)
            {
                bed.DisabledSince ??= state.Elapsed;
                if (state.Elapsed - bed.DisabledSince.Value
                    >= TimeSpan.FromHours(StationProvisionRules.DisabledCropDeathHours))
                {
                    bed.Lifecycle = CropLifecycleState.Dead;
                    bed.LifecycleChangedAt = state.Elapsed;
                    Log(state, $"{bed.Label} crop dies after prolonged shutdown.");
                }
                continue;
            }

            bed.DisabledSince = null;
            bed.Water = Math.Clamp(
                bed.Water - (StationProvisionRules.ConsumptionPerHour * hours * bed.Capacity),
                0,
                100);
            bed.Nutrients = Math.Clamp(
                bed.Nutrients - (StationProvisionRules.ConsumptionPerHour * hours * bed.Capacity),
                0,
                100);

            if (!state.Facility.Rooms.TryGetValue(bed.RoomId, out var room))
                continue;

            var growSystem = state.Devices.Values.FirstOrDefault(device =>
                device.Kind == StationSystemKind.GrowBeds && device.RoomId == bed.RoomId);

            var equipment = growSystem is null || growSystem.IsFailed || !growSystem.IsEnabled
                ? 0
                : Math.Clamp(growSystem.Condition / 100, 0.2, 1);
            var lit = room.IsPowered && room.LightsOn ? 1 : 0;
            var supplied = Math.Min(bed.Water, bed.Nutrients) > 0 ? 1 : 0;

            if (lit == 0 || supplied == 0 || equipment == 0)
            {
                bed.Growth = Math.Max(0, bed.Growth - (1.5 * hours));
                if (bed.Growth <= 0 && bed.Water <= 0)
                {
                    bed.Lifecycle = CropLifecycleState.Dead;
                    bed.LifecycleChangedAt = state.Elapsed;
                }
                continue;
            }

            bed.Growth = Math.Clamp(
                bed.Growth + (StationProvisionRules.GrowthPerHour * equipment * hours),
                0,
                100);

            var next = bed.Growth >= 100
                ? CropLifecycleState.ReadyToHarvest
                : bed.Growth >= StationProvisionRules.SeedlingEndsAtGrowth
                    ? CropLifecycleState.Maturing
                    : CropLifecycleState.Seedling;
            if (next != bed.Lifecycle)
            {
                bed.Lifecycle = next;
                bed.LifecycleChangedAt = state.Elapsed;
            }
        }
    }

    /// <summary>
    /// The reclaim loop tops up irrigation stock, but only while life support is
    /// actually running.
    /// </summary>
    private static void ReplenishSupply(GameState state, double hours)
    {
        if (!state.LifeSupport.IsOnline)
        {
            return;
        }

        state.Stores.Water = Math.Clamp(state.Stores.Water + (22 * hours), 0, 150);
        state.Stores.Nutrients = Math.Clamp(state.Stores.Nutrients + (18 * hours), 0, 150);
    }

    // ----------------------------------------------------------------- jobs --

    private void AssignWork(GameState state)
    {
        var kitchen = state.Facility.Rooms.Values
            .FirstOrDefault(room => room.Type == RoomType.Kitchen);

        foreach (var npc in state.Crew
                     .Where(npc => IsAvailable(state, npc))
                     .OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            // Mature produce is time-sensitive.
            var ripe = Unclaimed(state, bed => bed.IsReadyToHarvest)
                .FirstOrDefault(bed => CanReach(state, npc, bed.RoomId));

            if (ripe is not null && Qualified(npc, MaintenanceDiscipline.Horticulture))
            {
                Assign(state, npc, ActionKind.Harvest, ripe.RoomId, ripe.Id,
                    $"Harvest {ripe.Label}.",
                    "The crop is ready and will not keep.",
                    50);
                continue;
            }

            // Empty enabled bays need a physical planting visit. The selected
            // crop must exist in generated seed inventory.
            var empty = Unclaimed(state, bed =>
                    bed.IsEnabled
                    && bed.Lifecycle == CropLifecycleState.Empty
                    && bed.RequestedCrop is { } crop
                    && state.Stores.Seeds.TryGetValue(crop, out var seeds)
                    && seeds >= 1)
                .FirstOrDefault(bed => CanReach(state, npc, bed.RoomId));

            if (empty is not null && Qualified(npc, MaintenanceDiscipline.Horticulture))
            {
                Assign(state, npc, ActionKind.TendCrops, empty.RoomId, empty.Id,
                    $"Plant {empty.RequestedCrop} in {empty.Label}.",
                    "An enabled grow bay is empty and seed stock is available.",
                    48);
                continue;
            }

            // Then the galley, if the station is short of meals.
            if (kitchen is not null
                && state.Stores.Meals < StationProvisionRules.RestockMealsBelow
                && state.Stores.Produce >= StationProvisionRules.ProducePerCookingSession
                && Qualified(npc, MaintenanceDiscipline.Galley)
                && CanReach(state, npc, kitchen.Id)
                && !state.Crew.Any(other => other.ProvisioningJob == ActionKind.Cook))
            {
                Assign(state, npc, ActionKind.Cook, kitchen.Id, null,
                    "Get a meal service on.",
                    "The station is low on prepared food.",
                    state.Stores.Meals <= 2 ? 75 : 45);
                continue;
            }

            // Then whichever bed is thirstiest.
            var thirsty = Unclaimed(state, bed => bed.IsEnabled && bed.TendUrgency >= 15)
                .OrderByDescending(bed => bed.TendUrgency)
                .FirstOrDefault(bed => CanReach(state, npc, bed.RoomId));

            if (thirsty is not null && Qualified(npc, MaintenanceDiscipline.Horticulture))
            {
                Assign(state, npc, ActionKind.TendCrops, thirsty.RoomId, thirsty.Id,
                    $"Water and feed {thirsty.Label}.",
                    thirsty.IsDead
                        ? "That bed has died and needs replanting."
                        : "The bed is running dry.",
                    45);
            }
        }
    }

    private static IEnumerable<CropBed> Unclaimed(GameState state, Func<CropBed, bool> predicate)
    {
        var claimed = state.Crew
            .Where(npc => npc.IsAlive && npc.TendingBedId is not null)
            .Select(npc => npc.TendingBedId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return state.CropBeds.Where(bed => predicate(bed) && !claimed.Contains(bed.Id));
    }

    private static void Assign(
        GameState state,
        Npc npc,
        ActionKind kind,
        string roomId,
        string? bedId,
        string goal,
        string reason,
        int urgency)
    {
        npc.TendingBedId = bedId;
        npc.ProvisioningJob = kind;
        npc.ProvisioningRoomId = roomId;
        npc.ProvisioningCompletesAt = null;
        npc.Intent = new NpcIntent(kind, roomId, goal, reason, urgency, "Duty", state.Elapsed);
    }

    private static void ProgressJob(GameState state, Npc npc)
    {
        if (npc.ProvisioningJob is not { } job || npc.ProvisioningRoomId is not { } jobRoom)
            return;

        if (npc.Intent is { } competing
            && competing.Action is not (ActionKind.TendCrops or ActionKind.Harvest or ActionKind.Cook))
        {
            if (competing.Urgency >= ProtectedUrgency)
            {
                InterruptJob(state, npc, $"Higher-priority {competing.Action} intent (urgency {competing.Urgency}) took precedence.");
                return;
            }
            npc.Intent = null;
        }

        if (!npc.CurrentRoomId.Equals(jobRoom, StringComparison.OrdinalIgnoreCase))
        {
            if (npc.ProvisioningCompletesAt is not null)
                InterruptJob(state, npc, "Worker left the task area before completion.");
            return;
        }

        if (!state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room) || !room.IsPowered)
        {
            if (npc.ProvisioningCompletesAt is not null)
                InterruptJob(state, npc, "Required room power was lost.");
            return;
        }

        var minutes = job switch
        {
            ActionKind.Harvest => StationProvisionRules.HarvestMinutes,
            ActionKind.Cook => StationProvisionRules.CookMinutes,
            _ => StationProvisionRules.TendMinutes
        };

        if (npc.ProvisioningCompletesAt is null)
        {
            npc.ProvisioningCompletesAt = state.Elapsed + TimeSpan.FromMinutes(minutes);

            var bed = npc.TendingBedId is null
                ? null
                : state.CropBeds.FirstOrDefault(candidate => candidate.Id == npc.TendingBedId);

            if (job == ActionKind.Harvest && bed is { Lifecycle: CropLifecycleState.ReadyToHarvest })
            {
                bed.Lifecycle = CropLifecycleState.Harvesting;
                bed.LifecycleChangedAt = state.Elapsed;
            }
            else if (job == ActionKind.TendCrops && bed is { Lifecycle: CropLifecycleState.Empty })
            {
                bed.Lifecycle = CropLifecycleState.Planting;
                bed.LifecycleChangedAt = state.Elapsed;
            }

            npc.CurrentAction = new NpcAction(job, npc.TendingBedId ?? jobRoom, JobDescription(job, bed));
            CrewTaskSystem.Start(
                state,
                npc,
                job,
                npc.TendingBedId ?? jobRoom,
                JobDescription(job, bed),
                TimeSpan.FromMinutes(minutes));
            return;
        }

        if (state.Elapsed < npc.ProvisioningCompletesAt.Value)
            return;

        switch (job)
        {
            case ActionKind.Harvest:
                CompleteHarvest(state, npc);
                break;
            case ActionKind.Cook:
                CompleteCooking(state, npc);
                break;
            default:
                CompleteTending(state, npc);
                break;
        }

        npc.Intent = null;
        npc.RoutineUntil = TimeSpan.Zero;
        ReleaseJob(npc);
    }

    public static void InterruptForExternalPriority(GameState state, Npc npc, string reason)
    {
        if (npc.ProvisioningJob is not null)
            InterruptJob(state, npc, reason);
    }

    private static void InterruptJob(GameState state, Npc npc, string reason)
    {
        var bed = npc.TendingBedId is null
            ? null
            : state.CropBeds.FirstOrDefault(candidate => candidate.Id == npc.TendingBedId);

        if (bed?.Lifecycle == CropLifecycleState.Harvesting)
            bed.Lifecycle = CropLifecycleState.ReadyToHarvest;
        else if (bed?.Lifecycle == CropLifecycleState.Planting)
            bed.Lifecycle = CropLifecycleState.Empty;

        if (bed is not null)
            bed.LifecycleChangedAt = state.Elapsed;

        CrewTaskSystem.Interrupt(state, npc, reason);
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
        ReleaseJob(npc);
    }

    private static string JobDescription(ActionKind job, CropBed? bed) => job switch
    {
        ActionKind.Harvest => $"Harvesting {bed?.Label ?? "crop"}.",
        ActionKind.Cook => "Running a meal service.",
        _ when bed?.Lifecycle == CropLifecycleState.Planting => $"Planting {bed.RequestedCrop} in {bed.Label}.",
        _ when bed?.Lifecycle == CropLifecycleState.Dead => $"Clearing dead crop from {bed.Label}.",
        _ => $"Watering and feeding {bed?.Label ?? "grow bay"}."
    };

    private static void ReleaseJob(Npc npc)
    {
        npc.TendingBedId = null;
        npc.ProvisioningJob = null;
        npc.ProvisioningRoomId = null;
        npc.ProvisioningCompletesAt = null;
    }

    private static void CompleteHarvest(GameState state, Npc npc)
    {
        var bed = state.CropBeds.FirstOrDefault(b => b.Id == npc.TendingBedId);
        if (bed is null || bed.Lifecycle != CropLifecycleState.Harvesting)
        {
            CrewTaskSystem.Fail(state, npc, "Crop was no longer harvestable.");
            return;
        }

        var crop = bed.Crop;
        var yield = bed.HarvestYield;
        state.Stores.Produce += yield;
        state.Stores.RawCrops[crop] += yield;
        bed.Growth = 0;
        bed.Lifecycle = CropLifecycleState.Empty;
        bed.LifecycleChangedAt = state.Elapsed;
        bed.RequestedCrop ??= crop;

        var outcome = $"Harvested {yield:0.0} units of {crop}; bay returned to Empty.";
        CrewTaskSystem.Succeed(state, npc, outcome);
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, outcome);
        Log(state, $"{npc.Name} harvests {bed.Label}. {crop} stock {state.Stores.RawCrops[crop]:0.0}; total produce {state.Stores.Produce:0.0}.");
    }

    private static void CompleteCooking(GameState state, Npc npc)
    {
        var galley = state.Devices.Values.FirstOrDefault(device =>
            device.Kind == StationSystemKind.GalleyEquipment
            && device.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (galley is null || galley.IsFailed)
        {
            CrewTaskSystem.Fail(state, npc, "The galley equipment is dead. Nothing can be cooked.");
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                "The galley equipment is dead. Nothing can be cooked.");
            return;
        }

        if (state.Stores.Produce < StationProvisionRules.ProducePerCookingSession)
        {
            CrewTaskSystem.Fail(state, npc, "There is not enough produce to complete the meal service.");
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                "There is nothing left to cook with.");
            return;
        }

        state.Stores.Produce -= StationProvisionRules.ProducePerCookingSession;
        ConsumeTypedProduce(state.Stores, StationProvisionRules.ProducePerCookingSession);
        state.Stores.Meals += StationProvisionRules.MealsPerCookingSession;

        CrewTaskSystem.Succeed(state, npc, "Meal service completed.");
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Meal service is up.");
        Log(state, $"{npc.Name} prepares a meal service. {state.Stores.Meals:0} meals ready.");
    }

    private static void CompleteTending(GameState state, Npc npc)
    {
        var bed = state.CropBeds.FirstOrDefault(b => b.Id == npc.TendingBedId);
        if (bed is null)
        {
            CrewTaskSystem.Fail(state, npc, "Grow bay no longer exists.");
            return;
        }

        if (bed.Lifecycle == CropLifecycleState.Planting)
        {
            if (!bed.IsEnabled || bed.RequestedCrop is not { } crop
                || !state.Stores.Seeds.TryGetValue(crop, out var seedStock) || seedStock < 1)
            {
                bed.Lifecycle = CropLifecycleState.Empty;
                CrewTaskSystem.Fail(state, npc, "Planting could not complete because the bay was disabled or seed stock was unavailable.");
                return;
            }

            state.Stores.Seeds[crop] = seedStock - 1;
            bed.Crop = crop;
            bed.Growth = 0;
            bed.Water = Math.Max(55, bed.Water);
            bed.Nutrients = Math.Max(55, bed.Nutrients);
            bed.Lifecycle = CropLifecycleState.Seedling;
            bed.LifecycleChangedAt = state.Elapsed;
            CrewTaskSystem.Succeed(state, npc, $"Planted {crop}; bay entered Seedling.");
            npc.CurrentAction = new NpcAction(ActionKind.Idle, null, $"Planted {crop} in {bed.Label}.");
            return;
        }

        if (bed.Lifecycle == CropLifecycleState.Dead)
        {
            bed.Growth = 0;
            bed.Lifecycle = CropLifecycleState.Empty;
            bed.LifecycleChangedAt = state.Elapsed;
            CrewTaskSystem.Succeed(state, npc, $"Cleared dead crop; {bed.Label} returned to Empty.");
            npc.CurrentAction = new NpcAction(ActionKind.Idle, null, $"Cleared dead crop from {bed.Label}.");
            return;
        }

        const double PerVisitLimit = 45;
        var water = Math.Min(Math.Min(state.Stores.Water, PerVisitLimit), 100 - bed.Water);
        var feed = Math.Min(Math.Min(state.Stores.Nutrients, PerVisitLimit), 100 - bed.Nutrients);

        bed.Water += water;
        bed.Nutrients += feed;
        state.Stores.Water -= water;
        state.Stores.Nutrients -= feed;

        CrewTaskSystem.Succeed(state, npc, $"Tended {bed.Label}; water {bed.Water:0}%, nutrients {bed.Nutrients:0}%.");
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, $"Tended {bed.Label}.");
    }

    private static void ConsumeTypedProduce(StationStores stores, double amount)
    {
        var remaining = amount;
        foreach (var crop in stores.RawCrops
                     .Where(pair => pair.Key != CropKind.Tobacco && pair.Value > 0)
                     .OrderBy(pair => pair.Key)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            if (remaining <= 0) break;
            var used = Math.Min(remaining, stores.RawCrops[crop]);
            stores.RawCrops[crop] -= used;
            remaining -= used;
        }
    }

    private static bool Qualified(Npc npc, MaintenanceDiscipline discipline) =>
        StationUpkeepRules.SkillOf(npc, discipline) >= 25;

    /// <summary>
    /// Somebody hungry goes and eats rather than starting a chore — but only if
    /// there is anything to eat. Gating on hunger alone deadlocked the station:
    /// too hungry to cook, so no food, so hungrier still.
    /// </summary>
    private static bool IsAvailable(GameState state, Npc npc) =>
        npc.IsAlive
        && npc.IsPresent
        && npc.ServicingDeviceId is null
        && npc.ProvisioningJob is null
        && (npc.Hunger < StationProvisionRules.HungryAt || !state.Stores.HasMeal)
        && (npc.Intent is null || npc.Intent.Urgency < ProtectedUrgency);

    private bool CanReach(GameState state, Npc npc, string roomId) =>
        npc.CurrentRoomId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
        || _navigation.FindPathForCrew(state, npc, npc.CurrentRoomId, roomId).Count > 0;

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
