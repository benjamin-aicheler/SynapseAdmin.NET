using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SynapseAdmin.Services;
using Xunit;

namespace SynapseAdmin.Tests;

public class TimeZoneAndPurgeTimestampTests
{
    [Fact]
    public async Task InitializeAsync_WithValidIanaTimeZone_ComputesPurgeTimestampInUserZone()
    {
        var jsRuntime = new StubJsRuntime("Europe/Berlin");
        await using var service = new TimeZoneService(jsRuntime, NullLogger<TimeZoneService>.Instance);

        await service.InitializeAsync();

        // 2026-01-15 12:00:00 in Europe/Berlin (CET = UTC+1) -> 2026-01-15 11:00:00 UTC
        var selectedDate = new DateTime(2026, 1, 15);
        var selectedTime = new TimeSpan(12, 0, 0);
        var localDateTime = selectedDate.Date.Add(selectedTime);

        var dto = service.ToDateTimeOffset(localDateTime);
        var purgeUpToTs = dto.ToUnixTimeMilliseconds();

        var expectedUtc = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(1), dto.Offset);
        Assert.Equal(expectedUtc.ToUnixTimeMilliseconds(), purgeUpToTs);
        Assert.Contains("(UTC+01:00)", service.GetDisplayOffset(localDateTime));
    }

    [Fact]
    public async Task ToDateTimeOffset_DuringDstSpringForwardGap_AdvancesInvalidHourCleanly()
    {
        var jsRuntime = new StubJsRuntime("Europe/Berlin");
        await using var service = new TimeZoneService(jsRuntime, NullLogger<TimeZoneService>.Instance);
        await service.InitializeAsync();

        // In Europe/Berlin, 2026-03-29 at 02:30:00 does not exist (clocks jump 02:00 -> 03:00 CEST UTC+2)
        var invalidLocalTime = new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Local);

        var dto = service.ToDateTimeOffset(invalidLocalTime);

        Assert.Equal(3, dto.Hour);
        Assert.Equal(30, dto.Minute);
        Assert.Equal(TimeSpan.FromHours(2), dto.Offset);
    }

    [Fact]
    public async Task InitializeAsync_WithUnknownTimeZone_FallsBackToUtc()
    {
        var jsRuntime = new StubJsRuntime("Invalid/NoSuchZone");
        await using var service = new TimeZoneService(jsRuntime, NullLogger<TimeZoneService>.Instance);

        await service.InitializeAsync();

        Assert.Equal(TimeZoneInfo.Utc, service.UserTimeZone);
        Assert.Equal("UTC (UTC+00:00)", service.GetDisplayOffset(new DateTime(2026, 6, 1, 12, 0, 0)));
    }

    private sealed class StubJsRuntime(string timeZoneId) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import")
            {
                IJSObjectReference module = new StubJsModule(timeZoneId);
                return new ValueTask<TValue>((TValue)module);
            }

            throw new NotSupportedException($"Unexpected JS call: {identifier}");
        }
    }

    private sealed class StubJsModule(string timeZoneId) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "getBrowserTimeZone")
            {
                return new ValueTask<TValue>((TValue)(object)timeZoneId);
            }

            throw new NotSupportedException($"Unexpected module call: {identifier}");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
