using Microsoft.JSInterop;

namespace Overseer.Web.UI;

/// <summary>
/// Best-effort browser calls (campaign storage, audio cues). They must never
/// end a render lifecycle method or the clock loop: on the Blazor Server host
/// an in-flight call is cancelled when the circuit tears down or the interop
/// timeout elapses, and a browser may refuse storage (quota, disabled).
/// Unhandled in <c>OnAfterRenderAsync</c> that kills the circuit; inside the
/// clock loop a <see cref="TaskCanceledException"/> used to look like a normal
/// pause and silently stop the run.
/// </summary>
public static class BrowserInterop
{
    public static async Task<bool> TryInvokeVoidAsync(
        IJSRuntime js,
        string identifier,
        params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (Exception exception) when (IsBrowserUnavailable(exception))
        {
            return false;
        }
    }

    public static async Task<T?> TryInvokeAsync<T>(
        IJSRuntime js,
        string identifier,
        params object?[] args)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, args);
        }
        catch (Exception exception) when (IsBrowserUnavailable(exception))
        {
            return default;
        }
    }

    // The interop calls take no caller token, so any cancellation here is
    // the runtime's (teardown or timeout), never a pause the caller asked for.
    private static bool IsBrowserUnavailable(Exception exception) =>
        exception is JSDisconnectedException
            or OperationCanceledException
            or JSException;
}
