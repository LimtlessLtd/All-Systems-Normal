using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationUiPresentationPolishTests
{
    [Fact]
    public void SeededStations_KeepFixturesInsideRoomsWithoutOverlap()
    {
        var states = RepresentativeStates(6);

        foreach (var state in states)
        {
            foreach (var room in state.Facility.Rooms.Values)
            {
                for (var firstIndex = 0; firstIndex < room.Fixtures.Count; firstIndex++)
                {
                    var first = room.Fixtures[firstIndex];
                    Assert.InRange(first.X - (first.Width / 2), 0.99, 99);
                    Assert.InRange(first.X + (first.Width / 2), 1, 99.01);
                    Assert.InRange(first.Y - (first.Height / 2), 0.99, 99);
                    Assert.InRange(first.Y + (first.Height / 2), 1, 99.01);

                    for (var secondIndex = firstIndex + 1; secondIndex < room.Fixtures.Count; secondIndex++)
                    {
                        var second = room.Fixtures[secondIndex];
                        Assert.False(
                            Overlaps(first, second),
                            $"Seed {state.StationGeneration?.Seed}, room {room.Id}: " +
                            $"'{first.Label}' overlaps '{second.Label}'.");
                    }
                }
            }
        }
    }

    [Fact]
    public void SeededStations_LeaveVisibleSeparationAroundMajorFloorMachinery()
    {
        foreach (var state in RepresentativeStates(6))
        {
            foreach (var room in state.Facility.Rooms.Values)
            {
                var floorMachinery = room.Fixtures.Where(NeedsFloorAisle).ToList();
                for (var firstIndex = 0; firstIndex < floorMachinery.Count; firstIndex++)
                {
                    for (var secondIndex = firstIndex + 1; secondIndex < floorMachinery.Count; secondIndex++)
                    {
                        Assert.False(
                            OverlapsWithPadding(floorMachinery[firstIndex], floorMachinery[secondIndex], .75),
                            $"Seed {state.StationGeneration?.Seed}, room {room.Id}: " +
                            $"'{floorMachinery[firstIndex].Label}' and '{floorMachinery[secondIndex].Label}' are packed together.");
                    }
                }
            }
        }
    }

    [Fact]
    public void SeededStations_KeepStandingInteractionPointsClearOfOtherPhysicalFixtures()
    {
        foreach (var state in RepresentativeStates(6))
        {
            foreach (var room in state.Facility.Rooms.Values)
            {
                foreach (var fixture in room.Fixtures.Where(item =>
                             item.UsePose == FixtureUsePose.Stand
                             && item.InteractionX is not null
                             && item.InteractionY is not null))
                {
                    foreach (var other in room.Fixtures.Where(item =>
                                 !ReferenceEquals(item, fixture)
                                 && IsPhysicalObstacle(item)))
                    {
                        Assert.False(
                            ContainsPoint(other, fixture.InteractionX!.Value, fixture.InteractionY!.Value, 0),
                            $"Seed {state.StationGeneration?.Seed}, room {room.Id}: interaction point for " +
                            $"'{fixture.Label}' is blocked by '{other.Label}'.");
                    }
                }
            }
        }
    }

    [Fact]
    public void Hydroponics_ExposeDistinctCropTypesAndLiveGrowthStages()
    {
        var root = FindRepositoryRoot();
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor.css"));

        Assert.Contains("data-crop=\"@CropFixtureLabel(room, fixture)\"", home);
        Assert.Contains("data-growth-stage=\"@CropGrowthStage(room, fixture)\"", home);
        Assert.Contains("\"empty\"", home);
        Assert.Contains("\"planting\"", home);
        Assert.Contains("\"seedling\"", home);
        Assert.Contains("\"maturing\"", home);
        Assert.Contains("\"harvest-ready\"", home);
        Assert.Contains("\"harvesting\"", home);
        Assert.Contains("\"dead\"", home);

        foreach (var crop in new[] { "TOMATO", "POTATO", "APPLE", "GRAPE", "BANANA", "TOBACCO", "WHEAT" })
        {
            Assert.Contains($"data-crop=\"{crop}\"", css);
        }

        Assert.Contains("data-growth-stage=\"harvest-ready\"", css);
        Assert.Contains("var(--crop-symbol)", css);
    }

    [Fact]
    public void FireAndSmokePresentationScalesWithAuthoritativeSeverity()
    {
        var root = FindRepositoryRoot();
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor.css"));

        foreach (var severity in new[]
                 {
                     "fire-minor", "fire-growing", "fire-severe", "fire-inferno",
                     "smoke-light", "smoke-building", "smoke-heavy", "smoke-blackout"
                 })
        {
            Assert.Contains(severity, home);
            Assert.Contains($".room-node.{severity}", css);
        }

        Assert.Contains(".room-node.has-smoke::before", css);
        Assert.Contains("compartment-smoke-drift", css);
        Assert.Contains("content: \"🔥  🔥  🔥  🔥\"", css);
    }

    [Fact]
    public void LightingClimateAndAirHandlerControls_HugRoomEdges()
    {
        var states = RepresentativeStates(4);
        var inspected = 0;

        foreach (var state in states)
        {
            foreach (var fixture in state.Facility.Rooms.Values.SelectMany(room => room.Fixtures))
            {
                if (fixture.DeviceId is not { } deviceId
                    || !(deviceId.StartsWith("lighting:", StringComparison.OrdinalIgnoreCase)
                         || deviceId.StartsWith("climate:", StringComparison.OrdinalIgnoreCase)
                         || deviceId.StartsWith("ventilation:", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                inspected++;
                var nearestEdge = new[]
                {
                    fixture.X - (fixture.Width / 2),
                    100 - (fixture.X + (fixture.Width / 2)),
                    fixture.Y - (fixture.Height / 2),
                    100 - (fixture.Y + (fixture.Height / 2))
                }.Min();

                Assert.True(
                    nearestEdge <= 8.1,
                    $"{deviceId} should be wall/edge mounted, but nearest edge was {nearestEdge:0.0}%.");
            }
        }

        Assert.True(inspected >= 12, "Expected representative stations to expose physical light/climate/air-handler controls.");
    }

    [Fact]
    public void RoomCallouts_AlwaysAttachToTopOrBottomEvenWhenCrowded()
    {
        var clear = new Facility();
        clear.Rooms["control"] = new Room
        {
            Id = "control",
            Name = "Control",
            Type = RoomType.ControlRoom,
            MapX = 50,
            MapY = 50,
            MapWidth = 12,
            MapHeight = 10
        };

        var attached = Assert.Single(StationRoomCalloutSystem.Build(clear));
        Assert.False(attached.IsExternal);
        Assert.Contains(attached.Side, new[] { "top", "bottom" });
        Assert.InRange(
            Math.Sqrt(
                Math.Pow(attached.LabelX - attached.AnchorX, 2)
                + Math.Pow(attached.LabelY - attached.AnchorY, 2)),
            .8,
            3);

        var crowded = new Facility();
        crowded.Rooms["control"] = clear.Rooms["control"];
        crowded.Rooms["left"] = Corridor("left", 35, 50, 20, 24);
        crowded.Rooms["right"] = Corridor("right", 65, 50, 20, 24);
        crowded.Rooms["top"] = Corridor("top", 50, 35, 24, 20);
        crowded.Rooms["bottom"] = Corridor("bottom", 50, 65, 24, 20);

        var crowdedCallout = Assert.Single(StationRoomCalloutSystem.Build(crowded));
        Assert.False(crowdedCallout.IsExternal);
        Assert.Contains(crowdedCallout.Side, new[] { "top", "bottom" });
    }

    [Fact]
    public void SharedUiContracts_KeepFocusPopupsMachineFamiliesAndFineZoom()
    {
        var root = FindRepositoryRoot();
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor.css"));
        var script = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "wwwroot", "layout.js"));

        Assert.Contains("station-focus-mode", home);
        Assert.Contains("station-messages-popover", home);
        Assert.Contains("station-objectives-popover", home);
        Assert.Contains("fixture-system-", home);
        Assert.Contains(">MESSAGES</button>", home);
        Assert.Contains(">OBJECTIVES</button>", home);
        Assert.Contains("station-speed-controls", home);
        Assert.Contains("BloodStyle(blood)", home);
        Assert.Contains("hands-active", home);
        Assert.Contains("IsLocallyMoving", home);

        Assert.Contains(".workspace-shell.station-focus-mode", css);
        Assert.Contains(".fixture-system-lighting", css);
        Assert.Contains(".fixture-system-climate", css);
        Assert.Contains(".fixture-system-ventilation", css);
        Assert.Contains(".fixture-resurrectionchamber", css);
        Assert.Contains(".blood-evidence", css);
        Assert.Contains(".station-speed-controls", css);
        Assert.Contains("selected::after", css);

        Assert.Contains("minZoom: 0.15", script);
        Assert.Contains("zoomStep: 0.05", script);
        Assert.Contains("document.body.style.userSelect = \"none\"", script);
    }

    [Fact]
    public void ContainmentActivity_HasDedicatedMapAndInspectorPresentation()
    {
        var root = FindRepositoryRoot();
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor.css"));

        Assert.Contains("containment-breach", home);
        Assert.Contains("containment-at-large", home);
        Assert.Contains("containment-recapture", home);
        Assert.Contains("BREACH IN PROGRESS", home);
        Assert.Contains("RECAPTURE IN PROGRESS", home);
        Assert.Contains("IsContainmentBreachInProgress", home);
        Assert.Contains("HasEscapedContainment", home);

        Assert.Contains(".crew-token.containment-breach::before", css);
        Assert.Contains(".crew-token.containment-recapture::before", css);
        Assert.Contains(".containment-card.at-large", css);
        Assert.Contains(".containment-card.recapture-card", css);
    }

    private static List<GameState> RepresentativeStates(int count)
    {
        var states = new List<GameState>();

        for (var seed = 100; seed < 600 && states.Count < count; seed++)
        {
            try
            {
                states.Add(FacilitySeeder.CreateDefault(upkeepSeed: seed, stationSeed: seed));
            }
            catch (StationGenerationException)
            {
                // Procedural packing intentionally rejects some exact seeds.
            }
        }

        Assert.Equal(count, states.Count);
        return states;
    }

    private static Room Corridor(string id, double x, double y, double width, double height) =>
        new()
        {
            Id = id,
            Name = id,
            Type = RoomType.Corridor,
            MapX = x,
            MapY = y,
            MapWidth = width,
            MapHeight = height
        };

    private static bool NeedsFloorAisle(RoomFixture fixture) =>
        fixture.Type is FixtureType.Generator
            or FixtureType.ReactorCore
            or FixtureType.ResurrectionChamber
            or FixtureType.GrowBed
            or FixtureType.Bed
            or FixtureType.MedicalBed
            or FixtureType.OverseerShutdown
            or FixtureType.Workbench
            or FixtureType.TreatmentUnit
            or FixtureType.CapacitorBank
            or FixtureType.PowerBus
            or FixtureType.CoolantPump
            or FixtureType.WaterRecycler
            or FixtureType.OxygenGenerator
            or FixtureType.CarbonScrubber
            or FixtureType.NetworkRack;

    private static bool IsPhysicalObstacle(RoomFixture fixture) =>
        fixture.Type is not FixtureType.Camera
            and not FixtureType.Window
            and not FixtureType.Screen
            and not FixtureType.Mirror
            and not FixtureType.Pipe
            and not FixtureType.AirlockDoor;

    private static bool ContainsPoint(
        RoomFixture fixture,
        double x,
        double y,
        double clearance) =>
        x >= fixture.X - (fixture.Width / 2) - clearance
        && x <= fixture.X + (fixture.Width / 2) + clearance
        && y >= fixture.Y - (fixture.Height / 2) - clearance
        && y <= fixture.Y + (fixture.Height / 2) + clearance;

    private static bool OverlapsWithPadding(
        RoomFixture first,
        RoomFixture second,
        double padding)
    {
        var firstLeft = first.X - (first.Width / 2) - padding;
        var firstRight = first.X + (first.Width / 2) + padding;
        var firstTop = first.Y - (first.Height / 2) - padding;
        var firstBottom = first.Y + (first.Height / 2) + padding;
        var secondLeft = second.X - (second.Width / 2);
        var secondRight = second.X + (second.Width / 2);
        var secondTop = second.Y - (second.Height / 2);
        var secondBottom = second.Y + (second.Height / 2);

        return firstLeft < secondRight
            && firstRight > secondLeft
            && firstTop < secondBottom
            && firstBottom > secondTop;
    }

    private static bool Overlaps(RoomFixture first, RoomFixture second)
    {
        const double epsilon = .0001;
        var firstLeft = first.X - (first.Width / 2);
        var firstRight = first.X + (first.Width / 2);
        var firstTop = first.Y - (first.Height / 2);
        var firstBottom = first.Y + (first.Height / 2);
        var secondLeft = second.X - (second.Width / 2);
        var secondRight = second.X + (second.Width / 2);
        var secondTop = second.Y - (second.Height / 2);
        var secondBottom = second.Y + (second.Height / 2);

        return firstLeft < secondRight - epsilon
            && firstRight > secondLeft + epsilon
            && firstTop < secondBottom - epsilon
            && firstBottom > secondTop + epsilon;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
