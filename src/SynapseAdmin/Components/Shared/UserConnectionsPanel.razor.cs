using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Shared;

/// <summary>
/// The Recent connections tab of the user page (#598): the IP addresses and user agents the homeserver recorded for the
/// user, newest first. Read-only.
/// </summary>
public partial class UserConnectionsPanel : IDisposable
{
    [Inject] public IUserDeviceService DeviceService { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public ITimeZoneService TimeZoneService { get; set; } = null!;

    /// <summary>The ID of the user the page shows (loaded, not the route parameter).</summary>
    [Parameter] public string UserId { get; set; } = string.Empty;

    private List<UserConnectionViewModel>? _connections;
    private bool _isLoading;
    private string? _loadedUserId;
    private CancellationTokenSource? _loadCts;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedUserId == UserId && _loadCts != null) return;

        // Reset before the first await, so the previous user's connections are never shown for the new one.
        _loadedUserId = UserId;
        _connections = null;
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

        var result = await DeviceService.GetUserConnectionsAsync(userId, token);
        // Discard an answer for a user the panel no longer shows, or one a newer load replaces.
        if (token.IsCancellationRequested || userId != _loadedUserId) return;

        _connections = result.Success ? result.Data ?? [] : null;
        _isLoading = false;
        Snackbar.ShowFailure(result);
    }

    private string FormatTime(DateTimeOffset? timestamp) =>
        timestamp is { } t ? TimeZoneService.ToLocal(t).DateTime.ToString("g") : L["UnknownValue"].Value;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _loadCts?.Dispose();
    }
}
