using Microsoft.JSInterop;

namespace SynapseAdmin.Extensions;

public static class JsRuntimeExtensions
{
    /// <summary>
    /// Writes <paramref name="text"/> to the browser clipboard. Returns false if the write fails for any reason, for
    /// example a denied permission, a page without a secure context or a disconnected circuit, and logs the failure
    /// at Debug to <paramref name="logger"/>. The text itself is never logged: it can be an access token.
    /// </summary>
    public static async Task<bool> TryCopyToClipboardAsync(this IJSRuntime js, string text, ILogger? logger = null)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Clipboard write failed");
            return false;
        }
    }
}
