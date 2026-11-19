using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
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

    private IMatrixSessionService Session() => Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => _gateway));
}
