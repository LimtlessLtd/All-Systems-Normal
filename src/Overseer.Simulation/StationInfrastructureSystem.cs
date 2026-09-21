using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Promotes visible station furniture into real interaction points. Existing
/// decorative furniture stays decorative; control panels and machinery carry a
/// SystemId that resolves to authoritative room/door/device state.
/// </summary>
public static class StationInfrastructureSystem
{
    public static void EnsureInteractiveFixtures(Facility facility)
    {
        ArgumentNullException.ThrowIfNull(facility);

        foreach (var room in facility.Rooms.Values
                     .Where(room => room.Type != RoomType.Corridor))
        {
            AddIfMissing(
                room,
                new RoomFixture(
                    FixtureType.RoomControlPanel,
                    $"{room.Name} Control Panel",
                    8,
                    50,
                    7,
                    14,
                    12,
                    50,
                    SystemId: $"room:{room.Id}"));

            BindFirst(
                facility,
                room.Id,
                fixture => fixture.Type == FixtureType.Camera,
                $"camera:{room.Id}");

            EnsureServiceFixture(
                room,
                $"lighting:{room.Id}",
                "Lighting Driver",
                FixtureType.UtilityPanel,
                8,
                25);

            if (room.HasVentilationControl)
            {
                EnsureServiceFixture(
                    room,
                    $"ventilation:{room.Id}",
                    "Air Handler Service",
                    FixtureType.Vent,
                    92,
                    28);
            }

            if (room.HasTemperatureControl)
            {
                EnsureServiceFixture(
                    room,
                    $"climate:{room.Id}",
                    "Climate Controller",
                    FixtureType.UtilityPanel,
                    92,
                    70);
            }

            if (room.HasExteriorHatch)
            {
                EnsureServiceFixture(
                    room,
                    $"airlock:{room.Id}",
                    "Pressure / Hatch Service",
                    FixtureType.UtilityPanel,
                    50,
                    88);
            }
        }

        BindFirst(
            facility,
            "engineering",
            fixture => fixture.Label.Equals(
                "Systems Console",
                StringComparison.OrdinalIgnoreCase),
            "life-support:station");

        BindFirst(
            facility,
            "generator",
            fixture => fixture.Type == FixtureType.Generator,
            "generator:generator");

        BindFirst(
            facility,
            "reactor",
            fixture => fixture.Type == FixtureType.ReactorCore,
            "reactor:reactor");

        BindFirst(
            facility,
            "hydroponics",
            fixture => fixture.Type == FixtureType.GrowBed,
            "growbeds:hydroponics");

        BindFirst(
            facility,
            "kitchen",
            fixture => fixture.Type == FixtureType.KitchenCounter,
            "galley:kitchen");

        AddSystemFixture(
            facility,
            "engineering",
            FixtureType.PowerPanel,
            "Main Switchboard",
            18,
            18,
            24,
            13,
            "distribution:station");

        AddSystemFixture(
            facility,
            "engineering",
            FixtureType.CapacitorBank,
            "Pulse Capacitor Bank",
            18,
            78,
            20,
            18,
            "capacitor:station");

        AddSystemFixture(
            facility,
            "engineering",
            FixtureType.OxygenGenerator,
            "Oxygen Generator",
            48,
            18,
            22,
            13,
            "oxygen:station");

        AddSystemFixture(
            facility,
            "engineering",
            FixtureType.CarbonScrubber,
            "CO₂ Scrubber Rack",
            72,
            18,
            22,
            13,
            "scrubber:station");

        AddSystemFixture(
            facility,
            "engineering",
            FixtureType.ThermalLoop,
            "Thermal Control Loop",
            82,
            76,
            20,
            18,
            "thermal:station");

        AddSystemFixture(
            facility,
            "reactor",
            FixtureType.CoolantPump,
            "Primary Coolant Pump",
            20,
            78,
            20,
            18,
            "coolant:reactor");

        foreach (var door in facility.Doors)
        {
            AddDoorPanel(facility, door, door.RoomAId);
            AddDoorPanel(facility, door, door.RoomBId);
        }
    }

    private static void BindFirst(
        Facility facility,
        string roomId,
        Func<RoomFixture, bool> predicate,
        string systemId)
    {
        if (!facility.Rooms.TryGetValue(roomId, out var room))
            return;

        var index = room.Fixtures.FindIndex(fixture =>
            predicate(fixture) && string.IsNullOrWhiteSpace(fixture.SystemId));

        if (index < 0)
            return;

        room.Fixtures[index] = room.Fixtures[index] with { SystemId = systemId };
    }

    private static void EnsureServiceFixture(
        Room room,
        string systemId,
        string label,
        FixtureType preferredType,
        double fallbackX,
        double fallbackY)
    {
        var index = room.Fixtures.FindIndex(fixture =>
            string.IsNullOrWhiteSpace(fixture.SystemId)
            && fixture.Type == preferredType);

        if (index >= 0)
        {
            room.Fixtures[index] = room.Fixtures[index] with
            {
                SystemId = systemId
            };
            return;
        }

        AddIfMissing(
            room,
            new RoomFixture(
                preferredType,
                label,
                fallbackX,
                fallbackY,
                8,
                10,
                fallbackX,
                fallbackY,
                SystemId: systemId));
    }

    private static void AddSystemFixture(
        Facility facility,
        string roomId,
        FixtureType type,
        string label,
        double x,
        double y,
        double width,
        double height,
        string systemId)
    {
        if (!facility.Rooms.TryGetValue(roomId, out var room))
            return;

        AddIfMissing(
            room,
            new RoomFixture(
                type,
                label,
                x,
                y,
                width,
                height,
                x,
                y,
                SystemId: systemId));
    }

    private static void AddDoorPanel(
        Facility facility,
        Door door,
        string roomId)
    {
        if (!facility.Rooms.TryGetValue(roomId, out var room))
            return;

        // Tiny connector passages do not need separate operator furniture.
        if (room.Type == RoomType.Corridor
            && room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            return;

        var otherId = door.RoomAId.Equals(roomId, StringComparison.OrdinalIgnoreCase)
            ? door.RoomBId
            : door.RoomAId;

        if (!facility.Rooms.TryGetValue(otherId, out var other))
            return;

        var portal = StationGeometry.FindSharedPortal(room, other);
        var localX = ToLocal(portal.X, room.MapX, room.MapWidth);
        var localY = ToLocal(portal.Y, room.MapY, room.MapHeight);

        double x;
        double y;
        double width;
        double height;

        if (portal.Wall == StationWall.Vertical)
        {
            x = portal.X >= room.MapX ? 92 : 8;
            y = Math.Clamp(localY + (localY < 50 ? 8 : -8), 14, 86);
            width = 7;
            height = 13;
        }
        else
        {
            x = Math.Clamp(localX + (localX < 50 ? 8 : -8), 14, 86);
            y = portal.Y >= room.MapY ? 92 : 8;
            width = 13;
            height = 7;
        }

        AddIfMissing(
            room,
            new RoomFixture(
                FixtureType.DoorControlPanel,
                $"{door.Id} Local Control",
                x,
                y,
                width,
                height,
                x,
                y,
                SystemId: $"door:{door.Id}"));
    }

    private static double ToLocal(double global, double center, double span) =>
        Math.Clamp(50 + (((global - center) / Math.Max(span, 0.001)) * 100), 0, 100);

    private static void AddIfMissing(Room room, RoomFixture fixture)
    {
        if (room.Fixtures.Any(existing =>
                existing.Label.Equals(fixture.Label, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(fixture.SystemId)
                    && existing.SystemId?.Equals(
                        fixture.SystemId,
                        StringComparison.OrdinalIgnoreCase) == true
                    && existing.Type == fixture.Type)))
        {
            return;
        }

        room.Fixtures.Add(fixture);
    }
}
