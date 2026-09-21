namespace Overseer.Simulation.Tests;

public sealed class DebugTelemetryUiTests
{
    [Fact]
    public void MirroredDebugPages_ShowLlmRequestAndResponseOnlyOnDebugSurface()
    {
        var root = FindRepositoryRoot();
        var serverDebug = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web", "Components", "Pages", "Debug.razor"));
        var clientDebug = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.Client", "Pages", "Debug.razor"));
        var serverHome = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web", "Components", "Pages", "Home.razor"));
        var clientHome = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.Client", "Pages", "Home.razor"));

        foreach (var debug in new[] { serverDebug, clientDebug })
        {
            Assert.Contains("LLM REQUEST // PROMPT + AFFORDANCES + OBSERVATIONS", debug);
            Assert.Contains("LLM RESPONSE // RAW MODEL OUTPUT", debug);
        }

        foreach (var home in new[] { serverHome, clientHome })
        {
            Assert.DoesNotContain("LLM REQUEST // PROMPT", home);
            Assert.DoesNotContain("LLM RESPONSE // RAW MODEL OUTPUT", home);
        }

        Assert.Equal(serverDebug, clientDebug);
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
