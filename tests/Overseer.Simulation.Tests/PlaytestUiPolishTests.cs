namespace Overseer.Simulation.Tests;

public sealed class PlaytestUiPolishTests
{
    [Fact]
    public void RunEndCard_StacksAboveTheStationFocusWorkspace()
    {
        // The focus-mode workspace is a fixed full-screen layer; a run-end card
        // beneath it left CONTINUE CAMPAIGN and the ending choices unreachable.
        foreach (var css in ReadMirroredCss())
        {
            Assert.True(
                ZIndexOf(css, ".run-end-overlay {") > ZIndexOf(css, ".workspace-shell.station-focus-mode {"),
                "The run-end overlay must stack above the station-focus workspace.");
        }
    }

    private static int ZIndexOf(string css, string selectorBlock)
    {
        var start = css.IndexOf(selectorBlock, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing CSS block {selectorBlock}");
        var body = css[start..css.IndexOf('}', start)];
        var match = System.Text.RegularExpressions.Regex.Match(body, @"z-index:\s*(\d+)");
        Assert.True(match.Success, $"{selectorBlock} declares no z-index");
        return int.Parse(match.Groups[1].Value);
    }

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
    public void MainPage_IsOnlyTheStationWorkspaceAndItsOverlays()
    {
        foreach (var home in ReadMirroredHomes())
        {
            Assert.DoesNotContain("<header class=\"command-bar\">", home);
            Assert.DoesNotContain("mission-directive-panel", home);
            Assert.DoesNotContain("<section class=\"panel directive-board\">", home);
            Assert.DoesNotContain("comms-primary", home);

            Assert.Contains("<div id=\"overseer-workspace\"", home);
            Assert.Contains("station-messages-popover", home);
            Assert.Contains("station-objectives-popover", home);
            Assert.Contains(">MESSAGES</button>", home);
            Assert.Contains(">OBJECTIVES</button>", home);
            Assert.Contains("station-menu-popover", home);
        }
    }

    [Fact]
    public void CrewMapNameplate_ShowsOnlyTheFullName()
    {
        foreach (var home in ReadMirroredHomes())
        {
            var nameplate = home.IndexOf("<span class=\"crew-nameplate ", StringComparison.Ordinal);
            Assert.True(nameplate >= 0);

            var end = home.IndexOf("</span>", nameplate + 1, StringComparison.Ordinal);
            var snippet = home[nameplate..(end + "</span>".Length)];

            Assert.Contains("@npc.Name", snippet);
            Assert.Contains("crew-full-name", snippet);
            Assert.DoesNotContain("Initials(", snippet);
            Assert.DoesNotContain("CrewActivityLabel", snippet);
            Assert.DoesNotContain("crew-motion-dot", snippet);
        }
    }

    [Fact]
    public void CrewMovement_ExposesDottedGreenIntentLineAndClosedWalkCycle()
    {
        foreach (var home in ReadMirroredHomes())
        {
            Assert.Contains("crew-route-lines", home);
            Assert.Contains("CrewNextMapPoint", home);
            Assert.Contains("LocalMovementSystem.PreviewDoorRoute", home);
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
    public void SelectedPathIsThickGreenAndDottedAndCrewDoesNotCssGlideBetweenWaypoints()
    {
        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains("stroke: #58b77a;", css);
            Assert.Contains("stroke-width: 3.2px;", css);
            Assert.Contains("stroke-linecap: round;", css);
            Assert.Contains("stroke-dasharray: 1px 8px;", css);
            Assert.DoesNotContain("left var(--crew-step-duration", css);
            Assert.DoesNotContain("top var(--crew-step-duration", css);
        }
    }

    [Fact]
    public void HostileControlsHaveExplicitContrastingBackgroundAndText()
    {
        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains(".robot-policy-controls .danger", css);
            Assert.Contains("background: var(--danger);", css);
            Assert.Contains("color: #180507;", css);
        }
    }

    [Fact]
    public void FireAndHydroponicsExposeStrongAuthoritativeVisualStates()
    {
        foreach (var home in ReadMirroredHomes())
        {
            Assert.Contains("data-crop-bed-id", home);
            Assert.Contains("SelectCropBed", home);
            Assert.Contains("growbed-active-lamp", home);
            Assert.Contains("crew-task-meter", home);
            Assert.Contains("CropLifecycleState.ReadyToHarvest", home);
            Assert.Contains("CropLifecycleState.Dead", home);
        }

        foreach (var css in ReadMirroredCss())
        {
            Assert.Contains(".room-node .fire-sprite", css);
            Assert.Contains(".room-node[data-room-id].has-smoke::before", css);
            Assert.Contains(".room-node[data-room-id].smoke-blackout::before", css);
            Assert.Contains("[data-growth-stage=\"seedling\"]", css);
            Assert.Contains("[data-growth-stage=\"maturing\"]", css);
            Assert.Contains("[data-growth-stage=\"harvest-ready\"][data-crop=\"TOMATO\"]", css);
            Assert.Contains("[data-growth-stage=\"dead\"]", css);
            Assert.Contains(".fixture-growbed[data-active=\"true\"] .growbed-active-lamp", css);
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
