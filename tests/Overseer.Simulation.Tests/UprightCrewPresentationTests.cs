namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #86: crew figures stay upright; facing is shown by mirroring
/// and a separate heading cue rather than by rotating the whole body.
/// </summary>
public sealed class UprightCrewPresentationTests
{
    [Fact]
    public void CrewStyle_KeepsTheBodyUprightAndCarriesHeadingSeparately()
    {
        var razor = ReadUi("Home.razor");

        Assert.Contains("--crew-facing:0deg;--crew-flip:{flip};--crew-heading:{heading:0.##}deg;", razor);
        Assert.DoesNotContain("--crew-facing:{facing", razor);
        Assert.Contains("class=\"crew-facing-cue\"", razor);
        Assert.Contains("class=\"person-orientation\"", razor);
    }

    [Fact]
    public void HeadingCue_RotatesWhileTheFigureOnlyMirrors()
    {
        var css = ReadUi("Home.razor.css");

        Assert.Contains("transform: scaleX(var(--crew-flip, 1));", css);
        Assert.Contains("rotate(var(--crew-heading, 0deg))", css);
        Assert.Contains(".crew-token.facing-away .person-visor", css);
    }

    private static string ReadUi(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "src",
            "Overseer.Web.UI",
            "Pages",
            file));
    }
}
