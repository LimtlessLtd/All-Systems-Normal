using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Builds the station's equipment register and wears it out.
///
/// The station is no longer a fixed board the player toggles. Every light,
/// camera, hatch, generator and grow bed is a real unit with a condition that
/// falls whether anybody is watching or not. A station left alone runs itself
/// down, which is what gives the crew work worth doing and gives Overseer
/// something to quietly stop them doing.
///
/// Failures are deliberately not things the player can simply switch back on:
/// a dead unit has to be physically serviced by somebody with the right skill.
/// </summary>
public sealed class StationUpkeepSystem
{
    /// <summary>Power drawn by an ordinary powered compartment.</summary>
    private const double RoomDemandKilowatts = 8;

    /// <summary>Corridors and access ways need lighting and little else.</summary>
    private const double CorridorDemandKilowatts = 3;

    /// <summary>Extra draw from a compartment running heavy equipment.</summary>
    private const double HeavyRoomDemandKilowatts = 6;

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Devices.Count == 0)
        {
            return;
        }

        var hours = delta.TotalHours;

        Wear(state, hours);
        UpdatePowerGrid(state, hours);
        ApplyFailures(state);
    }

    // ---------------------------------------------------------------- setup --

    /// <summary>
    /// Registers every maintainable unit on the station and gives each one a
    /// random starting condition and wear rate.
    ///
    /// Stations are not delivered new. A fresh game can legitimately hand the
    /// player a reactor already halfway through its life or a compartment whose
    /// lighting is about to go, and the crew will have to deal with it.
    /// </summary>
    public static void Register(GameState state, int seed)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Devices.Clear();
        state.UpkeepSeed = seed;
        var random = new Random(seed);

        foreach (var room in state.Facility.Rooms.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            Add(state, random, new StationDevice
            {
                Id = $"lighting:{room.Id}",
                Kind = StationSystemKind.Lighting,
                RoomId = room.Id,
                Label = $"{room.Name} lighting",
                Discipline = MaintenanceDiscipline.Electrical,
                WearPerHour = 0,
                DegradedAt = 30,
                ServiceDifficulty = 30
            });

            Add(state, random, new StationDevice
            {
                Id = $"camera:{room.Id}",
                Kind = StationSystemKind.Camera,
                RoomId = room.Id,
                Label = $"{room.Name} camera",
                Discipline = MaintenanceDiscipline.Electrical,
                WearPerHour = 0,
                DegradedAt = 25,
                ServiceDifficulty = 35
            });

            if (room.HasVentilationControl)
            {
                Add(state, random, new StationDevice
                {
                    Id = $"ventilation:{room.Id}",
                    Kind = StationSystemKind.Ventilation,
                    RoomId = room.Id,
                    Label = $"{room.Name} air handler",
                    Discipline = MaintenanceDiscipline.Mechanical,
                    WearPerHour = 0,
                    DegradedAt = 30,
                    ServiceDifficulty = 45
                });
            }

            if (room.HasTemperatureControl)
            {
                Add(state, random, new StationDevice
                {
                    Id = $"climate:{room.Id}",
                    Kind = StationSystemKind.ClimateControl,
                    RoomId = room.Id,
                    Label = $"{room.Name} climate unit",
                    Discipline = MaintenanceDiscipline.Mechanical,
                    WearPerHour = 0,
                    DegradedAt = 30,
                    ServiceDifficulty = 40
                });
            }

            if (room.HasExteriorHatch)
            {
                Add(state, random, new StationDevice
                {
                    Id = $"airlock:{room.Id}",
                    Kind = StationSystemKind.AirlockMechanism,
                    RoomId = room.Id,
                    Label = $"{room.Name} hatch mechanism",
                    Discipline = MaintenanceDiscipline.Mechanical,
                    WearPerHour = 0,
                    DegradedAt = 45,
                    ServiceDifficulty = 55
                });
            }

            switch (room.Type)
            {
                case RoomType.Generator:
                    Add(state, random, new StationDevice
                    {
                        Id = $"generator:{room.Id}",
                        Kind = StationSystemKind.PowerGenerator,
                        RoomId = room.Id,
                        Label = $"{room.Name} turbine",
                        FixtureLabel = "Generator A",
                        Discipline = MaintenanceDiscipline.Mechanical,
                        WearPerHour = 0,
                        DegradedAt = 50,
                        ServiceDifficulty = 55
                    });
                    break;

                case RoomType.Reactor:
                    Add(state, random, new StationDevice
                    {
                        Id = $"reactor:{room.Id}",
                        Kind = StationSystemKind.Reactor,
                        RoomId = room.Id,
                        Label = $"{room.Name} core",
                        FixtureLabel = "Reactor Core",
                        Discipline = MaintenanceDiscipline.Reactor,
                        WearPerHour = 0,
                        DegradedAt = 55,
                        ServiceDifficulty = 65
                    });
                    break;

                case RoomType.Hydroponics:
                    Add(state, random, new StationDevice
                    {
                        Id = $"growbeds:{room.Id}",
                        Kind = StationSystemKind.GrowBeds,
                        RoomId = room.Id,
                        Label = $"{room.Name} grow beds",
                        FixtureLabel = "Grow Bed A",
                        Discipline = MaintenanceDiscipline.Horticulture,
                        WearPerHour = 0,
                        DegradedAt = 40,
                        ServiceDifficulty = 35
                    });
                    break;

                case RoomType.Kitchen:
                    Add(state, random, new StationDevice
                    {
                        Id = $"galley:{room.Id}",
                        Kind = StationSystemKind.GalleyEquipment,
                        RoomId = room.Id,
                        Label = $"{room.Name} galley equipment",
                        FixtureLabel = "Galley Line",
                        Discipline = MaintenanceDiscipline.Galley,
                        WearPerHour = 0,
                        DegradedAt = 35,
                        ServiceDifficulty = 30
                    });
                    break;
            }
        }

        Add(state, random, new StationDevice
        {
            Id = "life-support:station",
            Kind = StationSystemKind.LifeSupport,
            RoomId = "engineering",
            Label = "Primary life support controller",
            FixtureLabel = "Systems Console",
            Discipline = MaintenanceDiscipline.LifeSupport,
            WearPerHour = 0,
            DegradedAt = 50,
            ServiceDifficulty = 60
        });

        Add(state, random, new StationDevice
        {
            Id = "distribution:station",
            Kind = StationSystemKind.PowerDistribution,
            RoomId = "engineering",
            Label = "Main switchboard",
            FixtureLabel = "Main Switchboard",
            Discipline = MaintenanceDiscipline.Electrical,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 62
        });

        Add(state, random, new StationDevice
        {
            Id = "capacitor:station",
            Kind = StationSystemKind.CapacitorBank,
            RoomId = "engineering",
            Label = "Pulse capacitor bank",
            FixtureLabel = "Pulse Capacitor Bank",
            Discipline = MaintenanceDiscipline.Electrical,
            WearPerHour = 0,
            DegradedAt = 40,
            ServiceDifficulty = 56
        });

        Add(state, random, new StationDevice
        {
            Id = "oxygen:station",
            Kind = StationSystemKind.OxygenGenerator,
            RoomId = "engineering",
            Label = "Oxygen generator",
            FixtureLabel = "Oxygen Generator",
            Discipline = MaintenanceDiscipline.LifeSupport,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 58
        });

        Add(state, random, new StationDevice
        {
            Id = "scrubber:station",
            Kind = StationSystemKind.CarbonScrubber,
            RoomId = "engineering",
            Label = "CO2 scrubber rack",
            FixtureLabel = "CO₂ Scrubber Rack",
            Discipline = MaintenanceDiscipline.LifeSupport,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 58
        });

        Add(state, random, new StationDevice
        {
            Id = "thermal:station",
            Kind = StationSystemKind.ThermalLoop,
            RoomId = "engineering",
            Label = "Thermal control loop",
            FixtureLabel = "Thermal Control Loop",
            Discipline = MaintenanceDiscipline.Mechanical,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 55
        });

        Add(state, random, new StationDevice
        {
            Id = "coolant:reactor",
            Kind = StationSystemKind.CoolantPump,
            RoomId = "reactor",
            Label = "Primary reactor coolant pump",
            FixtureLabel = "Primary Coolant Pump",
            Discipline = MaintenanceDiscipline.Reactor,
            WearPerHour = 0,
            DegradedAt = 50,
            ServiceDifficulty = 64
        });

        foreach (var door in state.Facility.Doors.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            Add(state, random, new StationDevice
            {
                Id = $"door:{door.Id}",
                Kind = StationSystemKind.Door,
                RoomId = DoorServiceRoomId(state.Facility, door),
                DoorId = door.Id,
                Label = $"{door.Id} actuator",
                FixtureLabel = $"{door.Id} Local Control",
                Discipline = MaintenanceDiscipline.Mechanical,
                WearPerHour = 0,
                DegradedAt = 30,
                ServiceDifficulty = 45
            });
        }

        foreach (var mechanism in state.ShutdownMechanisms)
        {
            Add(state, random, new StationDevice
            {
                Id = $"isolation:{mechanism.Id}",
                Kind = StationSystemKind.IsolationMechanism,
                RoomId = mechanism.RoomId,
                Label = mechanism.Label,
                Discipline = MaintenanceDiscipline.Electrical,
                WearPerHour = 0,
                DegradedAt = 40,
                ServiceDifficulty = 60
            });
        }
    }

    /// <summary>
    /// Gives a newly registered unit its wear rate and starting condition.
    ///
    /// Most equipment starts serviceable, but roughly one unit in six comes
    /// already worn and about one in twenty is close to failing. A run can begin
    /// with a genuine maintenance backlog purely from bad luck, which is the
    /// point — the crew have real work waiting for them before Overseer does
    /// anything at all.
    /// </summary>
    private static void Add(GameState state, Random random, StationDevice template)
    {
        var baseWear = template.Kind switch
        {
            StationSystemKind.Reactor => 0.55,
            StationSystemKind.PowerGenerator => 0.70,
            StationSystemKind.LifeSupport => 0.45,
            StationSystemKind.AirlockMechanism => 0.30,
            StationSystemKind.GrowBeds => 0.65,
            StationSystemKind.GalleyEquipment => 0.50,
            StationSystemKind.Door => 0.22,
            StationSystemKind.ClimateControl => 0.32,
            StationSystemKind.Ventilation => 0.34,
            StationSystemKind.Lighting => 0.28,
            StationSystemKind.Camera => 0.24,
            StationSystemKind.PowerDistribution => 0.42,
            StationSystemKind.CapacitorBank => 0.30,
            StationSystemKind.CoolantPump => 0.48,
            StationSystemKind.OxygenGenerator => 0.40,
            StationSystemKind.CarbonScrubber => 0.42,
            StationSystemKind.ThermalLoop => 0.38,
            StationSystemKind.IsolationMechanism => 0.18,
            _ => 0.3
        };

        // Individual units are better or worse than the spec sheet.
        var wear = baseWear * (0.6 + (random.NextDouble() * 0.9));

        var roll = random.NextDouble();
        var condition = roll switch
        {
            < 0.05 => 6 + (random.NextDouble() * 18),    // nearly gone
            < 0.20 => 30 + (random.NextDouble() * 25),   // a known problem
            < 0.55 => 60 + (random.NextDouble() * 25),   // used
            _ => 85 + (random.NextDouble() * 15)         // serviceable
        };

        // Critical backbone machinery may begin worn, but a new run should not
        // randomly boot into a total station-wide electrical/atmosphere death
        // spiral before anybody can react. It can still degrade and fail later.
        condition = template.Kind switch
        {
            StationSystemKind.PowerDistribution => Math.Max(condition, 62),
            StationSystemKind.CapacitorBank => Math.Max(condition, 50),
            StationSystemKind.CoolantPump => Math.Max(condition, 55),
            StationSystemKind.OxygenGenerator
                or StationSystemKind.CarbonScrubber
                or StationSystemKind.ThermalLoop => Math.Max(condition, 50),
            _ => condition
        };

        var device = new StationDevice
        {
            Id = template.Id,
            Kind = template.Kind,
            RoomId = template.RoomId,
            DoorId = template.DoorId,
            Label = template.Label,
            Discipline = template.Discipline,
            WearPerHour = wear,
            DegradedAt = template.DegradedAt,
            ServiceDifficulty = template.ServiceDifficulty,
            FixtureLabel = template.FixtureLabel,
            Condition = Math.Round(condition, 1)
        };

        state.Devices[device.Id] = device;
    }

    // ----------------------------------------------------------------- wear --

    private static void Wear(GameState state, double hours)
    {
        foreach (var device in state.Devices.Values)
        {
            if (device.IsFailed)
            {
                continue;
            }

            var rate = device.WearPerHour;

            // Equipment in an unpowered compartment is not running, so it is not
            // wearing either. Cutting power to a room preserves its kit — and
            // stops the crew servicing it.
            if (state.Facility.Rooms.TryGetValue(device.RoomId, out var room)
                && !room.IsPowered
                && device.Kind is not StationSystemKind.Reactor
                    and not StationSystemKind.PowerGenerator
                    and not StationSystemKind.LifeSupport)
            {
                rate *= 0.15;
            }

            // Generation runs harder when it is carrying the station alone.
            if (device.Kind is StationSystemKind.Reactor or StationSystemKind.PowerGenerator
                && state.Power.LoadPercent > 85)
            {
                rate *= 1.6;
            }

            device.Condition = Math.Clamp(device.Condition - (rate * hours), 0, 100);
        }
    }

    // ---------------------------------------------------------------- power --

    private const double DoorActuatorDemandKilowatts = 1.1;
    private const double LifeSupportDemandKilowatts = 28;
    private const double ControlBusDemandKilowatts = 8;

    private static void UpdatePowerGrid(GameState state, double hours)
    {
        var rawGeneration = state.Devices.Values
            .Where(device => device.Kind is StationSystemKind.Reactor
                or StationSystemKind.PowerGenerator)
            .Sum(device => CurrentOutputKilowatts(state, device));

        var distribution = state.Devices.GetValueOrDefault("distribution:station");
        var distributionEfficiency = DeviceEfficiency(distribution, failedFloor: 0.12);
        state.Power.DistributionCapacityKilowatts = 340 * distributionEfficiency;

        var generation = Math.Min(rawGeneration, state.Power.DistributionCapacityKilowatts);

        var roomDemand = state.Facility.Rooms.Values
            .Where(room => room.IsPowered)
            .Sum(Demand);

        var fixedDemand =
            (state.Facility.Doors.Count * DoorActuatorDemandKilowatts)
            + (state.LifeSupport.IsOnline ? LifeSupportDemandKilowatts : 0)
            + ControlBusDemandKilowatts;

        var requested = roomDemand + fixedDemand;
        state.Power.RequestedKilowatts = requested;
        state.Power.GenerationKilowatts = generation;

        var capacitor = state.Devices.GetValueOrDefault("capacitor:station");
        var capacitorEfficiency = DeviceEfficiency(capacitor, failedFloor: 0);
        state.Power.CapacitorCapacityKwh = 48 * capacitorEfficiency;
        state.Power.CapacitorChargeKwh = Math.Clamp(
            state.Power.CapacitorChargeKwh,
            0,
            state.Power.CapacitorCapacityKwh);
        state.Power.CapacitorFlowKilowatts = 0;

        var supply = generation;

        if (requested > generation
            && hours > 0
            && state.Power.CapacitorChargeKwh > 0.001
            && capacitorEfficiency > 0)
        {
            var deficit = requested - generation;
            var energyLimited = state.Power.CapacitorChargeKwh / hours;
            var discharge = Math.Min(
                deficit,
                Math.Min(72 * capacitorEfficiency, energyLimited));

            state.Power.CapacitorChargeKwh = Math.Max(
                0,
                state.Power.CapacitorChargeKwh - (discharge * hours));
            state.Power.CapacitorFlowKilowatts = discharge;
            supply += discharge;
        }
        else if (generation > requested
                 && hours > 0
                 && capacitorEfficiency > 0
                 && state.Power.CapacitorChargeKwh
                    < state.Power.CapacitorCapacityKwh - 0.001)
        {
            var surplus = generation - requested;
            var remainingEnergy =
                state.Power.CapacitorCapacityKwh - state.Power.CapacitorChargeKwh;
            var energyLimited = remainingEnergy / hours;
            var charge = Math.Min(
                surplus,
                Math.Min(45 * capacitorEfficiency, energyLimited));

            state.Power.CapacitorChargeKwh = Math.Min(
                state.Power.CapacitorCapacityKwh,
                state.Power.CapacitorChargeKwh + (charge * hours));
            state.Power.CapacitorFlowKilowatts = -charge;
        }

        state.Power.SupplyKilowatts = supply;
        state.Power.DemandKilowatts = requested;
        state.Power.QualityPercent = requested <= 0
            ? 100
            : Math.Clamp((supply / requested) * 100, 0, 100);

        ApplyPowerQuality(state);
        ShedLoad(state);
    }

    /// <summary>
    /// What one compartment draws. Heavy rooms include pumps, medical loads or
    /// agricultural lighting beyond the base hotel load.
    /// </summary>
    private static double Demand(Room room) =>
        (room.Type == RoomType.Corridor
            ? CorridorDemandKilowatts
            : RoomDemandKilowatts)
        + (room.Type is RoomType.Reactor or RoomType.Generator
            or RoomType.Hydroponics or RoomType.Medical
                ? HeavyRoomDemandKilowatts
                : 0);

    public static double CurrentOutputKilowatts(
        GameState state,
        StationDevice device)
    {
        if (device.IsFailed
            || device.Kind is not (StationSystemKind.Reactor
                or StationSystemKind.PowerGenerator))
        {
            return 0;
        }

        var rated = device.Kind == StationSystemKind.Reactor ? 200.0 : 95.0;
        var efficiency = DeviceEfficiency(device, failedFloor: 0);

        if (device.Kind == StationSystemKind.Reactor)
        {
            var coolant = state.Devices.GetValueOrDefault("coolant:reactor");
            var coolantEfficiency = DeviceEfficiency(coolant, failedFloor: 0.18);

            // A weak coolant loop forces an automatic reactor derate. It does
            // not magically explode; crew get a degraded station to repair.
            efficiency *= 0.25 + (0.75 * coolantEfficiency);
        }

        return rated * efficiency;
    }

    private static double DeviceEfficiency(
        StationDevice? device,
        double failedFloor)
    {
        if (device is null)
            return 1;

        if (device.IsFailed)
            return failedFloor;

        if (!device.IsDegraded)
            return 1;

        return Math.Clamp(
            device.Condition / Math.Max(1, device.DegradedAt),
            Math.Max(0, failedFloor),
            1);
    }

    private static void ApplyPowerQuality(GameState state)
    {
        var quality = state.Power.QualityPercent;

        foreach (var room in state.Facility.Rooms.Values)
        {
            var priority = Priority(room);

            room.LightingPowerAvailable = room.IsPowered
                && (quality >= 96
                    || priority >= 7
                    || (quality >= 84 && priority >= 4));

            room.CameraPowerAvailable = room.IsPowered
                && (quality >= 91
                    || priority >= 8
                    || (quality >= 76 && priority >= 6));
        }

        foreach (var door in state.Facility.Doors)
        {
            var a = state.Facility.Rooms[door.RoomAId];
            var b = state.Facility.Rooms[door.RoomBId];
            var highestPriority = Math.Max(Priority(a), Priority(b));

            door.GridPowerAvailable = quality switch
            {
                >= 72 => true,
                >= 54 => highestPriority >= 7,
                >= 38 => IsCritical(a) || IsCritical(b),
                _ => false
            };
        }

        // Life support is on the essential bus, below doors and normal room
        // loads. Only a serious generation/distribution collapse takes it out.
        state.LifeSupport.PowerAvailable =
            quality >= 30
            && state.Power.SupplyKilowatts >= LifeSupportDemandKilowatts;
    }

    /// <summary>
    /// When generation cannot carry the station, compartments are dropped in
    /// reverse priority until it can. Electrical quality effects happen first,
    /// so users see flickering auxiliaries and unavailable hatch actuators
    /// before whole rooms go dark.
    /// </summary>
    private static void ShedLoad(GameState state)
    {
        var restored = new List<string>();

        foreach (var roomId in state.Power.SheddedRoomIds.ToList())
        {
            if (!state.Facility.Rooms.TryGetValue(roomId, out var room))
            {
                state.Power.SheddedRoomIds.Remove(roomId);
                continue;
            }

            if (state.Power.SupplyKilowatts - state.Power.DemandKilowatts < Demand(room))
            {
                break;
            }

            room.IsPowered = true;
            state.Power.DemandKilowatts += Demand(room);
            state.Power.SheddedRoomIds.Remove(roomId);
            restored.Add(room.Name);
        }

        if (restored.Count > 0)
        {
            Log(state, $"GRID: power restored to {string.Join(", ", restored)}.");
        }

        var shed = new List<string>();

        while (state.Power.IsBrownedOut)
        {
            var candidate = state.Facility.Rooms.Values
                .Where(room => room.IsPowered && !IsCritical(room))
                .OrderBy(room => Priority(room))
                .ThenBy(room => room.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (candidate is null)
                break;

            candidate.IsPowered = false;
            candidate.LightingPowerAvailable = false;
            candidate.CameraPowerAvailable = false;
            state.Power.SheddedRoomIds.Add(candidate.Id);
            state.Power.DemandKilowatts -= Demand(candidate);
            shed.Add(candidate.Name);
        }

        if (shed.Count > 0)
        {
            AudioCueSystem.Emit(state, AudioCueKind.Critical);
            Log(
                state,
                $"GRID: insufficient generation. Load shed from {string.Join(", ", shed)}.");
        }
    }

    private static bool IsCritical(Room room) =>
        room.Type is RoomType.Reactor
            or RoomType.Generator
            or RoomType.Engineering
            or RoomType.Corridor;

    private static int Priority(Room room) => room.Type switch
    {
        RoomType.Recreation => 0,
        RoomType.Washroom => 1,
        RoomType.Storage => 2,
        RoomType.CrewQuarters => 3,
        RoomType.Kitchen => 4,
        RoomType.Hydroponics => 5,
        RoomType.Engineering => 8,
        RoomType.Medical => 8,
        RoomType.ControlRoom => 9,
        RoomType.Reactor or RoomType.Generator or RoomType.Corridor => 10,
        _ => 6
    };

    // ------------------------------------------------------------- failures --

    /// <summary>
    /// Turns equipment condition into things the crew and the player can see.
    /// A failed unit forces its function off and takes it out of Overseer's
    /// hands until somebody repairs it.
    /// </summary>
    private static void ApplyFailures(GameState state)
    {
        foreach (var device in state.Devices.Values)
        {
            if (!state.Facility.Rooms.TryGetValue(device.RoomId, out var room))
            {
                continue;
            }

            switch (device.Kind)
            {
                case StationSystemKind.Lighting when device.IsFailed:
                    room.LightsOn = false;
                    break;

                case StationSystemKind.Camera when device.IsFailed:
                    room.CameraOnline = false;
                    break;

                case StationSystemKind.ClimateControl:
                    room.IsTemperatureAiControllable = !device.IsFailed;
                    if (device.IsFailed)
                    {
                        room.TemperatureControlOnline = false;
                    }

                    break;

                case StationSystemKind.Ventilation:
                    room.IsVentilationAiControllable = !device.IsFailed;
                    if (device.IsFailed)
                    {
                        room.VentilationEnabled = false;
                    }

                    break;

                case StationSystemKind.Door when device.DoorId is { } doorId:
                {
                    var door = state.Facility.Doors.FirstOrDefault(d => d.Id == doorId);

                    if (door is null)
                    {
                        break;
                    }

                    door.StructuralIntegrityPercent = (int)Math.Round(device.Condition);
                    door.IsDamaged = device.IsDegraded;
                    door.IsPowered = !device.IsFailed;

                    // A dead actuator is a manual/mechanical problem. Overseer
                    // loses it and ordinary powered opening is unavailable until
                    // somebody gets a tool on it.
                    if (device.IsFailed)
                    {
                        door.IsAiControllable = false;
                    }

                    break;
                }

                case StationSystemKind.LifeSupport:
                    state.LifeSupport.IsAiControllable = !device.IsFailed;

                    if (device.IsFailed)
                    {
                        state.LifeSupport.IsOnline = false;
                    }

                    break;

                case StationSystemKind.OxygenGenerator:
                    state.LifeSupport.OxygenGenerationPercent = device.IsFailed
                        ? 0
                        : Math.Clamp(device.Condition, 5, 100);
                    break;

                case StationSystemKind.CarbonScrubber:
                    state.LifeSupport.ScrubberEfficiencyPercent = device.IsFailed
                        ? 0
                        : Math.Clamp(device.Condition, 5, 100);
                    break;

                case StationSystemKind.ThermalLoop:
                    state.LifeSupport.ThermalLoopEfficiencyPercent = device.IsFailed
                        ? 0
                        : Math.Clamp(device.Condition, 5, 100);
                    break;

                case StationSystemKind.AirlockMechanism:
                    room.IsExteriorHatchAiControllable = !device.IsFailed;
                    room.IsAirlockSafetyAiControllable = !device.IsFailed;
                    break;

                case StationSystemKind.IsolationMechanism:
                {
                    var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
                        device.Id.Equals(
                            $"isolation:{m.Id}",
                            StringComparison.OrdinalIgnoreCase));

                    // A failed isolation switch is one the crew cannot use — a
                    // reprieve Overseer did nothing to earn.
                    if (mechanism is not null)
                    {
                        mechanism.IsOnline = !device.IsFailed;
                    }

                    break;
                }
            }
        }
    }

    private static string DoorServiceRoomId(Facility facility, Door door)
    {
        var roomA = facility.Rooms[door.RoomAId];
        var roomB = facility.Rooms[door.RoomBId];

        var aIsTinyConnector = roomA.Type == RoomType.Corridor
            && roomA.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase);
        var bIsTinyConnector = roomB.Type == RoomType.Corridor
            && roomB.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase);

        if (aIsTinyConnector && !bIsTinyConnector)
            return roomB.Id;

        if (bIsTinyConnector && !aIsTinyConnector)
            return roomA.Id;

        return roomA.Id;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
