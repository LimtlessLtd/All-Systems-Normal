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
        ApplyUnexpectedFaults(state, delta);
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
                        Discipline = MaintenanceDiscipline.Mechanical,
                        WearPerHour = 0,
                        DegradedAt = 50,
                        ServiceDifficulty = 55,
                        RatedOutputKilowatts = 95
                    });
                    break;

                case RoomType.Reactor:
                    Add(state, random, new StationDevice
                    {
                        Id = $"reactor:{room.Id}",
                        Kind = StationSystemKind.Reactor,
                        RoomId = room.Id,
                        Label = $"{room.Name} core",
                        Discipline = MaintenanceDiscipline.Reactor,
                        WearPerHour = 0,
                        DegradedAt = 55,
                        ServiceDifficulty = 65,
                        RatedOutputKilowatts = 200
                    });
                    break;

                case RoomType.Hydroponics:
                    Add(state, random, new StationDevice
                    {
                        Id = $"growbeds:{room.Id}",
                        Kind = StationSystemKind.GrowBeds,
                        RoomId = room.Id,
                        Label = $"{room.Name} grow beds",
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
            Discipline = MaintenanceDiscipline.LifeSupport,
            WearPerHour = 0,
            DegradedAt = 50,
            ServiceDifficulty = 60,
            RatedDrawKilowatts = 14
        });

        Add(state, random, new StationDevice
        {
            Id = "power-bus:engineering",
            Kind = StationSystemKind.PowerDistributionBus,
            RoomId = "engineering",
            Label = "Main 440 V distribution bus",
            Discipline = MaintenanceDiscipline.Electrical,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 60,
            RatedDrawKilowatts = 2
        });

        Add(state, random, new StationDevice
        {
            Id = "capacitor-bank:engineering",
            Kind = StationSystemKind.CapacitorBank,
            RoomId = "engineering",
            Label = "Transient capacitor bank",
            Discipline = MaintenanceDiscipline.Electrical,
            WearPerHour = 0,
            DegradedAt = 40,
            ServiceDifficulty = 55,
            RatedDrawKilowatts = 1,
            StorageCapacityKwh = 18
        });

        Add(state, random, new StationDevice
        {
            Id = "oxygen-generator:engineering",
            Kind = StationSystemKind.OxygenGenerator,
            RoomId = "engineering",
            Label = "Oxygen electrolyser",
            Discipline = MaintenanceDiscipline.LifeSupport,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 58,
            RatedDrawKilowatts = 10
        });

        Add(state, random, new StationDevice
        {
            Id = "co2-scrubber:engineering",
            Kind = StationSystemKind.CarbonScrubber,
            RoomId = "engineering",
            Label = "CO₂ scrubber train",
            Discipline = MaintenanceDiscipline.LifeSupport,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 58,
            RatedDrawKilowatts = 8
        });

        Add(state, random, new StationDevice
        {
            Id = "water-recycler:engineering",
            Kind = StationSystemKind.WaterRecycler,
            RoomId = "engineering",
            Label = "Water recovery loop",
            Discipline = MaintenanceDiscipline.Utilities,
            WearPerHour = 0,
            DegradedAt = 40,
            ServiceDifficulty = 52,
            RatedDrawKilowatts = 7
        });

        Add(state, random, new StationDevice
        {
            Id = "network:control",
            Kind = StationSystemKind.DataNetwork,
            RoomId = "control",
            Label = "Station control network rack",
            Discipline = MaintenanceDiscipline.Electrical,
            WearPerHour = 0,
            DegradedAt = 35,
            ServiceDifficulty = 50,
            RatedDrawKilowatts = 4
        });

        Add(state, random, new StationDevice
        {
            Id = "coolant-pump:reactor",
            Kind = StationSystemKind.CoolantPump,
            RoomId = "reactor",
            Label = "Reactor primary coolant pump",
            Discipline = MaintenanceDiscipline.Mechanical,
            WearPerHour = 0,
            DegradedAt = 45,
            ServiceDifficulty = 62,
            RatedDrawKilowatts = 9
        });

        Add(state, random, new StationDevice
        {
            Id = "coolant-pump:generator",
            Kind = StationSystemKind.CoolantPump,
            RoomId = "generator",
            Label = "Generator cooling pump",
            Discipline = MaintenanceDiscipline.Mechanical,
            WearPerHour = 0,
            DegradedAt = 40,
            ServiceDifficulty = 48,
            RatedDrawKilowatts = 5
        });

        foreach (var door in state.Facility.Doors.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            Add(state, random, new StationDevice
            {
                Id = $"door:{door.Id}",
                Kind = StationSystemKind.Door,
                RoomId = door.RoomAId,
                DoorId = door.Id,
                // Crew say this label aloud and the Inspector shows it, so it
                // uses room names rather than the internal door ID.
                Label = $"{HatchName(state, door)} actuator",
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

        BindMachineFixtures(state);
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
            StationSystemKind.IsolationMechanism => 0.18,
            StationSystemKind.PowerDistributionBus => 0.18,
            StationSystemKind.CapacitorBank => 0.16,
            StationSystemKind.CoolantPump => 0.24,
            StationSystemKind.WaterRecycler => 0.20,
            StationSystemKind.OxygenGenerator => 0.22,
            StationSystemKind.CarbonScrubber => 0.21,
            StationSystemKind.DataNetwork => 0.14,
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

        if (template.Kind is StationSystemKind.PowerDistributionBus
            or StationSystemKind.CapacitorBank
            or StationSystemKind.CoolantPump)
        {
            condition = Math.Max(condition, 68 + (random.NextDouble() * 16));
        }

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
            Condition = Math.Round(condition, 1),
            RatedOutputKilowatts = template.RatedOutputKilowatts,
            RatedDrawKilowatts = template.RatedDrawKilowatts,
            StorageCapacityKwh = template.StorageCapacityKwh,
            IsEnabled = template.IsEnabled,
            IsAiControllable = template.IsAiControllable
        };

        state.Devices[device.Id] = device;
    }

    // ----------------------------------------------------------------- wear --

    private static void Wear(GameState state, double hours)
    {
        foreach (var device in state.Devices.Values)
        {
            if (!device.IsOperational)
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

    private static void ApplyUnexpectedFaults(GameState state, TimeSpan delta)
    {
        if (delta < TimeSpan.FromMinutes(1)
            || state.Elapsed < TimeSpan.FromMinutes(15))
        {
            return;
        }

        var currentSlot = (int)(state.Elapsed.TotalMinutes / 15);
        var previousSlot = (int)((state.Elapsed - delta).TotalMinutes / 15);
        if (currentSlot == previousSlot)
        {
            return;
        }

        foreach (var device in state.Devices.Values
                     .Where(device => device.IsOperational && device.Condition > 20)
                     .OrderBy(device => device.Id, StringComparer.Ordinal))
        {
            var roll = StableUnit($"{state.UpkeepSeed}:{currentSlot}:{device.Id}:fault");
            if (roll >= 0.00008)
            {
                continue;
            }

            var severity = 10
                + (StableUnit($"{state.UpkeepSeed}:{currentSlot}:{device.Id}:severity") * 24);
            var before = device.Condition;
            device.Condition = Math.Max(0, device.Condition - severity);

            AudioCueSystem.Emit(
                state,
                device.Condition <= 0.01 ? AudioCueKind.Critical : AudioCueKind.Warning,
                roomId: device.RoomId);

            Log(
                state,
                $"FAULT: {device.Label} suffers an unexpected failure event ({before:0}% to {device.Condition:0}%).");
        }
    }

    private static double StableUnit(string value) =>
        StableHash(value) / (double)int.MaxValue;

    // ---------------------------------------------------------------- power --

    /// <summary>
    /// Fills in the grid readings without applying any consequences (no load
    /// shedding, storage or dependency changes), so a freshly seeded station
    /// reports real supply and demand before its first turn instead of 0/0 kW.
    /// </summary>
    public static void RefreshPowerReadings(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Devices.Count == 0)
        {
            return;
        }

        MeasureGrid(state);
    }

    private static (double Supply, double Demand) MeasureGrid(GameState state)
    {
        var rawSupply = state.Devices.Values
            .Where(device => device.Kind is StationSystemKind.Reactor
                or StationSystemKind.PowerGenerator)
            .Sum(device => Output(state, device));

        var bus = Find(state, StationSystemKind.PowerDistributionBus);
        var busEfficiency = bus is null || !bus.IsOperational
            ? 0.18
            : bus.Condition >= bus.DegradedAt
                ? 1.0
                : Math.Clamp(0.68 + (0.32 * (bus.Condition / Math.Max(1, bus.DegradedAt))), 0.68, 1.0);

        state.Power.DistributionEfficiencyPercent = busEfficiency * 100;
        var supply = rawSupply * busEfficiency;

        var roomDemand = state.Facility.Rooms.Values
            .Where(room => room.IsPowered)
            .Sum(Demand);
        var deviceDemand = state.Devices.Values
            .Where(device => device.IsOperational)
            .Sum(device => DeviceDraw(state, device));

        var demand = roomDemand + deviceDemand;

        state.Power.SupplyKilowatts = supply;
        state.Power.DemandKilowatts = demand;
        return (supply, demand);
    }

    private static void UpdatePowerGrid(GameState state, double hours)
    {
        var (supply, demand) = MeasureGrid(state);
        state.Power.BufferDischargeKilowatts = 0;

        var capacitor = Find(state, StationSystemKind.CapacitorBank);
        state.Power.StorageCapacityKilowattHours = capacitor is null || !capacitor.IsOperational
            ? 0
            : Math.Max(1, capacitor.StorageCapacityKwh)
                * Math.Clamp(capacitor.Condition / 100d, 0.15, 1);

        state.Power.StoredKilowattHours = Math.Clamp(
            state.Power.StoredKilowattHours,
            0,
            state.Power.StorageCapacityKilowattHours);

        var deficit = demand - supply;
        if (deficit > 0.01
            && state.Power.StoredKilowattHours > 0.01
            && capacitor is { IsOperational: true })
        {
            var maxDischarge = 55d * Math.Clamp(capacitor.Condition / 100d, 0.2, 1);
            var energyLimitedKw = hours <= 0
                ? maxDischarge
                : state.Power.StoredKilowattHours / hours;
            var discharge = Math.Min(deficit, Math.Min(maxDischarge, energyLimitedKw));
            state.Power.BufferDischargeKilowatts = discharge;
            state.Power.StoredKilowattHours = Math.Max(
                0,
                state.Power.StoredKilowattHours - (discharge * hours));
        }
        else if (deficit < -0.01
                 && capacitor is { IsOperational: true }
                 && state.Power.StorageCapacityKilowattHours > state.Power.StoredKilowattHours)
        {
            var maxCharge = 35d * Math.Clamp(capacitor.Condition / 100d, 0.2, 1);
            var chargeKw = Math.Min(-deficit, maxCharge);
            state.Power.StoredKilowattHours = Math.Min(
                state.Power.StorageCapacityKilowattHours,
                state.Power.StoredKilowattHours + (chargeKw * hours));
        }

        ShedLoad(state);
        ApplyPoweredDependencies(state);
    }

    /// <summary>
    /// What one compartment draws. A healthy station must sit comfortably inside
    /// its generation: load shedding is meant to be a consequence of equipment
    /// failing, not the normal state of affairs. Shedding by default starved the
    /// crew, because the kitchen is one of the first compartments to be dropped.
    /// </summary>
    private static double Demand(Room room) =>
        (room.Type == RoomType.Corridor
            ? CorridorDemandKilowatts
            : RoomDemandKilowatts)
        + (room.Type is RoomType.Reactor or RoomType.Generator
            or RoomType.Hydroponics or RoomType.Medical
                ? HeavyRoomDemandKilowatts
                : 0);

    /// <summary>
    /// Everything a compartment draws while it is powered: the room itself plus
    /// the machines that go dark with it. Shedding and restoring must both count
    /// the machines, or a restored room overloads the grid on the next tick and
    /// is shed again, and shedding drops more rooms than the deficit needs.
    /// </summary>
    private static double SheddableLoad(GameState state, Room room) =>
        Demand(room)
        + state.Devices.Values
            .Where(device => device.RoomId == room.Id
                && device.IsOperational
                && device.RatedDrawKilowatts > 0
                && !DrawsWhileRoomUnpowered(device))
            .Sum(device => device.RatedDrawKilowatts);

    private static bool DrawsWhileRoomUnpowered(StationDevice device) =>
        device.Kind is StationSystemKind.LifeSupport
            or StationSystemKind.PowerDistributionBus
            or StationSystemKind.CapacitorBank;

    private static double Output(GameState state, StationDevice device)
    {
        if (!device.IsOperational)
        {
            return 0;
        }

        var rated = device.RatedOutputKilowatts > 0
            ? device.RatedOutputKilowatts
            : device.Kind == StationSystemKind.Reactor ? 200.0 : 95.0;

        var efficiency = device.Condition >= device.DegradedAt
            ? 1.0
            : Math.Clamp(device.Condition / Math.Max(1, device.DegradedAt), 0.15, 1.0);

        var coolant = device.Kind switch
        {
            StationSystemKind.Reactor => state.Devices.GetValueOrDefault("coolant-pump:reactor"),
            StationSystemKind.PowerGenerator => state.Devices.GetValueOrDefault("coolant-pump:generator"),
            _ => null
        };

        if (coolant is not null)
        {
            var coolingFactor = !coolant.IsOperational
                ? 0.28
                : coolant.Condition >= coolant.DegradedAt
                    ? 1.0
                    : Math.Clamp(coolant.Condition / Math.Max(1, coolant.DegradedAt), 0.45, 1.0);
            efficiency *= coolingFactor;
        }

        return rated * efficiency;
    }

    private static double DeviceDraw(GameState state, StationDevice device)
    {
        if (device.RatedDrawKilowatts <= 0)
        {
            return 0;
        }

        if (state.Facility.Rooms.TryGetValue(device.RoomId, out var room)
            && !room.IsPowered
            && !DrawsWhileRoomUnpowered(device))
        {
            return 0;
        }

        return device.RatedDrawKilowatts;
    }

    /// <summary>
    /// When generation cannot carry the station, compartments are dropped in
    /// reverse priority until it can. Life support, the reactor and the corridor
    /// are the last things to go.
    /// </summary>
    private static void ShedLoad(GameState state)
    {
        var restored = new List<string>();

        // Give power back first, so recovery is automatic once a generator is
        // serviced. The grid is machinery, not an Overseer decision.
        // Most important compartment first. SheddedRoomIds is a set, so its own
        // enumeration order says nothing about which room should come back first.
        foreach (var roomId in state.Power.SheddedRoomIds
                     .OrderByDescending(id => state.Facility.Rooms.TryGetValue(id, out var shedRoom)
                         ? Priority(shedRoom)
                         : int.MaxValue)
                     .ThenBy(id => id, StringComparer.Ordinal)
                     .ToList())
        {
            if (!state.Facility.Rooms.TryGetValue(roomId, out var room))
            {
                state.Power.SheddedRoomIds.Remove(roomId);
                continue;
            }

            var load = SheddableLoad(state, room);
            if ((state.Power.SupplyKilowatts + state.Power.BufferDischargeKilowatts)
                - state.Power.DemandKilowatts < load)
            {
                break;
            }

            room.IsPowered = true;
            state.Power.DemandKilowatts += load;
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
            {
                break;
            }

            candidate.IsPowered = false;
            candidate.LightsOn = false;
            candidate.CameraOnline = false;
            state.Power.SheddedRoomIds.Add(candidate.Id);
            state.Power.DemandKilowatts -= SheddableLoad(state, candidate);
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

    private static StationDevice? Find(GameState state, StationSystemKind kind) =>
        state.Devices.Values.FirstOrDefault(device => device.Kind == kind);

    private static void ApplyPoweredDependencies(GameState state)
    {
        foreach (var door in state.Facility.Doors)
        {
            var actuator = state.Devices.GetValueOrDefault($"door:{door.Id}");
            var aPowered = state.Facility.Rooms.TryGetValue(door.RoomAId, out var a) && a.IsPowered;
            var bPowered = state.Facility.Rooms.TryGetValue(door.RoomBId, out var b) && b.IsPowered;

            door.IsPowered = actuator is { IsOperational: true }
                && aPowered
                && bPowered
                && state.Power.DistributionEfficiencyPercent >= 22;
        }

        var network = Find(state, StationSystemKind.DataNetwork);
        var controlPowered = state.Facility.Rooms.TryGetValue("control", out var control)
            && control.IsPowered;
        state.ControlNetworkOnline = controlPowered
            && network is { IsOperational: true }
            && state.Power.DistributionEfficiencyPercent >= 18;

        foreach (var room in state.Facility.Rooms.Values)
            room.CameraNetworkReachable = state.ControlNetworkOnline;

        var engineeringPowered = state.Facility.Rooms.TryGetValue("engineering", out var engineering)
            && engineering.IsPowered;
        var core = Find(state, StationSystemKind.LifeSupport);
        var oxygen = Find(state, StationSystemKind.OxygenGenerator);
        var scrubber = Find(state, StationSystemKind.CarbonScrubber);
        var water = Find(state, StationSystemKind.WaterRecycler);

        state.LifeSupport.OxygenGeneratorOnline =
            engineeringPowered && oxygen is { IsOperational: true };
        state.LifeSupport.CarbonScrubberOnline =
            engineeringPowered && scrubber is { IsOperational: true };
        state.LifeSupport.WaterRecyclerOnline =
            engineeringPowered && water is { IsOperational: true };

        state.LifeSupport.ScrubberEfficiencyPercent = scrubber is null
            ? 0
            : Math.Clamp(scrubber.Condition, 0, 100);

        var utilitiesAvailable = engineeringPowered
            && core is { IsOperational: true }
            && state.Power.DistributionEfficiencyPercent >= 30;

        state.LifeSupport.IsAiControllable = utilitiesAvailable;
        state.LifeSupport.IsOnline =
            state.LifeSupport.RequestedOnline
            && utilitiesAvailable
            && state.LifeSupport.OxygenGeneratorOnline
            && state.LifeSupport.CarbonScrubberOnline;

        var coolantReactor = state.Devices.GetValueOrDefault("coolant-pump:reactor");
        if (state.Facility.Rooms.TryGetValue("reactor", out var reactorRoom)
            && coolantReactor is { IsOperational: false })
        {
            reactorRoom.TemperatureC = Math.Min(55, reactorRoom.TemperatureC + 0.25);
        }

        var coolantGenerator = state.Devices.GetValueOrDefault("coolant-pump:generator");
        if (state.Facility.Rooms.TryGetValue("generator", out var generatorRoom)
            && coolantGenerator is { IsOperational: false })
        {
            generatorRoom.TemperatureC = Math.Min(48, generatorRoom.TemperatureC + 0.16);
        }
    }

    private static bool IsCritical(Room room) =>
        room.Type is RoomType.Reactor or RoomType.Generator or RoomType.Corridor;

    private static int Priority(Room room) => room.Type switch
    {
        RoomType.Recreation => 0,
        RoomType.Washroom => 1,
        RoomType.Storage => 2,
        RoomType.CrewQuarters => 3,
        RoomType.Kitchen => 4,
        RoomType.Hydroponics => 5,
        RoomType.Medical => 6,
        RoomType.ControlRoom => 7,
        // Engineering houses the O2 generator, scrubbers and life-support core,
        // so it is the last compartment the grid lets go of.
        RoomType.Engineering => 9,
        _ => 8
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
                    if (!device.IsOperational)
                    {
                        room.TemperatureControlOnline = false;
                    }

                    break;

                case StationSystemKind.Ventilation:
                    room.IsVentilationAiControllable = !device.IsFailed;
                    if (!device.IsOperational)
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

                    // A dead actuator is a manual door. Overseer loses it until
                    // somebody gets a tool on it.
                    if (!device.IsOperational)
                    {
                        door.IsAiControllable = false;
                    }

                    break;
                }

                case StationSystemKind.LifeSupport:
                    state.LifeSupport.ScrubberEfficiencyPercent = Math.Clamp(
                        device.Condition,
                        10,
                        100);

                    state.LifeSupport.IsAiControllable = !device.IsFailed;

                    if (!device.IsOperational)
                    {
                        state.LifeSupport.IsOnline = false;
                    }

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


    private static void BindMachineFixtures(GameState state)
    {
        foreach (var device in state.Devices.Values.OrderBy(device => device.Id, StringComparer.Ordinal))
        {
            if (device.Kind == StationSystemKind.Door
                && device.DoorId is { } doorId
                && state.Facility.Doors.FirstOrDefault(door =>
                    door.Id.Equals(doorId, StringComparison.OrdinalIgnoreCase)) is { } door)
            {
                if (state.Facility.Rooms[door.RoomAId].Type != RoomType.Corridor)
                    AddDoorConsole(state, device, door.RoomAId, side: 0);
                if (state.Facility.Rooms[door.RoomBId].Type != RoomType.Corridor)
                    AddDoorConsole(state, device, door.RoomBId, side: 1);
                continue;
            }

            if (!state.Facility.Rooms.TryGetValue(device.RoomId, out var room))
            {
                continue;
            }

            if (room.Type == RoomType.Corridor
                && device.Kind != StationSystemKind.Camera)
            {
                continue;
            }

            var fixtureType = FixtureTypeFor(device.Kind);
            if (fixtureType is null)
            {
                continue;
            }

            var existingIndex = room.Fixtures.FindIndex(fixture =>
                fixture.DeviceId is null
                && FixtureMatchesDevice(fixture.Type, device.Kind));

            if (existingIndex >= 0)
            {
                var existing = room.Fixtures[existingIndex];
                room.Fixtures[existingIndex] = existing with
                {
                    Label = device.Label,
                    DeviceId = device.Id,
                    InteractionX = existing.InteractionX ?? existing.X,
                    InteractionY = existing.InteractionY ?? existing.Y
                };
                continue;
            }

            var ordinal = StableHash(device.Id);
            var x = 18 + (ordinal % 5) * 16;
            var y = 18 + ((ordinal / 7) % 4) * 18;
            // Utility hardware is visually substantial but must not consume
            // most of a compartment's walkable floor. Collision remains
            // authoritative; smaller footprints leave real circulation lanes
            // around the machines instead of forcing the packer to overlap or
            // shrink them only after the room is already saturated.
            var size = fixtureType.Value switch
            {
                FixtureType.CapacitorBank => (Width: 17d, Height: 14d),
                FixtureType.PowerBus => (Width: 22d, Height: 10d),
                FixtureType.CoolantPump => (Width: 14d, Height: 14d),
                FixtureType.WaterRecycler => (Width: 18d, Height: 17d),
                FixtureType.OxygenGenerator => (Width: 17d, Height: 17d),
                FixtureType.CarbonScrubber => (Width: 17d, Height: 17d),
                FixtureType.NetworkRack => (Width: 16d, Height: 18d),
                _ => (Width: 14d, Height: 13d)
            };

            room.Fixtures.Add(new RoomFixture(
                fixtureType.Value,
                device.Label,
                Math.Clamp(x, 12, 88),
                Math.Clamp(y, 12, 88),
                size.Width,
                size.Height,
                Math.Clamp(x, 12, 88),
                Math.Clamp(y + (size.Height / 2) + 5, 8, 92),
                FixtureUsePose.Stand,
                0,
                device.Id));
        }
    }

    private static void AddDoorConsole(
        GameState state,
        StationDevice device,
        string roomId,
        int side)
    {
        if (!state.Facility.Rooms.TryGetValue(roomId, out var room))
        {
            return;
        }

        var hash = StableHash($"{device.Id}:{roomId}");
        var x = side == 0 ? 9d : 91d;
        var y = 22d + (hash % 55);

        room.Fixtures.Add(new RoomFixture(
            FixtureType.DoorConsole,
            $"{device.Label} local control",
            x,
            y,
            9,
            12,
            side == 0 ? 15 : 85,
            y,
            FixtureUsePose.Stand,
            side == 0 ? 90 : 270,
            device.Id));
    }

    private static FixtureType? FixtureTypeFor(StationSystemKind kind) =>
        kind switch
        {
            StationSystemKind.PowerGenerator => FixtureType.Generator,
            StationSystemKind.Reactor => FixtureType.ReactorCore,
            StationSystemKind.Lighting => FixtureType.UtilityPanel,
            StationSystemKind.Camera => FixtureType.Camera,
            StationSystemKind.ClimateControl => FixtureType.UtilityPanel,
            StationSystemKind.Ventilation => FixtureType.Vent,
            StationSystemKind.AirlockMechanism => FixtureType.AirlockDoor,
            StationSystemKind.IsolationMechanism => FixtureType.OverseerShutdown,
            StationSystemKind.GrowBeds => FixtureType.GrowBed,
            StationSystemKind.GalleyEquipment => FixtureType.KitchenCounter,
            StationSystemKind.LifeSupport => FixtureType.Console,
            StationSystemKind.PowerDistributionBus => FixtureType.PowerBus,
            StationSystemKind.CapacitorBank => FixtureType.CapacitorBank,
            StationSystemKind.CoolantPump => FixtureType.CoolantPump,
            StationSystemKind.WaterRecycler => FixtureType.WaterRecycler,
            StationSystemKind.OxygenGenerator => FixtureType.OxygenGenerator,
            StationSystemKind.CarbonScrubber => FixtureType.CarbonScrubber,
            StationSystemKind.DataNetwork => FixtureType.NetworkRack,
            _ => null
        };

    private static bool FixtureMatchesDevice(FixtureType fixtureType, StationSystemKind kind) =>
        FixtureTypeFor(kind) == fixtureType
        || (kind == StationSystemKind.LifeSupport && fixtureType == FixtureType.Console)
        || (kind is StationSystemKind.ClimateControl or StationSystemKind.Lighting
            && fixtureType == FixtureType.UtilityPanel);

    private static string HatchName(GameState state, Door door)
    {
        var first = state.Facility.Rooms.TryGetValue(door.RoomAId, out var roomA)
            ? roomA.Name
            : door.RoomAId;
        var second = state.Facility.Rooms.TryGetValue(door.RoomBId, out var roomB)
            ? roomB.Name
            : door.RoomBId;

        return $"{first} / {second} hatch";
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)(hash & 0x7fffffff);
        }
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
