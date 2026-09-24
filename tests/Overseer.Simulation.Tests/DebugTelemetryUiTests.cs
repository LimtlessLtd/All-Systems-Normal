namespace Overseer.Simulation.Tests;

public sealed class DebugTelemetryUiTests
{
    [Fact]
    public void DebugPage_ShowsLlmRequestAndResponseOnlyOnDebugSurface()
    {
        var root = FindRepositoryRoot();
        var debug = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Debug.razor"));
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));

        Assert.Contains("LLM REQUEST // EXACT PROMPT + OPTIONS", debug);
        Assert.Contains("LLM RESPONSE // RAW PROVIDER OUTPUT", debug);
        Assert.Contains("data-cognition-sequence=\"@trace.Sequence\"", debug);
        Assert.Contains("#@trace.Sequence", debug);
        Assert.Contains("OLLAMA PROVIDER STATUS", debug);
        Assert.Contains("runtime.ProviderRequestsStarted", debug);
        Assert.Contains("runtime.NpcDecisionRequestsStarted", debug);
        Assert.Contains("LAST PROVIDER ERROR", debug);
        Assert.Contains("COGNITION DISPATCH FAILURE", debug);

        var request = debug.IndexOf("LLM REQUEST // EXACT PROMPT + OPTIONS", StringComparison.Ordinal);
        var response = debug.IndexOf("LLM RESPONSE // RAW PROVIDER OUTPUT", StringComparison.Ordinal);
        Assert.True(request >= 0 && response > request, "Each cognition entry should present its request before its corresponding raw response.");

        Assert.DoesNotContain("LLM REQUEST // EXACT PROMPT", home);
        Assert.DoesNotContain("LLM RESPONSE // RAW PROVIDER OUTPUT", home);
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
