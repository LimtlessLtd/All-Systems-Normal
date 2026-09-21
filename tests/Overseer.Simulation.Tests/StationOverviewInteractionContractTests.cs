using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationOverviewInteractionContractTests
{
    [Fact]
    public void EveryMaintainableDevice_HasAPhysicalInspectableFixture()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 420042);

        foreach (var device in state.Devices.Values)
        {
            var fixtures = state.Facility.Rooms.Values
                .SelectMany(room => room.Fixtures)
                .Where(fixture => string.Equals(
                    fixture.DeviceId,
                    device.Id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(
                fixtures.Count > 0,
                $"{device.Id} ({device.Kind}) has no bound physical fixture.");
        }

        foreach (var doorDevice in state.Devices.Values.Where(device =>
                     device.Kind == StationSystemKind.Door))
        {
            Assert.True(
                state.Facility.Rooms.Values
                    .SelectMany(room => room.Fixtures)
                    .Count(fixture =>
                        fixture.Type == FixtureType.DoorConsole
                        && string.Equals(
                            fixture.DeviceId,
                            doorDevice.Id,
                            StringComparison.OrdinalIgnoreCase)) >= 2,
                $"{doorDevice.Id} does not have local controls on both sides.");
        }
    }

    [Fact]
    public void DeviceSelection_UsesTheUniversalInspectorContract()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 420043);
        var device = state.Devices["power-bus:engineering"];
        var selection = new StationSelection(
            StationSelectionKind.Device,
            device.Id);

        Assert.True(StationInspectionSystem.Exists(state, selection));
        Assert.Same(device, StationInspectionSystem.Device(state, selection));
    }

    [Fact]
    public void LossOfGeneration_ShedsLoadsDepowersDoorsAndStopsLifeSupport()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 420044);
        MakeEquipmentHealthy(state);

        state.Devices.Values
            .Where(device => device.Kind is
                StationSystemKind.Reactor or StationSystemKind.PowerGenerator)
            .ToList()
            .ForEach(device => device.IsEnabled = false);

        state.Devices["capacitor-bank:engineering"].IsEnabled = false;
        state.Power.StoredKilowattHours = 0;

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.NotEmpty(state.Power.SheddedRoomIds);
        Assert.False(state.LifeSupport.IsOnline);
        Assert.Contains(state.Facility.Doors, door => !door.IsPowered);
        Assert.True(
            state.Facility.Rooms.Values.Any(room =>
                !room.IsPowered && !IsCriticalPowerRoom(room)));
    }

    [Fact]
    public void CapacitorBank_BridgesAShortGenerationDeficitBeforeLoadShedding()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 420045);
        MakeEquipmentHealthy(state);

        var reactor = state.Devices.Values.Single(device =>
            device.Kind == StationSystemKind.Reactor);
        reactor.IsEnabled = false;

        var generator = state.Devices.Values.Single(device =>
            device.Kind == StationSystemKind.PowerGenerator);
        generator.Condition = 60;

        var capacitor = state.Devices["capacitor-bank:engineering"];
        capacitor.Condition = 100;
        capacitor.IsEnabled = true;
        state.Power.StoredKilowattHours = 18;

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(state.Power.BufferDischargeKilowatts > 0);
        Assert.True(state.Power.StoredKilowattHours < 18);
    }

    [Fact]
    public void FailedCoolantPump_DeratesGeneratorOutput()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 420046);
        MakeEquipmentHealthy(state);

        state.Devices.Values.Single(device =>
            device.Kind == StationSystemKind.Reactor).IsEnabled = false;

        var coolant = state.Devices["coolant-pump:generator"];
        coolant.IsEnabled = false;

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));
        var withoutCooling = state.Power.SupplyKilowatts;

        coolant.IsEnabled = true;
        coolant.Condition = 100;
        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));
        var withCooling = state.Power.SupplyKilowatts;

        Assert.True(
            withCooling > withoutCooling * 2,
            $"Expected coolant recovery to substantially raise output: {withoutCooling:0.0} -> {withCooling:0.0}.");
    }

    [Fact]
    public void ControlNetworkFailure_RemovesCameraReachabilityWithoutDestroyingLocalCameraState()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 420047);
        MakeEquipmentHealthy(state);

        var room = state.Facility.Rooms["control"];
        room.CameraOnline = true;
        state.Devices["network:control"].IsEnabled = false;

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.False(state.ControlNetworkOnline);
        Assert.True(room.CameraOnline);
        Assert.False(room.CameraNetworkReachable);
        Assert.False(room.HasVisualFeed);
    }

    [Fact]
    public void CameraPanning_MustNotCaptureInteractiveEntityPointers()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            "src/Overseer.Web/wwwroot/layout.js",
            "src/Overseer.Web.Client/wwwroot/layout.js"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            var guard = source.IndexOf(
                "if (isStationInteractiveTarget(event.target))",
                StringComparison.Ordinal);
            var capture = source.IndexOf(
                "viewport.setPointerCapture",
                StringComparison.Ordinal);

            Assert.True(guard >= 0, $"{relative} is missing the interactive target guard.");
            Assert.True(capture > guard, $"{relative} captures the pointer before checking interactive entities.");
        }
    }

    [Fact]
    public void StationOverview_ExposesEveryCoreEntityTypeThroughTheInspector()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            "src/Overseer.Web/Components/Pages/Home.razor",
            "src/Overseer.Web.Client/Pages/Home.razor"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));

            Assert.Contains("data-station-interactive", source);
            Assert.Contains("SelectRoom(", source);
            Assert.Contains("SelectCrew(", source);
            Assert.Contains("SelectDoor(", source);
            Assert.Contains("SelectRobot(", source);
            Assert.Contains("SelectTurret(", source);
            Assert.Contains("SelectDevice(", source);
            Assert.Contains("SelectedDevice is", source);
            Assert.Contains("fixture.DeviceId", source);

            var inspector = source.IndexOf(
                "<aside class=\"inspector-panel panel\">",
                StringComparison.Ordinal);
            var robot = source.IndexOf(
                "SelectedRobot is",
                StringComparison.Ordinal);

            Assert.True(inspector >= 0 && robot > inspector);
        }
    }

    [Fact]
    public void DoorPresentation_RetractsLeavesBelowMobileEntitiesAndHasDistinctStates()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            "src/Overseer.Web/Components/Pages/Home.razor.css",
            "src/Overseer.Web.Client/Pages/Home.razor.css"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));

            Assert.Contains(".map-door.open.door-horizontal .door-leaf-a", source);
            Assert.Contains("translateX(-145%)", source);
            Assert.Contains("translateY(-145%)", source);
            Assert.Contains(".map-door.closed .door-leaf", source);
            Assert.Contains(".map-door.locked .door-leaf", source);
            Assert.Contains("z-index: 12 !important", source);
            Assert.Contains("z-index: 30 !important", source);
        }
    }

    private static void MakeEquipmentHealthy(GameState state)
    {
        foreach (var device in state.Devices.Values)
        {
            device.Condition = 100;
            device.IsEnabled = true;
        }

        foreach (var room in state.Facility.Rooms.Values)
        {
            room.IsPowered = true;
            room.LightsOn = true;
            room.CameraOnline = true;
        }

        state.LifeSupport.RequestedOnline = true;
        state.Power.StoredKilowattHours = 16;
    }

    private static bool IsCriticalPowerRoom(Room room) =>
        room.Type is RoomType.Reactor or RoomType.Generator or RoomType.Corridor;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Overseer.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Overseer.slnx from test output.");
    }
}
