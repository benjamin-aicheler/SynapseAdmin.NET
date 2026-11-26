using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Shared;

/// <summary>
/// The Devices tab of the user page (#598): lists the user's devices, renames one and deletes one or several. The
/// device of this session's access token can't be deleted, nor any device of the signed-in admin's own account while
/// that device is unknown, because deleting it would sign the app out. Every action confirms the user and device IDs
/// and re-checks afterwards that the panel still shows that user.
/// </summary>
public partial class UserDevicesPanel : IDisposable
{
    [Inject] public IUserDeviceService DeviceService { get; set; } = null!;
    [Inject] public IMatrixSessionService MatrixSession { get; set; } = null!;
    [Inject] public IDialogService DialogService { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public ITimeZoneService TimeZoneService { get; set; } = null!;

    /// <summary>The ID of the user the page shows (loaded, not the route parameter).</summary>
    [Parameter] public string UserId { get; set; } = string.Empty;

    /// <summary>The user as the page names it in its confirmations.</summary>
    [Parameter] public string Target { get; set; } = string.Empty;

    private List<UserDeviceViewModel>? _devices;
    private HashSet<UserDeviceViewModel> _selected = [];
    private bool _isLoading;
    private string? _loadedUserId;
    private CancellationTokenSource? _loadCts;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedUserId == UserId && _loadCts != null) return;

        // Reset before the first await, so the previous user's devices are never shown for the new one.
        _loadedUserId = UserId;
        _devices = null;
        _selected = [];
        await LoadAsync(UserId);
    }

    private Task Reload() => _loadedUserId is { } userId ? LoadAsync(userId) : Task.CompletedTask;

    private async Task LoadAsync(string userId)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var token = _loadCts.Token;
        _isLoading = true;

        var result = await DeviceService.GetUserDevicesAsync(userId, token);
        // Discard an answer for a user the panel no longer shows, or one a newer load replaces.
        if (token.IsCancellationRequested || userId != _loadedUserId) return;

        _devices = result.Success ? result.Data ?? [] : null;
        _selected = [];
        _isLoading = false;
        Snackbar.ShowFailure(result);
    }

    /// <summary>The user whose devices are shown, or <c>null</c> while none are.</summary>
    private string? GetDisplayedUser() =>
        !_disposed && _devices != null && !_isLoading && _loadedUserId == UserId ? _loadedUserId : null;

    /// <summary>Re-checks after an awaited dialog that the panel still shows <paramref name="userId"/>.</summary>
    private bool IsStillDisplayed(string userId)
    {
        if (!_disposed && _loadedUserId == userId && UserId == userId) return true;
        Snackbar.Add(L["SelectionChangedActionAborted"], Severity.Warning);
        return false;
    }

    private bool IsOwnAccount => MatrixSession.Gateway is { } gateway && string.Equals(UserId, gateway.UserId, StringComparison.Ordinal);

    /// <summary>Whether this is the device of this session's access token.</summary>
    private bool IsSessionDevice(UserDeviceViewModel device) =>
        IsOwnAccount && MatrixSession.Gateway?.DeviceId is { } own && string.Equals(device.DeviceId, own, StringComparison.Ordinal);

    /// <summary>Whether deleting the device could sign the app out (the service refuses it as well).</summary>
    private bool IsProtected(UserDeviceViewModel device) =>
        MatrixSession.Gateway is { } gateway && UserDeviceService.IncludesOwnSessionDevice(gateway, UserId, [device.DeviceId]);

    private bool CanDeleteSelected => GetDisplayedUser() != null && _selected.Count > 0 && !_selected.Any(IsProtected);

    private string? ProtectionNote => IsOwnAccount
        ? MatrixSession.Gateway?.DeviceId == null ? L["OwnSessionDeviceUnknownNote"].Value : L["OwnSessionDeviceNote"].Value
        : null;

    private string FormatTime(DateTimeOffset? timestamp) =>
        timestamp is { } t ? TimeZoneService.ToLocal(t).DateTime.ToString("g") : L["UnknownValue"].Value;

    private async Task RenameDevice(UserDeviceViewModel device)
    {
        if (GetDisplayedUser() is not { } userId) return;

        var parameters = new DialogParameters
        {
            { nameof(RenameDeviceDialog.Target), Target },
            { nameof(RenameDeviceDialog.DeviceId), device.DeviceId },
            { nameof(RenameDeviceDialog.CurrentName), device.DisplayName }
        };
        var dialog = await DialogService.ShowAsync<RenameDeviceDialog>(L["RenameDevice"], parameters, DialogPresets.Small());
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: string newName } && IsStillDisplayed(userId))
        {
            await RunAndReload(userId, DeviceService.RenameUserDeviceAsync(userId, device.DeviceId, newName, _cts.Token));
        }
    }

    private Task DeleteDevice(UserDeviceViewModel device) => DeleteDevices([device]);

    private Task DeleteSelected() => DeleteDevices(_selected.ToList());

    private async Task DeleteDevices(IReadOnlyList<UserDeviceViewModel> devices)
    {
        if (GetDisplayedUser() is not { } userId || devices.Count == 0 || devices.Any(IsProtected)) return;

        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var question = deviceIds.Count == 1
            ? L["DeleteDeviceConfirmation", Target, deviceIds[0]]
            : L["DeleteDevicesConfirmation", Target, string.Join(", ", deviceIds), deviceIds.Count];
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            deviceIds.Count == 1 ? L["DeleteDevice"] : L["DeleteDevices"],
            $"{question} {L["DeviceDeletionHint"]}",
            yesText: L["Delete"], cancelText: L["Cancel"]);

        if (confirmed == true && IsStillDisplayed(userId))
        {
            await RunAndReload(userId, DeviceService.DeleteUserDevicesAsync(userId, deviceIds, _cts.Token));
        }
    }

    private async Task RunAndReload(string userId, Task<OperationResult> action)
    {
        var result = await action;
        Snackbar.ShowResult(result);
        if (result.Success && !_disposed && _loadedUserId == userId)
        {
            await LoadAsync(userId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _loadCts?.Dispose();
    }
}
