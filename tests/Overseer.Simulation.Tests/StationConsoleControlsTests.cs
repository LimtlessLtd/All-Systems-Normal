using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Pause, crew roster, station log, reset confirmation and the station-first
/// default layout must exist in the shared station console.
/// </summary>
public sealed class StationConsoleControlsTests
{
    private static readonly string[] HomePages =
    [
        "src/Overseer.Web.UI/Pages/Home.razor"
    ];

    private const string LayoutScript = "src/Overseer.Web.UI/wwwroot/layout.js";

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
    public void Console_ExposesPauseRosterLogAndMenu()
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
    public void LayoutScript_SupportsPauseFitAndFocus()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, LayoutScript));

        Assert.Contains("registerPauseHandler", source);
        Assert.Contains("function fitCamera", source);
        Assert.Contains("function focusEntity", source);
        Assert.Contains("event.code === \"Space\"", source);
    }

    [Fact]
    public void StationHeader_WrapsControlsInsteadOfCoveringTheTitle()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root, "src/Overseer.Web.UI/Pages/Home.razor.css"));

        // Right-aligned controls on a line that cannot wrap slide over the
        // title whenever they do not fit (most widths below ~1650px).
        Assert.Contains(".station-command-header {\n    flex-wrap: wrap;", css.Replace("\r\n", "\n"));
        Assert.Contains(".station-command-header .station-title-compact {\n    flex: 0 0 auto;", css.Replace("\r\n", "\n"));
        Assert.Contains(".station-command-header .station-header-actions {\n    flex: 1 1 340px;\n    flex-wrap: wrap;", css.Replace("\r\n", "\n"));
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
