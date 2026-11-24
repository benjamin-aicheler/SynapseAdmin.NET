using System.Globalization;
using System.Resources;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// <see cref="GatewayCallExtensions"/>: the shared service-layer error policy. The per-service characterization
/// suites (<see cref="MediaServiceCharacterizationTests"/>) cover the results; these tests cover what they can't see.
/// </summary>
public sealed class GatewayCallExtensionsTests
{
    private readonly IMatrixGateway _gateway = Stub.Create<IMatrixGateway>();
    private readonly List<Exception> _logged = [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsTheSessionGatewayOnce_AndPassesItToTheCall(bool withData)
    {
        var session = Stub.Create<IMatrixSessionService>(out var sessionStub);
        sessionStub.On("get_Gateway", _ => _gateway);
        IMatrixGateway? received = null;

        if (withData)
        {
            await session.RunGatewayCallAsync(new KeyLocalizer(), "Error", _logged.Add, gateway =>
            {
                received = gateway;
                return Task.FromResult(OperationResult<int>.Ok(1));
            });
        }
        else
        {
            await session.RunGatewayCallAsync(new KeyLocalizer(), "Error", _logged.Add, gateway =>
            {
                received = gateway;
                return Task.FromResult(OperationResult.Ok());
            });
        }

        Assert.Same(_gateway, received);
        Assert.Single(sessionStub.CallsTo("get_Gateway"));
    }

    [Fact]
    public async Task WithoutSession_DoesNotRunTheCall()
    {
        var session = Stub.Create<IMatrixSessionService>();
        var ran = false;

        var result = await session.RunGatewayCallAsync<int>(new KeyLocalizer(), "Error", _logged.Add, _ =>
        {
            ran = true;
            return Task.FromResult(OperationResult<int>.Ok(1));
        });

        Assert.False(ran);
        Assert.Equal("NotAuthenticated", result.Message);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task ReturnsTheCallsResultUnchanged()
    {
        var expected = OperationResult<int>.Failure("NotFound", Severity.Warning);

        var result = await Session().RunGatewayCallAsync(new KeyLocalizer(), "Error", _logged.Add, _ => Task.FromResult(expected));

        Assert.Same(expected, result);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task SynchronousCancellation_IsSilent()
    {
        var result = await Session().RunGatewayCallAsync(new KeyLocalizer(), "Error", _logged.Add, _ => throw new OperationCanceledException());

        Assert.Equal(Severity.Normal, result.Severity);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task SynchronousException_IsLoggedOnce_AndReturnsTheLocalizedFailureKey()
    {
        var error = new InvalidOperationException("boom");

        var result = await Session().RunGatewayCallAsync(new KeyLocalizer(), "ErrorDoingThings", _logged.Add, _ => throw error);

        Assert.False(result.Success);
        Assert.Equal("ErrorDoingThings", result.Message);
        Assert.Equal(Severity.Error, result.Severity);
        Assert.Same(error, Assert.Single(_logged));
    }

    /// <summary>
    /// A gateway that can't carry out an operation with Synapse's result refuses it (ADR 0006, AC-14). The refusal is
    /// logged once and reported as the shared "not supported" warning instead of the method's failure key.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotSupportedByHomeserver_IsLoggedOnce_AndReturnsTheNotSupportedWarning(bool withData)
    {
        var error = new OperationNotSupportedByHomeserverException("DeleteLocalMediaAsync");

        OperationResult result = withData
            ? await Session().RunGatewayCallAsync<int>(new KeyLocalizer(), "ErrorDoingThings", _logged.Add, _ => throw error)
            : await Session().RunGatewayCallAsync(new KeyLocalizer(), "ErrorDoingThings", _logged.Add, _ => throw error);

        Assert.False(result.Success);
        Assert.Equal("OperationNotSupportedByHomeserver", result.Message);
        Assert.Equal(Severity.Warning, result.Severity);
        Assert.Same(error, Assert.Single(_logged));
    }

    /// <summary>
    /// A plain <see cref="NotSupportedException"/> comes from elsewhere (a stream, the JSON serializer, LibMatrix) and
    /// says nothing about the homeserver, so it stays an ordinary failure.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainNotSupportedException_KeepsTheFailureKey(bool withData)
    {
        var error = new NotSupportedException("Stream does not support seeking.");

        OperationResult result = withData
            ? await Session().RunGatewayCallAsync<int>(new KeyLocalizer(), "ErrorDoingThings", _logged.Add, _ => throw error)
            : await Session().RunGatewayCallAsync(new KeyLocalizer(), "ErrorDoingThings", _logged.Add, _ => throw error);

        Assert.False(result.Success);
        Assert.Equal("ErrorDoingThings", result.Message);
        Assert.Equal(Severity.Error, result.Severity);
        Assert.Same(error, Assert.Single(_logged));
    }

    [Fact]
    public void NotSupportedByHomeserver_NamesTheRefusedOperation()
    {
        var error = RefusedOperation();

        Assert.IsAssignableFrom<NotSupportedException>(error);
        Assert.Equal(nameof(RefusedOperation), error.Operation);
        Assert.Contains(nameof(RefusedOperation), error.Message);
    }

    private static OperationNotSupportedByHomeserverException RefusedOperation() => new();

    /// <summary>The "not supported" warning exists in every UI language, not only through the neutral fallback.</summary>
    [Fact]
    public void NotSupportedWarning_IsTranslated()
    {
        var resources = new ResourceManager(typeof(SharedResources).FullName!, typeof(SharedResources).Assembly);
        var english = resources.GetString("OperationNotSupportedByHomeserver", new CultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(english));
        foreach (var culture in new[] { "de", "fr" })
        {
            var translated = resources.GetString("OperationNotSupportedByHomeserver", new CultureInfo(culture));
            Assert.False(string.IsNullOrWhiteSpace(translated));
            Assert.NotEqual(english, translated);
        }
    }

    private IMatrixSessionService Session() => Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => _gateway));
}
