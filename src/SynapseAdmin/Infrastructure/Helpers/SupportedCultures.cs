namespace SynapseAdmin.Infrastructure.Helpers;

/// <summary>
/// The UI cultures the application ships resources for (see LOCALIZATION.md). Single source for request
/// localization, the language selector and the culture cookie endpoint.
/// </summary>
public static class SupportedCultures
{
    public const string English = "en-US";
    public const string German = "de-DE";
    public const string French = "fr-FR";

    public const string Default = English;

    public static IReadOnlyList<string> All { get; } = [English, German, French];

    /// <summary>
    /// Returns the canonical name of a supported culture (case-insensitive match), or <c>null</c> if
    /// <paramref name="culture"/> is missing, malformed or not supported.
    /// </summary>
    public static string? Normalize(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return null;
        var trimmed = culture.Trim();
        return All.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
