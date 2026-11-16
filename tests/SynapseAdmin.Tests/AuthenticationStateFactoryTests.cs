using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// The authenticated state of <see cref="MatrixAuthenticationStateProvider"/>: four claims taken from the session's
/// gateway (never from the login input or the stored values) and the authentication type "MatrixAuth".
/// </summary>
public sealed class AuthenticationStateFactoryTests
{
    private const string UserId = "@alice:example.com";
    private const string Username = "alice";
    private const string Homeserver = "https://matrix.example.com";
    private const string AccessToken = "syt_gateway_token";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_CachesAndAnnouncesTheAuthenticatedState(bool withToken)
    {
        var provider = CreateProvider(out _);
        Task<AuthenticationState>? announced = null;
        provider.AuthenticationStateChanged += task => announced = task;

        // The input differs from the gateway's values: the claims must come from the gateway.
        var result = withToken
            ? await provider.LoginWithTokenAsync("https://entered.example.com", "syt_entered_token")
            : await provider.LoginAsync("https://entered.example.com", "entered-user", "password");

        Assert.True(result.Success);
        var state = await provider.GetAuthenticationStateAsync();
        AssertAuthenticatedState(state);
        Assert.NotNull(announced);
        Assert.Same(state, await announced);
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_StoredSession_RestoresTheAuthenticatedState()
    {
        var provider = CreateProvider(out var storage);
        await storage.SetAsync("matrix_homeserver", "https://stored.example.com");
        await storage.SetAsync("matrix_access_token", "syt_stored_token");

        AssertAuthenticatedState(await provider.GetAuthenticationStateAsync());
    }

    [Fact]
    public void CreateAuthenticatedState_HasTheFourGatewayClaims()
    {
        AssertAuthenticatedState(MatrixAuthenticationStateProvider.CreateAuthenticatedState(CreateGateway()));
    }

    private static void AssertAuthenticatedState(AuthenticationState state)
    {
        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.Equal("MatrixAuth", state.User.Identity.AuthenticationType);
        Assert.Equal(Username, state.User.Identity.Name);
        (string, string)[] expectedClaims =
        [
            (ClaimTypes.NameIdentifier, UserId),
            (ClaimTypes.Name, Username),
            ("Homeserver", Homeserver),
            ("AccessToken", AccessToken),
        ];
        Assert.Equal(expectedClaims, state.User.Claims.Select(c => (c.Type, c.Value)));
    }

    private static IMatrixGateway CreateGateway() => Stub.Create<IMatrixGateway>(s => s
        .On("get_UserId", _ => UserId)
        .On("get_Username", _ => Username)
        .On("get_HomeserverUrl", _ => Homeserver)
        .On("get_AccessToken", _ => AccessToken));

    private static MatrixAuthenticationStateProvider CreateProvider(out ProtectedLocalStorage storage)
    {
        var gateway = CreateGateway();
        var session = Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true)
            .On(nameof(IMatrixSessionService.LoginAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IMatrixSessionService.LoginWithTokenAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IMatrixSessionService.RestoreSessionAsync), _ => Task.FromResult(OperationResult.Ok())));
        storage = new ProtectedLocalStorage(new FakeLocalStorageJsRuntime(), new EphemeralDataProtectionProvider());
        return new MatrixAuthenticationStateProvider(storage, session, new HttpContextAccessor(), NullLogger<MatrixAuthenticationStateProvider>.Instance);
    }

    /// <summary>In-memory stand-in for the browser's <c>localStorage</c>, as called by <see cref="ProtectedLocalStorage"/>.</summary>
    private sealed class FakeLocalStorageJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _items = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = (string)args![0]!;
            switch (identifier)
            {
                case "localStorage.getItem":
                    return new ValueTask<TValue>((TValue)(object)_items.GetValueOrDefault(key)!);
                case "localStorage.setItem":
                    _items[key] = (string)args[1]!;
                    break;
                case "localStorage.removeItem":
                    _items.Remove(key);
                    break;
            }
            return default;
        }
    }
}
