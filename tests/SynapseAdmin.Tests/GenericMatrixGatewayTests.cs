using System.Reflection;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Models;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// <see cref="GenericMatrixGateway"/> serves homeservers without the Synapse Admin API. Every admin method it declares
/// refuses the operation with <see cref="OperationNotSupportedByHomeserverException"/>, named after the method, so the
/// service layer reports the shared "not supported" warning (AC-14) and nothing reaches the homeserver.
/// </summary>
public sealed class GenericMatrixGatewayTests
{
    private static MethodInfo[] DeclaredMethods() => typeof(GenericMatrixGateway)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    // The signature, not the name: GetMediaMetadataAsync is overloaded.
    public static TheoryData<string> Methods => new(DeclaredMethods().Select(m => m.ToString()!));

    [Fact]
    public void DeclaresTheAdminMethods() => Assert.NotEmpty(DeclaredMethods());

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task EveryDeclaredMethod_RefusesWithItsOwnName(string signature)
    {
        var method = DeclaredMethods().Single(m => m.ToString() == signature);
        // Nothing listens on port 9 (discard): a request would fail with a different exception.
        var gateway = new GenericMatrixGateway(new AuthenticatedHomeserverGeneric(
            "example.com", new HomeserverResolverService.WellKnownUris { Client = "http://127.0.0.1:9" }, null, "syt_token"));
        var arguments = method.GetParameters().Select(DefaultArgument).ToArray();

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => InvokeAsync(gateway, method, arguments));

        Assert.Equal(method.Name, error.Operation);
    }

    private static object? DefaultArgument(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue) return parameter.DefaultValue;
        if (parameter.ParameterType == typeof(string)) return "x";
        return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
    }

    /// <summary>Unwraps a synchronous throw and awaits a returned task, so both ways of refusing count.</summary>
    private static async Task InvokeAsync(object target, MethodInfo method, object?[] arguments)
    {
        object? returned;
        try
        {
            returned = method.Invoke(target, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }

        if (returned is Task task) await task;
    }
}
