using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>Circuit-side half of the browser-bound cookie bridge (#369).</summary>
public class AuthCookieServiceTests
{
    private const string RequestToken = "circuit-antiforgery-request-token";

    private sealed class FixedAntiforgeryStateProvider(AntiforgeryRequestToken? token) : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => token;
    }

    private sealed class KeyLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SessionBridgeService _bridge;
    private readonly Stub _module;
    private readonly IJSRuntime _js;
    private Func<object?[], object?> _completeSignIn = _ => new ValueTask<bool>(true);

    public AuthCookieServiceTests()
    {
        _bridge = new SessionBridgeService(_cache, NullLogger<SessionBridgeService>.Instance);
        var module = Stub.Create<IJSObjectReference>(out _module);
        _module.On(nameof(IJSObjectReference.InvokeAsync), args => _completeSignIn(args));
        _js = Stub.Create<IJSRuntime>(s => s.On(nameof(IJSRuntime.InvokeAsync), _ => new ValueTask<IJSObjectReference>(module)));
    }

    private AuthCookieService CreateService(AntiforgeryRequestToken? token) => new(
        _bridge,
        new FixedAntiforgeryStateProvider(token),
        Options.Create(new AntiforgeryOptions()),
        _js,
        NullLogger<AuthCookieService>.Instance,
        new KeyLocalizer());

    private Task<OperationResult> Establish(AntiforgeryRequestToken? token) =>
        CreateService(token).EstablishCookieSessionAsync("https://matrix.example.com", "syt_token", "@admin:example.com", "admin");

    [Fact]
    public async Task Success_BindsBridgeToCircuitToken_AndSendsKeyInBody()
    {
        var result = await Establish(new AntiforgeryRequestToken(RequestToken, "__RequestVerificationToken"));

        Assert.True(result.Success);
        var call = Assert.Single(_module.CallsTo(nameof(IJSObjectReference.InvokeAsync)));
        Assert.Equal("completeSignIn", call[0]);
        var jsArgs = (object?[])call[1]!;
        var key = (string)jsArgs[0]!;
        Assert.Equal(RequestToken, jsArgs[1]);
        Assert.Equal(new AntiforgeryOptions().HeaderName, jsArgs[2]);

        // The key handed to the browser is bound to exactly this circuit's token.
        Assert.False(_bridge.TryConsumeBridge(key, "other-browser-token", out _));
        Assert.True(_bridge.TryConsumeBridge(key, RequestToken, out var data));
        Assert.Equal("syt_token", data.AccessToken);
    }

    [Fact]
    public async Task MissingAntiforgeryToken_FailsWithoutCreatingBridgeOrCallingBrowser()
    {
        var result = await Establish(null);

        Assert.False(result.Success);
        Assert.Equal("ErrorSessionBridgeFailed", result.Message);
        Assert.Empty(_module.Calls);
    }

    [Fact]
    public async Task RejectedExchange_IsReportedAsFailure()
    {
        _completeSignIn = _ => new ValueTask<bool>(false);

        var result = await Establish(new AntiforgeryRequestToken(RequestToken, "__RequestVerificationToken"));

        Assert.False(result.Success);
        Assert.NotEqual(MudBlazor.Severity.Normal, result.Severity);
    }

    [Fact]
    public async Task DisconnectedCircuit_IsSilentCancellation()
    {
        _completeSignIn = _ => throw new JSDisconnectedException("circuit gone");

        var result = await Establish(new AntiforgeryRequestToken(RequestToken, "__RequestVerificationToken"));

        Assert.False(result.Success);
        Assert.Equal(MudBlazor.Severity.Normal, result.Severity);
    }
}
