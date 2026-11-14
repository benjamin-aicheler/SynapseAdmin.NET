using System.Reflection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Resources;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// #395 (H-08): identifier checks compare ordinally. Culture-aware comparison ignores characters such as the
/// soft hyphen (U+00AD), so it would accept strings that are not what they look like.
/// </summary>
public class OrdinalComparisonTests
{
    private const string ServerName = "example.org";
    private const string SoftHyphen = "­";

    [Theory]
    [InlineData("#room:example.org", true)]
    [InlineData("room:example.org", false)]             // missing '#'
    [InlineData("#room:Example.org", false)]            // server name is case-sensitive, as in Synapse
    [InlineData("#room:other.org", false)]
    [InlineData(SoftHyphen + "#room:example.org", false)] // culture-aware StartsWith would ignore the soft hyphen
    [InlineData("#room:example.org" + SoftHyphen, false)] // culture-aware EndsWith would ignore the soft hyphen
    public void AliasValidation_IsOrdinalAndCaseSensitive(string alias, bool valid)
    {
        var error = ValidateAlias(alias);

        if (valid) Assert.Null(error);
        else Assert.NotNull(error);
    }

    [Theory]
    [InlineData("mxc://example.com/abc123", "abc123")]
    [InlineData("MXC://example.com/abc123", "abc123")] // URI schemes are case-insensitive
    [InlineData(SoftHyphen + "mxc://example.com/abc123", SoftHyphen + "mxc://example.com/abc123")]
    public void GetMediaIdFromMxc_MatchesSchemeOrdinallyIgnoringCase(string input, string expected) =>
        Assert.Equal(expected, MediaHelper.GetMediaIdFromMxc(input));

    private static string? ValidateAlias(string alias)
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("get_ServerName", _ => ServerName));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var dialog = new RoomAliasesDialog { MatrixSession = session };
        typeof(RoomAliasesDialog).GetProperty("L", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(dialog, new KeyLocalizer());

        return (string?)typeof(RoomAliasesDialog)
            .GetMethod("ValidateAliasFormat", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, [alias]);
    }

    private sealed class KeyLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
