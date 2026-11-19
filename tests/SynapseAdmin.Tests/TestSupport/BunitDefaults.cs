using System.Runtime.CompilerServices;
using Bunit;

namespace SynapseAdmin.Tests.TestSupport;

/// <summary>
/// bUnit settings for the whole test assembly, applied when it loads, before any test runs.
/// </summary>
internal static class BunitDefaults
{
    /// <summary>
    /// The time every <c>WaitFor…</c> call allows unless it passes its own timeout. bUnit's 1 s default made checks time
    /// out under full-suite parallel load on slow hardware, sometimes before the first check had completed. A wait returns
    /// as soon as its condition holds, so only a failing test waits longer.
    /// </summary>
    internal static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [ModuleInitializer]
    internal static void Apply() => BunitContext.DefaultWaitTimeout = WaitTimeout;
}
