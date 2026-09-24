using Microsoft.JSInterop;
using Overseer.Web.UI;

namespace Overseer.Simulation.Tests;

// Owner report 2026-09-24 19:56: running locally, the campaign restore in
// OnAfterRenderAsync threw TaskCanceledException from localStorage.getItem.
public sealed class BrowserInteropTests
{
    public static TheoryData<Exception> UnavailableBrowser => new()
    {
        new TaskCanceledException(),
        new JSDisconnectedException("circuit gone"),
        new JSException("QuotaExceededError")
    };

    [Theory]
    [MemberData(nameof(UnavailableBrowser))]
    public async Task AnUnavailableBrowser_IsReportedNotThrown(Exception failure)
    {
        var js = new ThrowingJsRuntime(failure);

        Assert.False(await BrowserInterop.TryInvokeVoidAsync(js, "localStorage.setItem", "k", "v"));
        Assert.Null(await BrowserInterop.TryInvokeAsync<string?>(js, "localStorage.getItem", "k"));
    }

    [Fact]
    public async Task AnAvailableBrowser_ReturnsItsValue()
    {
        var js = new ThrowingJsRuntime(failure: null);

        Assert.True(await BrowserInterop.TryInvokeVoidAsync(js, "localStorage.setItem", "k", "v"));
        Assert.Equal("stored", await BrowserInterop.TryInvokeAsync<string?>(js, "localStorage.getItem", "k"));
    }

    [Fact]
    public async Task AProgrammingError_IsNotSwallowed()
    {
        var js = new ThrowingJsRuntime(new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserInterop.TryInvokeAsync<string?>(js, "localStorage.getItem", "k"));
    }

    [Fact]
    public void HomeRoutesStorageAndAudioThroughTheBestEffortHelper()
    {
        var root = FindRepositoryRoot();
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));

        Assert.DoesNotContain("JS.InvokeVoidAsync(\"localStorage", home);
        Assert.DoesNotContain("JS.InvokeAsync<string?>(\n            \"localStorage", home);
        Assert.DoesNotContain("JS.InvokeVoidAsync(\n                \"overseerAudio.play\"", home);
        Assert.Equal(3, CountOf(home, "BrowserInterop.TryInvokeVoidAsync(JS, \"localStorage."));
        Assert.Contains("BrowserInterop.TryInvokeAsync<string?>(\n            JS,\n            \"localStorage.getItem\"", home);

        // Only a pause the loop itself asked for ends it quietly; any other
        // failure must leave the clock paused, not running with no loop.
        var loop = home[home.IndexOf("private async Task RunClockAsync", StringComparison.Ordinal)..
            home.IndexOf("private void StopClock()", StringComparison.Ordinal)];
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", loop);
        Assert.Contains("catch (Exception) when (!cancellationToken.IsCancellationRequested)", loop);
        Assert.Contains("Session.PauseClock();", loop);
        Assert.Contains("_paused = true;", loop);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class ThrowingJsRuntime(Exception? failure) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            failure is null
                ? ValueTask.FromResult(typeof(TValue) == typeof(string) ? (TValue)(object)"stored" : default!)
                : ValueTask.FromException<TValue>(failure);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
