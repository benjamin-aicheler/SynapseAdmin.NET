using Microsoft.Extensions.Localization;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Components.Shared;

/// <summary>
/// The "Servers to join through" field of the two join dialogs (#606): servers separated by commas or whitespace. The
/// service trims them and drops duplicates again, and checks each one again.
/// </summary>
public static class JoinServerInput
{
    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    /// <summary>The servers in <paramref name="text"/>, in order, without empty entries.</summary>
    public static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The localized reason the field is refused, naming its first invalid server, or <c>null</c>.</summary>
    public static string? Validate(string? text, IStringLocalizer<SharedResources> localizer)
    {
        var invalid = Split(text).FirstOrDefault(server => !MatrixIdHelper.IsServerName(server));
        return invalid == null ? null : localizer["InvalidServerName", invalid].Value;
    }
}
