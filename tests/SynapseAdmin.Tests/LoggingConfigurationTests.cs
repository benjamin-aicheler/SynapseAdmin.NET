using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Debugging;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Serilog's <see cref="SelfLog"/> is process-wide, and the <c>WebApplicationFactory</c> tests run the app's real
/// Serilog setup. Tests that read <see cref="SelfLog"/> therefore run alone, after all parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerilogSelfLogCollection
{
    public const string Name = "Serilog SelfLog";
}

/// <summary>
/// Issue #498: when the Serilog configuration names a method that no loaded assembly provides (an enricher or sink
/// whose package isn't referenced), Serilog skips it and reports it only to <see cref="SelfLog"/>. The app then starts
/// without it, and nothing says so.
/// </summary>
[Collection(SerilogSelfLogCollection.Name)]
public class LoggingConfigurationTests
{
    [Fact]
    public void ShippedSerilogConfiguration_ResolvesEveryConfiguredMethod()
    {
        // The build copies the app's appsettings.json into the test output.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var selfLog = new List<string>();

        SelfLog.Enable(message =>
        {
            lock (selfLog) selfLog.Add(message);
        });
        try
        {
            // The same call as Program.cs.
            using var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
        }
        finally
        {
            SelfLog.Disable();
        }

        Assert.True(selfLog.Count == 0, "Serilog reported:" + Environment.NewLine + string.Join(Environment.NewLine, selfLog));
    }
}
