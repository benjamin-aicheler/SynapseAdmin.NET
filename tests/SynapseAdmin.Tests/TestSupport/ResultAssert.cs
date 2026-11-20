using MudBlazor;
using SynapseAdmin.Models;
using Xunit;

namespace SynapseAdmin.Tests.TestSupport;

/// <summary>Assertions for service results: a failure, a success and a silent cancellation.</summary>
internal static class ResultAssert
{
    public static void Failure(OperationResult result, string message, Severity severity = Severity.Error)
    {
        Assert.False(result.Success);
        Assert.Equal(message, result.Message);
        Assert.Equal(severity, result.Severity);
    }

    public static void Success(OperationResult result, string message)
    {
        Assert.True(result.Success);
        Assert.Equal(message, result.Message);
        Assert.Equal(Severity.Success, result.Severity);
    }

    public static void Cancelled(OperationResult result)
    {
        Assert.False(result.Success);
        Assert.Equal("Operation cancelled", result.Message);
        Assert.Equal(Severity.Normal, result.Severity);
    }
}
