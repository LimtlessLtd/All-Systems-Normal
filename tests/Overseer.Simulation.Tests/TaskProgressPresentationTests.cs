namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #84: map task progress sits under the crew nameplate at the
/// nameplate's width only while work is running, and finished work reads
/// as not active in the Inspector.
/// </summary>
public sealed class TaskProgressPresentationTests
{
    [Fact]
    public void MapNameplate_RendersProgressOnlyForInProgressWork()
    {
        var razor = ReadUi("Home.razor");

        Assert.Contains("class=\"crew-nameplate-progress\"", razor);
        Assert.Contains("npc.ActiveTask is { Status: CrewTaskStatus.InProgress } task", razor);
    }

    [Fact]
    public void NameplateProgress_SpansTheNameplateWidthRegardlessOfDuration()
    {
        var css = ReadUi("Home.razor.css");
        var start = css.IndexOf(".crew-nameplate-progress {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var rule = css[start..css.IndexOf('}', start)];

        Assert.Contains("position: absolute;", rule);
        Assert.Contains("left: 0;", rule);
        Assert.Contains("right: 0;", rule);
        Assert.DoesNotContain("width:", rule);
    }

    [Fact]
    public void Inspector_LabelsEndedTasksAsNotActive()
    {
        var razor = ReadUi("Home.razor");

        Assert.Contains("CrewTaskStatus.Succeeded => \"COMPLETED · NOT ACTIVE\"", razor);
        Assert.Contains("CrewTaskStatus.Interrupted => \"INTERRUPTED · NOT ACTIVE\"", razor);
        Assert.Contains("CrewTaskStatus.Failed => \"FAILED · NOT ACTIVE\"", razor);
        Assert.Contains(".crew-task-progress-card:not(.status-inprogress)", ReadUi("Home.razor.css"));
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
