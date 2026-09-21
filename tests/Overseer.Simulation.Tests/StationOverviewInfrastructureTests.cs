using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationOverviewInfrastructureTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void OverviewInfrastructure_IsBackedByInspectableAuthoritativeSystems()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);

        foreach (var room in state.Facility.Rooms.Values.Where(room =>
                     room.Type != RoomType.Corridor))
        {
            Assert.Contains(
                room.Fixtures,
                fixture => fixture.SystemId == $"room:{room.Id}");
        }

        var engineering = state.Facility.Rooms["engineering"];
        var expectedSystems = new[]
        {
            "distribution:station",
            "capacitor:station",
            "oxygen:station",
            "scrubber:station",
            "thermal:station"
        };

        foreach (var systemId in expectedSystems)
        {
            var fixture = Assert.Single(
                engineering.Fixtures,
                candidate => candidate.SystemId == systemId);

            var selection = new StationSelection(
                StationSelectionKind.Fixture,
                StationInspectionSystem.FixtureSelectionId(
                    engineering.Id,
                    fixture));

            var inspection = StationInspectionSystem.Fixture(state, selection);

            Assert.NotNull(inspection);
            Assert.Equal(systemId, inspection!.Fixture.SystemId);
            Assert.Equal(systemId, inspection.Device?.Id);
            Assert.True(StationInspectionSystem.Exists(state, selection));
        }

        var coolant = Assert.Single(
            state.Facility.Rooms["reactor"].Fixtures,
            fixture => fixture.SystemId == "coolant:reactor");
        Assert.Equal(
            "coolant:reactor",
            StationInspectionSystem.Fixture(
                state,
                new StationSelection(
                    StationSelectionKind.Fixture,
                    StationInspectionSystem.FixtureSelectionId(
                        "reactor",
                        coolant)))?.Device?.Id);

        var doorPanel = state.Facility.Rooms.Values
            .SelectMany(room => room.Fixtures.Select(fixture => (room, fixture)))
            .First(pair =>
                pair.fixture.SystemId?.StartsWith(
                    "door:",
                    StringComparison.OrdinalIgnoreCase) == true);
        var doorId = doorPanel.fixture.SystemId!["door:".Length..];
        var door = state.Facility.Doors.Single(candidate =>
            candidate.Id.Equals(doorId, StringComparison.OrdinalIgnoreCase));

        var doorInspection = StationInspectionSystem.Fixture(
            state,
            new StationSelection(
                StationSelectionKind.Fixture,
                StationInspectionSystem.FixtureSelectionId(
                    doorPanel.room.Id,
                    doorPanel.fixture)));

        Assert.Same(door, doorInspection?.Door);
        Assert.Equal(doorPanel.room.Id, doorInspection?.Device?.RoomId);
    }

    [Fact]
    public void MaintenanceCrew_WalkToTheActualAuthoritativeMachine()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = state.Crew.Single(candidate => candidate.Name == "Sarah Chen");
        var room = state.Facility.Rooms["engineering"];
        var fixture = Assert.Single(
            room.Fixtures,
            item => item.SystemId == "oxygen:station");

        npc.CurrentRoomId = room.Id;
        npc.PositionX = 82;
        npc.PositionY = 82;
        npc.Movement = null;
        npc.ServicingDeviceId = "oxygen:station";
        npc.CurrentAction = new NpcAction(
            ActionKind.Repair,
            room.Id,
            "Servicing the oxygen generator.");

        var targetX = fixture.InteractionX ?? fixture.X;
        var targetY = fixture.InteractionY ?? fixture.Y;
        var before = Distance(npc.PositionX, npc.PositionY, targetX, targetY);

        new LocalMovementSystem().Tick(state, Minute);

        var after = Distance(npc.PositionX, npc.PositionY, targetX, targetY);

        Assert.True(after < before);
    }

    [Fact]
    public void GenerationLoss_UsesCapacitorsBeforeCriticalSystemsLosePower()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var system = new StationUpkeepSystem();

        MakeInfrastructureHealthy(state);
        state.Power.CapacitorChargeKwh = 20;

        system.Tick(state, Minute);
        Assert.True(state.Power.QualityPercent > 90);
        Assert.True(state.LifeSupport.PowerAvailable);

        foreach (var device in state.Devices.Values.Where(device =>
                     device.Kind is StationSystemKind.Reactor
                         or StationSystemKind.PowerGenerator))
        {
            device.Condition = 0;
        }

        system.Tick(state, Minute);

        Assert.True(state.Power.CapacitorFlowKilowatts > 0);
        Assert.True(state.Power.SupplyKilowatts > 0);

        state.Power.CapacitorChargeKwh = 0;
        system.Tick(state, Minute);

        Assert.Equal(0, state.Power.SupplyKilowatts, 6);
        Assert.Equal(0, state.Power.QualityPercent, 6);
        Assert.False(state.LifeSupport.PowerAvailable);
        Assert.All(state.Facility.Doors, door =>
            Assert.False(door.GridPowerAvailable));
        Assert.Contains(
            state.Facility.Rooms.Values,
            room => !room.LightingPowerAvailable);

        var occupied = state.Facility.Rooms[
            state.Crew.First(npc => npc.IsAlive && npc.IsPresent).CurrentRoomId];
        var oxygenBefore = occupied.OxygenPercent;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(30));

        Assert.True(occupied.OxygenPercent < oxygenBefore);
    }

    [Fact]
    public void CoolantAndSwitchboardCondition_DerateRealPowerOutput()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        MakeInfrastructureHealthy(state);

        var reactor = state.Devices.Values.Single(device =>
            device.Kind == StationSystemKind.Reactor);
        var coolant = state.Devices["coolant:reactor"];

        var healthyOutput =
            StationUpkeepSystem.CurrentOutputKilowatts(state, reactor);

        coolant.Condition = 0;

        var deratedOutput =
            StationUpkeepSystem.CurrentOutputKilowatts(state, reactor);

        Assert.True(deratedOutput < healthyOutput * 0.5);

        coolant.Condition = 100;
        state.Devices["distribution:station"].Condition = 0;
        state.Power.CapacitorChargeKwh = 0;

        new StationUpkeepSystem().Tick(state, Minute);

        Assert.InRange(state.Power.GenerationKilowatts, 0, 41);
        Assert.True(state.Power.QualityPercent < 50);
        Assert.NotEmpty(state.Power.SheddedRoomIds);
    }

    [Fact]
    public void OverviewSourceContract_ProtectsClickableEntitiesAndSlidingDoors()
    {
        var root = RepositoryRoot();
        var serverJs = File.ReadAllText(
            Path.Combine(root, "src", "Overseer.Web", "wwwroot", "layout.js"));
        var clientJs = File.ReadAllText(
            Path.Combine(root, "src", "Overseer.Web.Client", "wwwroot", "layout.js"));
        var serverRazor = File.ReadAllText(
            Path.Combine(root, "src", "Overseer.Web", "Components", "Pages", "Home.razor"));
        var clientRazor = File.ReadAllText(
            Path.Combine(root, "src", "Overseer.Web.Client", "Pages", "Home.razor"));
        var serverCss = File.ReadAllText(
            Path.Combine(root, "src", "Overseer.Web", "Components", "Pages", "Home.razor.css"));
        var clientCss = File.ReadAllText(
            Path.Combine(root, "src", "Overseer.Web.Client", "Pages", "Home.razor.css"));

        Assert.Equal(serverJs, clientJs);
        Assert.Equal(serverCss, clientCss);

        var cameraPointerDown = serverJs.IndexOf(
            "viewport.addEventListener(\"pointerdown\"",
            StringComparison.Ordinal);
        var cameraGuard = serverJs.IndexOf(
            "event.target.closest(\"[data-station-interactable]\")",
            cameraPointerDown,
            StringComparison.Ordinal);
        var cameraCapture = serverJs.IndexOf(
            "viewport.setPointerCapture",
            cameraPointerDown,
            StringComparison.Ordinal);

        Assert.True(cameraPointerDown >= 0);
        Assert.True(cameraGuard > cameraPointerDown);
        Assert.True(cameraCapture > cameraGuard);

        foreach (var razor in new[] { serverRazor, clientRazor })
        {
            Assert.Contains("data-station-interactable", razor);
            Assert.Contains("SelectRoom(room.Id)", razor);
            Assert.Contains("SelectDoor(door.Id)", razor);
            Assert.Contains("SelectCrew(npc.Id)", razor);
            Assert.Contains("SelectRobot(mapRobot.Id)", razor);
            Assert.Contains("SelectTurret(mapTurret.Id)", razor);
            Assert.Contains("SelectFixture(room.Id, fixture)", razor);
            Assert.Equal(
                1,
                CountOccurrences(razor, "class=\"inspector-panel panel\""));
            Assert.DoesNotContain("robot-panel", razor, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("assets.science.nasa.gov", serverCss);
        Assert.Contains(".fixture-interactive", serverCss);
        Assert.Contains(".map-door.open.door-horizontal .door-leaf-a", serverCss);
        Assert.Contains("translateX(-138%)", serverCss);
        Assert.Contains(".station-authority-layer .crew-token", serverCss);
    }

    private static void MakeInfrastructureHealthy(GameState state)
    {
        foreach (var device in state.Devices.Values)
            device.Condition = 100;

        state.LifeSupport.IsOnline = true;
        state.LifeSupport.PowerAvailable = true;

        foreach (var room in state.Facility.Rooms.Values)
        {
            room.IsPowered = true;
            room.LightsOn = true;
            room.CameraOnline = true;
            room.LightingPowerAvailable = true;
            room.CameraPowerAvailable = true;
        }

        foreach (var door in state.Facility.Doors)
        {
            door.IsPowered = true;
            door.GridPowerAvailable = true;
        }
    }

    private static double Distance(
        double x1,
        double y1,
        double x2,
        double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate repository root from test output directory.");
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;

        while ((offset = value.IndexOf(
                    needle,
                    offset,
                    StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }
}
