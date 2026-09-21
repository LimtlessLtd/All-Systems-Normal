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
    public void RoomCallouts_AttachWhenClear_AndFallBackExternallyWhenCrowded()
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
        Assert.InRange(
            Math.Sqrt(
                Math.Pow(attached.LabelX - attached.AnchorX, 2)
                + Math.Pow(attached.LabelY - attached.AnchorY, 2)),
            2,
            7);

        var crowded = new Facility();
        crowded.Rooms["control"] = clear.Rooms["control"];
        crowded.Rooms["left"] = Corridor("left", 35, 50, 20, 24);
        crowded.Rooms["right"] = Corridor("right", 65, 50, 20, 24);
        crowded.Rooms["top"] = Corridor("top", 50, 35, 24, 20);
        crowded.Rooms["bottom"] = Corridor("bottom", 50, 65, 24, 20);

        var external = Assert.Single(StationRoomCalloutSystem.Build(crowded));
        Assert.True(external.IsExternal);
    }

    [Fact]
    public void MirroredUiContracts_KeepFocusPopupsMachineFamiliesAndFineZoom()
    {
        var root = FindRepositoryRoot();
        var serverHome = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web", "Components", "Pages", "Home.razor"));
        var clientHome = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.Client", "Pages", "Home.razor"));
        var serverCss = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web", "Components", "Pages", "Home.razor.css"));
        var clientCss = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.Client", "Pages", "Home.razor.css"));
        var serverJs = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web", "wwwroot", "layout.js"));
        var clientJs = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.Client", "wwwroot", "layout.js"));

        foreach (var home in new[] { serverHome, clientHome })
        {
            Assert.Contains("station-focus-mode", home);
            Assert.Contains("station-messages-popover", home);
            Assert.Contains("station-objectives-popover", home);
            Assert.Contains("fixture-system-", home);
            Assert.Contains(">MESSAGES</button>", home);
            Assert.Contains(">OBJECTIVES</button>", home);
        }

        Assert.Contains(".workspace-shell.station-focus-mode", serverCss);
        Assert.Contains(".fixture-system-lighting", serverCss);
        Assert.Contains(".fixture-system-climate", serverCss);
        Assert.Contains(".fixture-system-ventilation", serverCss);
        Assert.Equal(serverCss, clientCss);

        foreach (var script in new[] { serverJs, clientJs })
        {
            Assert.Contains("minZoom: 0.15", script);
            Assert.Contains("zoomStep: 0.05", script);
            Assert.Contains("document.body.style.userSelect = \"none\"", script);
        }

        Assert.Equal(serverJs, clientJs);
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
