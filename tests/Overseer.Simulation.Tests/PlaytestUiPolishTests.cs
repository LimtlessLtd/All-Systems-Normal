namespace Overseer.Simulation.Tests;

public sealed class PlaytestUiPolishTests
{
    [Fact]
    public void StationInteractiveButtons_NeverUseGenericPressedTransform()
    {
        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains(
                "button:not([data-station-interactive]):not(.room-node):not(.crew-token):not(.map-door):active:not(:disabled)",
                css);
            Assert.DoesNotContain(
                "button[data-station-interactive]:active",
                css);
        }
    }

    [Fact]
    public void RoomFloor_RemainsDarkTiledOnHoverAndSelection()
    {
        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains("--deck-grid:", css);
            Assert.Contains("#080a0b", css);
            Assert.Contains(
                ".station-authority-layer > .room-node:not(.hallway):not(.main-corridor):hover",
                css);

            var hover = css.IndexOf(
                ".station-authority-layer > .room-node:not(.hallway):not(.main-corridor):hover",
                StringComparison.Ordinal);
            var darkFloor = css.IndexOf("#080a0b", hover, StringComparison.Ordinal);
            Assert.True(darkFloor > hover, "Room hover state must retain the dark tiled floor.");
        }
    }

    [Fact]
    public void StationChromeAndObjectives_HaveClearVisualHierarchy()
    {
        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains(".mission-directive-panel", css);
            Assert.Contains(".objective-section-label", css);
            Assert.Contains(".directive-board", css);
            Assert.Contains(".station-panel .panel-heading", css);
            Assert.Contains("background: #d8dde0;", css);
        }

        foreach (var home in ReadMirroredHomes())
        {
            Assert.Contains("MISSION DIRECTIVE //", home);
            Assert.Contains("MISSION OBJECTIVES", home);
            Assert.Contains("THE CORPORATION // EXPERIMENT OVERSIGHT", home);
            Assert.Contains("CORPORATE DIRECTIVES", home);
            Assert.DoesNotContain(">SPONSOR", home, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OverseerComms_IsAboveStationWorkspace()
    {
        foreach (var home in ReadMirroredHomes())
        {
            var comms = home.IndexOf(
                "<section class=\"panel comms-panel comms-primary\">",
                StringComparison.Ordinal);
            var workspace = home.IndexOf(
                "<div id=\"overseer-workspace\"",
                StringComparison.Ordinal);

            Assert.True(comms >= 0, "Primary comms panel is missing.");
            Assert.True(workspace > comms, "Comms must render above the station workspace.");
        }
    }

    [Fact]
    public void CrewMovement_ExposesDottedGreenIntentLineAndClosedWalkCycle()
    {
        foreach (var home in ReadMirroredHomes())
        {
            Assert.Contains("crew-route-lines", home);
            Assert.Contains("CrewNextMapX", home);
            Assert.Contains("CrewNextMapY", home);
        }

        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains("stroke: #58b77a;", css);
            Assert.Contains("stroke-dasharray:", css);
            Assert.Contains("@keyframes crew-walk-cycle", css);
            Assert.Contains("0%, 100%", css);
        }
    }

    [Fact]
    public void RobotAndMachineryPresentation_IsTopDownAndLoopSafe()
    {
        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains(
                "transform: translate(-50%, -50%) rotate(var(--robot-facing, 0deg));",
                css);
            Assert.Contains("@keyframes pipe-flow", css);
            Assert.Contains("background-position-x: 18px;", css);
            Assert.Contains("@keyframes track-roll", css);
            Assert.Contains("background-position-y: 6px;", css);
            Assert.Contains("@keyframes console-status", css);
        }
    }

    private static IEnumerable<string> ReadMirroredHomes()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            "src/Overseer.Web.UI/Pages/Home.razor"
        })
        {
            yield return File.ReadAllText(Path.Combine(root, relative));
        }
    }

    private static IEnumerable<string> ReadMirroredCss()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            "src/Overseer.Web.UI/Pages/Home.razor.css"
        })
        {
            yield return File.ReadAllText(Path.Combine(root, relative));
        }
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
