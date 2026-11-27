using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Whether a Matrix ID is on the session's own server: it must end in <c>:</c> + the server name, compared
/// case-insensitively. The link checks in the UI are in <c>Components/LocalUserLinkTests</c>.
/// </summary>
public class LocalMatrixIdTests
{
    [Theory]
    [InlineData("@a:example.org", "example.org", true)]
    [InlineData("@a:EXAMPLE.org", "example.org", true)]
    [InlineData("@a:example.org", "Example.ORG", true)]
    [InlineData("@a:example.org.evil", "example.org", false)]
    [InlineData("@a:evilexample.org", "example.org", false)]
    [InlineData("@a:other.org", "example.org", false)]
    [InlineData("", "example.org", false)]
    [InlineData("@a:other.org", "", true)] // unlike the UI checks, an unknown server name allows every ID
    [InlineData("@a:other.org", null, true)]
    public async Task UserDetails_QueryOnlyLocalUsers_UnlessTheServerNameIsUnknown(string userId, string? serverName, bool queried)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.On("get_ServerName", _ => serverName);
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));

        // The unconfigured stub gateway finds no user, so a queried ID fails with UserNotFound.
        var result = await new UserService(session, NullLogger<UserService>.Instance, new KeyLocalizer()).GetUserDetailsAsync(userId);

        Assert.Equal(queried ? 1 : 0, stub.CallsTo(nameof(IMatrixGateway.GetUserDetailsAsync)).Count);
        Assert.Equal(queried ? "UserNotFound" : "CannotQueryRemoteUser", result.Message);
    }

    [Theory]
    [InlineData("@a:example.org", "example.org", true)]
    [InlineData("@a:EXAMPLE.org", "example.org", true)]
    [InlineData("@a:example.org", "Example.ORG", true)]
    [InlineData("!room:example.org", "example.org", true)]
    [InlineData("@a:example.org.evil", "example.org", false)]
    [InlineData("@a:evilexample.org", "example.org", false)]
    [InlineData("@a:other.org", "example.org", false)]
    [InlineData("", "example.org", false)]
    [InlineData(null, "example.org", false)]
    [InlineData("@a:", "", false)]
    [InlineData("@a:", null, false)]
    public void IsOnServer_MatchesTheServerSuffixIgnoringCase(string? id, string? serverName, bool expected) =>
        Assert.Equal(expected, MatrixIdHelper.IsOnServer(id, serverName));

    [Theory]
    [InlineData("@a:example.org", true)]
    [InlineData("@alice.smith_1=x/y:example.org", true)]
    [InlineData("@a:example.org:8448", true)] // a port belongs to the server name
    [InlineData("@a:[::1]:8448", true)]
    [InlineData("a:example.org", false)]
    [InlineData("#a:example.org", false)]
    [InlineData("!a:example.org", false)]
    [InlineData("@a", false)]
    [InlineData("@:example.org", false)]
    [InlineData("@a:", false)]
    [InlineData("@a b:example.org", false)]
    [InlineData(" @a:example.org", false)]
    [InlineData("@a:example.org\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUserId_AcceptsOnlyTheFullUserIdForm(string? id, bool expected) =>
        Assert.Equal(expected, MatrixIdHelper.IsUserId(id));

    /// <summary>Exact, as Synapse's <c>is_mine</c> and Tuwunel's <c>user_is_local</c> compare it (#587).</summary>
    [Theory]
    [InlineData("@a:example.org", "example.org", true)]
    [InlineData("@a:example.org:8448", "example.org:8448", true)]
    [InlineData("@a:EXAMPLE.org", "example.org", false)]
    [InlineData("@a:example.org", "Example.org", false)]
    [InlineData("@a:example.org:8448", "example.org", false)]
    [InlineData("@a:example.org.evil", "example.org", false)]
    [InlineData("@a:evil.example.org", "example.org", false)]
    [InlineData("@a:other.org", "example.org", false)]
    [InlineData("a:example.org", "example.org", false)]
    [InlineData("@a:example.org", "", false)]
    [InlineData("@a:example.org", null, false)]
    [InlineData(null, "example.org", false)]
    public void IsLocalUserId_ComparesTheServerNameExactly(string? id, string? serverName, bool expected) =>
        Assert.Equal(expected, MatrixIdHelper.IsLocalUserId(id, serverName));
}
