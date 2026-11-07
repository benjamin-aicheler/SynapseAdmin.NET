using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
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
    public async Task ToDateTimeOffset_DuringDstFallBackAmbiguousHour_SelectsEarlierDstOccurrence()
    {
        var jsRuntime = new StubJsRuntime("Europe/Berlin");
        await using var service = new TimeZoneService(jsRuntime, NullLogger<TimeZoneService>.Instance);
        await service.InitializeAsync();

        // In Europe/Berlin, 2026-10-25 at 02:30:00 occurs twice (03:00 CEST UTC+2 falls back to 02:00 CET UTC+1)
        var ambiguousLocalTime = new DateTime(2026, 10, 25, 2, 30, 0, DateTimeKind.Local);

        Assert.True(service.IsAmbiguousLocalTime(ambiguousLocalTime));

        var dto = service.ToDateTimeOffset(ambiguousLocalTime);

        Assert.Equal(2, dto.Hour);
        Assert.Equal(30, dto.Minute);
        Assert.Equal(TimeSpan.FromHours(2), dto.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), dto.ToUniversalTime());
        Assert.Contains("(UTC+02:00)", service.GetDisplayOffset(ambiguousLocalTime));
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

    [Fact]
    public async Task PurgeHistoryDialog_ExecutePurgeAsync_WhenSuccessWithoutPurgeId_ShowsFeedbackAndClosesDialog()
    {
        var roomService = DispatchProxy.Create<IRoomService, StubRoomServiceProxy>();
        var roomProxy = (StubRoomServiceProxy)(object)roomService;
        roomProxy.PurgeHandler = (_, _, _) => Task.FromResult(OperationResult<string>.Ok(null!));

        var snackbar = DispatchProxy.Create<ISnackbar, RecordingSnackbarProxy>();
        var snackbarProxy = (RecordingSnackbarProxy)(object)snackbar;

        var mudDialog = DispatchProxy.Create<IMudDialogInstance, RecordingDialogProxy>();
        var dialogProxy = (RecordingDialogProxy)(object)mudDialog;

        using var dialog = CreateDialog("!room:example.com", roomService, snackbar, mudDialog);

        await dialog.ExecutePurgeAsync(new PurgeHistoryViewModel { PurgeUpToEventId = "$event1" });

        Assert.Empty(roomProxy.SetActivePurgeCalls);
        var notification = Assert.Single(snackbarProxy.AddedMessages);
        Assert.Equal(Severity.Success, notification.Severity);
        Assert.False(string.IsNullOrWhiteSpace(notification.Message));
        Assert.Equal(1, dialogProxy.CloseCallCount);
        Assert.NotNull(dialogProxy.ClosedResult);
        Assert.False(dialogProxy.ClosedResult!.Canceled);
    }

    [Fact]
    public async Task PurgeHistoryDialog_ExecutePurgeAsync_WhenSuccessWithPurgeId_SetsActivePurgeIdAndClosesDialog()
    {
        var roomService = DispatchProxy.Create<IRoomService, StubRoomServiceProxy>();
        var roomProxy = (StubRoomServiceProxy)(object)roomService;
        roomProxy.PurgeHandler = (_, _, _) => Task.FromResult(OperationResult<string>.Ok("purge-42", "PurgeStarted"));

        var snackbar = DispatchProxy.Create<ISnackbar, RecordingSnackbarProxy>();
        var snackbarProxy = (RecordingSnackbarProxy)(object)snackbar;

        var mudDialog = DispatchProxy.Create<IMudDialogInstance, RecordingDialogProxy>();
        var dialogProxy = (RecordingDialogProxy)(object)mudDialog;

        using var dialog = CreateDialog("!room:example.com", roomService, snackbar, mudDialog);

        await dialog.ExecutePurgeAsync(new PurgeHistoryViewModel { PurgeUpToEventId = "$event1" });

        var activeCall = Assert.Single(roomProxy.SetActivePurgeCalls);
        Assert.Equal("!room:example.com", activeCall.RoomId);
        Assert.Equal("purge-42", activeCall.PurgeId);
        var notification = Assert.Single(snackbarProxy.AddedMessages);
        Assert.Equal("PurgeStarted", notification.Message);
        Assert.Equal(Severity.Success, notification.Severity);
        Assert.Equal(1, dialogProxy.CloseCallCount);
        Assert.Equal("purge-42", dialogProxy.ClosedResult?.Data);
    }

    [Fact]
    public async Task RoomService_PurgeRoomHistoryAsync_WhenGatewayReturnsEmptyPurgeId_ReturnsSuccessWithoutCachingEmptyId()
    {
        var gateway = DispatchProxy.Create<IMatrixGateway, StubPurgeGatewayProxy>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var roomService = new RoomService(
            new StubSessionService(gateway),
            NullLogger<RoomService>.Instance,
            new StubStringLocalizer(),
            cache);

        var result = await roomService.PurgeRoomHistoryAsync(
            "!room:example.com",
            new PurgeHistoryViewModel { PurgeUpToEventId = "$event1" });

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Data);
        Assert.Equal("PurgeStarted", result.Message);
        Assert.Null(roomService.GetActivePurgeId("!room:example.com"));
    }

#pragma warning disable BL0005
    private static PurgeHistoryDialog CreateDialog(
        string roomId,
        IRoomService roomService,
        ISnackbar snackbar,
        IMudDialogInstance mudDialog) => new()
    {
        RoomId = roomId,
        RoomService = roomService,
        Snackbar = snackbar,
        MudDialog = mudDialog
    };
#pragma warning restore BL0005

    public class StubRoomServiceProxy : DispatchProxy
    {
        public Func<string, PurgeHistoryViewModel, CancellationToken, Task<OperationResult<string>>>? PurgeHandler { get; set; }
        public List<(string RoomId, string PurgeId)> SetActivePurgeCalls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IRoomService.PurgeRoomHistoryAsync) && args is [string roomId, PurgeHistoryViewModel req, CancellationToken ct])
            {
                return PurgeHandler!(roomId, req, ct);
            }

            if (targetMethod?.Name == nameof(IRoomService.SetActivePurgeId) && args is [string rId, string pId])
            {
                SetActivePurgeCalls.Add((rId, pId));
                return null;
            }

            throw new NotSupportedException($"Unexpected call: {targetMethod?.Name}");
        }
    }

    public class RecordingSnackbarProxy : DispatchProxy
    {
        public List<(string Message, Severity Severity)> AddedMessages { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISnackbar.Add) && args is { Length: >= 2 } && args[0] is string msg && args[1] is Severity sev)
            {
                AddedMessages.Add((msg, sev));
            }

            return null;
        }
    }

    public class RecordingDialogProxy : DispatchProxy
    {
        public DialogResult? ClosedResult { get; private set; }
        public int CloseCallCount { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IMudDialogInstance.Close))
            {
                CloseCallCount++;
                ClosedResult = args is { Length: > 0 } ? args[0] as DialogResult : DialogResult.Ok<object?>(null);
            }

            return null;
        }
    }

    public class StubPurgeGatewayProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IMatrixGateway.HomeserverUrl)}")
            {
                return "https://matrix.example.com";
            }

            if (targetMethod?.Name == nameof(IMatrixGateway.PurgeRoomHistoryAsync))
            {
                return Task.FromResult<SynapseAdminPurgeHistoryResponse?>(new SynapseAdminPurgeHistoryResponse { PurgeId = string.Empty });
            }

            throw new NotSupportedException($"Unexpected gateway call: {targetMethod?.Name}");
        }
    }

    private sealed class StubSessionService(IMatrixGateway gateway) : IMatrixSessionService
    {
        public IMatrixGateway? Gateway => gateway;
        public bool IsLoggedIn => true;
        public Task<OperationResult> LoginAsync(string homeserver, string username, string password) => throw new NotImplementedException();
        public Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken) => throw new NotImplementedException();
        public Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false) => throw new NotImplementedException();
        public Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Logout() => throw new NotImplementedException();
    }

    private sealed class StubStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments), resourceNotFound: false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
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
