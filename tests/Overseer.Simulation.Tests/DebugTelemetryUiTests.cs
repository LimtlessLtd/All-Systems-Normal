namespace Overseer.Simulation.Tests;

public sealed class DebugTelemetryUiTests
{
    [Fact]
    public void DebugPage_ShowsLlmRequestAndResponseOnlyOnDebugSurface()
    {
        var root = FindRepositoryRoot();
        var debug = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Debug.razor"));
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));

        Assert.Contains("LLM REQUEST // PROMPT + AFFORDANCES + OBSERVATIONS", debug);
        Assert.Contains("LLM RESPONSE // RAW MODEL OUTPUT", debug);

        Assert.DoesNotContain("LLM REQUEST // PROMPT", home);
        Assert.DoesNotContain("LLM RESPONSE // RAW MODEL OUTPUT", home);
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
