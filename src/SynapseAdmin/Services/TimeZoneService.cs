using Microsoft.JSInterop;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class TimeZoneService(IJSRuntime jsRuntime, ILogger<TimeZoneService> logger) : ITimeZoneService, IAsyncDisposable
{
    private IJSObjectReference? _jsModule;
    private bool _isInitialized;

    public TimeZoneInfo UserTimeZone { get; private set; } = TimeZoneInfo.Utc;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            _jsModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/timezone-interop.js");
            var ianaId = await _jsModule.InvokeAsync<string>("getBrowserTimeZone");

            if (!string.IsNullOrWhiteSpace(ianaId) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var tz))
            {
                UserTimeZone = tz;
                logger.LogDebug("Resolved browser time zone: {TimeZoneId}", tz.Id.SanitizeForLogging());
            }
            else
            {
                logger.LogWarning("Unrecognized browser time zone '{TimeZoneId}', falling back to UTC.", ianaId.SanitizeForLogging());
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
            logger.LogDebug(ex, "TimeZoneService JS interop cancelled or circuit disconnected.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve browser time zone via JS interop; falling back to UTC.");
        }
        finally
        {
            _isInitialized = true;
        }
    }

    public DateTimeOffset GetLocalNow() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, UserTimeZone);

    public DateTimeOffset ToLocal(DateTimeOffset timestamp) =>
        TimeZoneInfo.ConvertTime(timestamp, UserTimeZone);

    public DateTimeOffset ToDateTimeOffset(DateTime localDateTime)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (UserTimeZone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        var offset = UserTimeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    public string GetDisplayOffset(DateTime? forLocalDateTime = null)
    {
        var refTime = DateTime.SpecifyKind(forLocalDateTime ?? GetLocalNow().DateTime, DateTimeKind.Unspecified);
        if (UserTimeZone.IsInvalidTime(refTime))
        {
            refTime = refTime.AddHours(1);
        }

        var offset = UserTimeZone.GetUtcOffset(refTime);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{UserTimeZone.Id} (UTC{sign}{offset:hh\\:mm})";
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null)
        {
            try
            {
                await _jsModule.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors when circuit is already disconnected
            }
        }

        GC.SuppressFinalize(this);
    }
}
