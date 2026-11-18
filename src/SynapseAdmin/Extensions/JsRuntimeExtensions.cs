using Microsoft.JSInterop;

namespace SynapseAdmin.Extensions;

public static class JsRuntimeExtensions
{
    /// <summary>
    /// Writes <paramref name="text"/> to the browser clipboard. Returns false if the write fails for any reason, for
    /// example a denied permission, a page without a secure context or a disconnected circuit.
    /// </summary>
    public static async Task<bool> TryCopyToClipboardAsync(this IJSRuntime js, string text)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
