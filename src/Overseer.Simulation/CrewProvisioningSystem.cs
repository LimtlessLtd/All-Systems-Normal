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
    public static void Plant(GameState state, int seed)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.CropBeds.Clear();
        var random = new Random(seed);

        foreach (var room in state.Facility.Rooms.Values
                     .Where(r => r.Type == RoomType.Hydroponics)
                     .OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            for (var index = 1; index <= 4; index++)
            {
                state.CropBeds.Add(new CropBed
                {
                    Id = $"bed:{room.Id}:{index}",
                    RoomId = room.Id,
                    Label = $"{room.Name} bed {index}",
                    Growth = Math.Round(random.NextDouble() * 85, 1),
                    Water = Math.Round(45 + (random.NextDouble() * 55), 1),
                    Nutrients = Math.Round(45 + (random.NextDouble() * 55), 1)
                });
            }
        }
    }

    // ---------------------------------------------------------------- crops --

    private static void GrowCrops(GameState state, double hours)
    {
        foreach (var bed in state.CropBeds)
        {
            if (bed.IsDead)
            {
                continue;
            }

            bed.Water = Math.Clamp(
                bed.Water - (StationProvisionRules.ConsumptionPerHour * hours),
                0,
                100);
            bed.Nutrients = Math.Clamp(
                bed.Nutrients - (StationProvisionRules.ConsumptionPerHour * hours),
                0,
                100);

            if (!state.Facility.Rooms.TryGetValue(bed.RoomId, out var room))
            {
                continue;
            }

            // Crops need light and working beds as much as they need water.
            var beds = state.Devices.Values.FirstOrDefault(device =>
                device.Kind == StationSystemKind.GrowBeds && device.RoomId == bed.RoomId);

            var equipment = beds is null || beds.IsFailed
                ? 0
                : Math.Clamp(beds.Condition / 100, 0.2, 1);

            var lit = room.IsPowered && room.LightsOn ? 1 : 0;
            var supplied = Math.Min(bed.Water, bed.Nutrients) > 0 ? 1 : 0;

            if (lit == 0 || supplied == 0 || equipment == 0)
            {
                // Starved of light, water or working equipment, a crop does not
                // simply pause. It starts dying.
                bed.Growth = Math.Max(0, bed.Growth - (1.5 * hours));

                if (bed.Growth <= 0 && bed.Water <= 0)
                {
                    bed.IsDead = true;
                }

                continue;
            }

            bed.Growth = Math.Clamp(
                bed.Growth + (StationProvisionRules.GrowthPerHour * equipment * hours),
                0,
                100);
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
            // Harvesting a ripe bed first: produce left standing rots and the
            // bed cannot be replanted.
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
            var thirsty = Unclaimed(state, bed => bed.TendUrgency >= 15)
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
        {
            return;
        }

        // Arriving somewhere clears the intent that brought you there, which
        // leaves the routine layer free to hand out something else. Only a
        // genuinely more important goal takes somebody off a chore; anything
        // lesser is dropped so the job in hand gets finished.
        if (npc.Intent is { } competing
            && competing.Action is not (ActionKind.TendCrops or ActionKind.Harvest or ActionKind.Cook))
        {
            if (competing.Urgency >= ProtectedUrgency)
            {
                ReleaseJob(npc);
                return;
            }

            npc.Intent = null;
        }

        if (!npc.CurrentRoomId.Equals(jobRoom, StringComparison.OrdinalIgnoreCase))
        {
            npc.ProvisioningCompletesAt = null;
            return;
        }

        if (!state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room) || !room.IsPowered)
        {
            // No power, no pumps and no ovens.
            npc.ProvisioningCompletesAt = null;
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
            npc.CurrentAction = new NpcAction(job, jobRoom, JobDescription(job));
            return;
        }

        if (state.Elapsed < npc.ProvisioningCompletesAt.Value)
        {
            return;
        }

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

    private static string JobDescription(ActionKind job) => job switch
    {
        ActionKind.Harvest => "Bringing in a crop.",
        ActionKind.Cook => "Running a meal service.",
        _ => "Watering and feeding the beds."
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

        if (bed is null || !bed.IsReadyToHarvest)
        {
            return;
        }

        state.Stores.Produce += StationProvisionRules.YieldPerHarvest;
        bed.Growth = 0;

        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, $"Brought in {bed.Label}.");
        Log(state, $"{npc.Name} harvests {bed.Label}. Produce store now {state.Stores.Produce:0}.");
    }

    private static void CompleteCooking(GameState state, Npc npc)
    {
        var galley = state.Devices.Values.FirstOrDefault(device =>
            device.Kind == StationSystemKind.GalleyEquipment
            && device.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (galley is null || galley.IsFailed)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                "The galley equipment is dead. Nothing can be cooked.");
            return;
        }

        if (state.Stores.Produce < StationProvisionRules.ProducePerCookingSession)
        {
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                "There is nothing left to cook with.");
            return;
        }

        state.Stores.Produce -= StationProvisionRules.ProducePerCookingSession;
        state.Stores.Meals += StationProvisionRules.MealsPerCookingSession;

        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Meal service is up.");
        Log(state, $"{npc.Name} prepares a meal service. {state.Stores.Meals:0} meals ready.");
    }

    private static void CompleteTending(GameState state, Npc npc)
    {
        var bed = state.CropBeds.FirstOrDefault(b => b.Id == npc.TendingBedId);

        if (bed is null)
        {
            return;
        }

        // A visit tops a bed up rather than filling it from empty, so one
        // thirsty bed cannot drain the whole reclaim loop in a single trip.
        const double PerVisitLimit = 45;

        var water = Math.Min(Math.Min(state.Stores.Water, PerVisitLimit), 100 - bed.Water);
        var feed = Math.Min(Math.Min(state.Stores.Nutrients, PerVisitLimit), 100 - bed.Nutrients);

        bed.Water += water;
        bed.Nutrients += feed;
        state.Stores.Water -= water;
        state.Stores.Nutrients -= feed;

        if (bed.IsDead && bed.Water > 50 && bed.Nutrients > 50)
        {
            bed.IsDead = false;
            bed.Growth = 0;
            Log(state, $"{npc.Name} replants {bed.Label}.");
        }

        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, $"Tended {bed.Label}.");
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
