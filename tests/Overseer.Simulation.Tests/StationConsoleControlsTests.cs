using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Pause, crew roster, station log, reset confirmation and the station-first
/// default layout must exist in both mirrored runtimes.
/// </summary>
public sealed class StationConsoleControlsTests
{
    private static readonly string[] HomePages =
    [
        "src/Overseer.Web/Components/Pages/Home.razor",
        "src/Overseer.Web.Client/Pages/Home.razor"
    ];

    private static readonly string[] LayoutScripts =
    [
        "src/Overseer.Web/wwwroot/layout.js",
        "src/Overseer.Web.Client/wwwroot/layout.js"
    ];

    [Fact]
    public void StationLog_OmitsRoutineMovementButKeepsStoryEvents()
    {
        var log = new[]
        {
            "T+01:10: Sarah Chen heads for door-hall-kitchen-corridor en route to Kitchen Access.",
            "T+01:09: Sarah Chen crosses door-hall-kitchen-corridor from Habitat Spine to Kitchen Access.",
            "T+01:08: Marcus Reed and Emma Voss get into an argument.",
            "T+01:07: door-medical-hall-medical slides closed after crew traffic clears.",
            "T+01:06: FAULT: Kitchen lighting suffers an unexpected failure event (80% to 20%).",
            "T+01:05: Felix Ward opens door-a to pass through.",
            "T+01:04: CRITICAL: David Hale has died — Died from oxygen deprivation.",
            "T+01:03: Nadia Okafor gets on with their work.",
            "T+01:02: Nadia Okafor inspects local equipment.",
        };

        var notable = StationLogPresentation.Notable(log, 10);

        Assert.Equal(3, notable.Count);
        Assert.Contains(notable, entry => entry.Contains("argument", StringComparison.Ordinal));
        Assert.Equal("warning", StationLogPresentation.Severity(notable[0]));
        Assert.Equal("warning", StationLogPresentation.Severity(notable[1]));
        Assert.Equal("critical", StationLogPresentation.Severity(notable[2]));
    }

    [Fact]
    public void StationLog_RespectsTheLimitNewestFirst()
    {
        var log = Enumerable.Range(0, 20)
            .Select(index => $"T+00:{index:00}: Event {index}.")
            .ToList();

        var notable = StationLogPresentation.Notable(log, 5);

        Assert.Equal(log.Take(5), notable);
    }

    [Fact]
    public void BothRuntimes_ExposePauseRosterLogAndMenu()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in HomePages)
        {
            var source = File.ReadAllText(Path.Combine(root, relative));

            Assert.Contains("@onclick=\"TogglePause\"", source);
            Assert.Contains("[JSInvokable]", source);
            Assert.Contains("ToggleStationOverlay(\"crew\")", source);
            Assert.Contains("ToggleStationOverlay(\"log\")", source);
            Assert.Contains("ToggleStationOverlay(\"menu\")", source);
            Assert.Contains("StationLogPresentation.Notable", source);
            Assert.Contains("data-crew-id=\"@npc.Id\"", source);
            Assert.Contains("data-map-zoom-fit", source);
        }
    }

    [Fact]
    public void RunReset_RequiresConfirmationAndStationViewIsTheDefault()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in HomePages)
        {
            var source = File.ReadAllText(Path.Combine(root, relative));

            Assert.Contains("@onclick=\"RequestResetAsync\"", source);
            Assert.DoesNotContain("@onclick=\"ResetGame\"", source);
            Assert.Contains("private bool _stationFullscreen = true;", source);
        }
    }

    [Fact]
    public void LayoutScripts_StayMirroredAndSupportPauseFitAndFocus()
    {
        var root = FindRepositoryRoot();
        var sources = LayoutScripts
            .Select(relative => File.ReadAllText(Path.Combine(root, relative)))
            .ToList();

        Assert.Equal(sources[0], sources[1]);
        Assert.Contains("registerPauseHandler", sources[0]);
        Assert.Contains("function fitCamera", sources[0]);
        Assert.Contains("function focusEntity", sources[0]);
        Assert.Contains("event.code === \"Space\"", sources[0]);
    }

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
