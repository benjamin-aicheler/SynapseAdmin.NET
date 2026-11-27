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

    /// <summary>Both room ID forms: with a domain, and room version 12's unpadded base64 without one (#605).</summary>
    [Theory]
    [InlineData("!abc:example.org", true)]
    [InlineData("!abc:example.org:8448", true)]
    [InlineData("!QxR7a-Kq_9abc", true)]
    [InlineData("!a", true)]
    [InlineData("!", false)]
    [InlineData("#room:example.org", false)]
    [InlineData("@a:example.org", false)]
    [InlineData("abc:example.org", false)]
    [InlineData("!ab c:example.org", false)]
    [InlineData(" !abc:example.org", false)]
    [InlineData("!abc:example.org\t", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRoomId_AcceptsBothRoomIdForms(string? id, bool expected) =>
        Assert.Equal(expected, MatrixIdHelper.IsRoomId(id));

    /// <summary>
    /// Synapse splits an alias at the first <c>:</c>; the localpart may contain spaces (#580), the server part is a server
    /// name (#606).
    /// </summary>
    [Theory]
    [InlineData("#room:example.org", true)]
    [InlineData("#my room:example.org", true)]
    [InlineData("#room:example.org:8448", true)]
    [InlineData("#room:[::1]:8448", true)]
    [InlineData("#room:127.0.0.1", true)]
    [InlineData("#r:x", true)]
    [InlineData("#:example.org", false)]
    [InlineData("#   :example.org", false)]
    [InlineData("#room", false)]
    [InlineData("#room:", false)]
    [InlineData("#room:exa mple.org", false)]
    [InlineData("#room:example.org/x", false)]
    [InlineData("#ro\nom:example.org", false)]
    [InlineData(" #room:example.org", false)]
    [InlineData("#room:example.org ", false)]
    [InlineData("!room:example.org", false)]
    [InlineData("room:example.org", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRoomAlias_AcceptsTheAliasForm(string? id, bool expected) =>
        Assert.Equal(expected, MatrixIdHelper.IsRoomAlias(id));

    /// <summary>The Matrix spec's server name: a DNS name, IPv4 or bracketed IPv6, with an optional port (#606).</summary>
    [Theory]
    [InlineData("example.org", true)]
    [InlineData("matrix.example-1.org", true)]
    [InlineData("example.org:8448", true)]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1:8008", true)]
    [InlineData("[::1]", true)]
    [InlineData("[2001:db8::1]:8448", true)]
    [InlineData("example.org:", false)]
    [InlineData("example.org:123456", false)]
    [InlineData("example.org:port", false)]
    [InlineData("exa mple.org", false)]
    [InlineData("example.org/path", false)]
    [InlineData("a,b", false)]
    [InlineData("user@example.org", false)]
    [InlineData("https://example.org", false)]
    [InlineData("::1", false)]
    [InlineData("example.org\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsServerName_FollowsTheSpecGrammar(string? name, bool expected) =>
        Assert.Equal(expected, MatrixIdHelper.IsServerName(name));

    [Fact]
    public void IsServerName_AllowsAtMost255Characters()
    {
        Assert.True(MatrixIdHelper.IsServerName(new string('a', 255)));
        Assert.False(MatrixIdHelper.IsServerName(new string('a', 256)));
    }

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
