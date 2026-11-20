using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomGallery : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject] public IRoomService RoomService { get; set; } = null!;
        [Inject] public NavigationManager Navigation { get; set; } = null!;
        [Inject] public ISnackbar Snackbar { get; set; } = null!;

        [Parameter] public string RoomId { get; set; } = string.Empty;

        private string? roomName;
        private RoomMediaViewModel? roomMedia;
        private bool roomLoaded;
        private string? errorMessage;
        private bool isLoading = true;
        private string? _loadedRoomId;
        private readonly CancellationTokenSource _cts = new();

        // Blazor reuses this page when only RoomId changes. `_loadCts` is replaced on every room change, so the
        // previous room's load is cancelled and its late answer is discarded (#495).
        private CancellationTokenSource? _loadCts;

        protected override async Task OnParametersSetAsync()
        {
            if (!string.IsNullOrEmpty(RoomId) && RoomId != _loadedRoomId)
            {
                var roomId = RoomId;
                _loadedRoomId = roomId;
                var token = ResetLoadToken();
                isLoading = true;
                roomName = null;
                roomMedia = null;
                roomLoaded = false;
                errorMessage = null;
                var result = await RoomService.GetRoomDetailsAsync(roomId, token);
                // Discard the answer for a room that is no longer current.
                if (token.IsCancellationRequested || roomId != _loadedRoomId) return;

                if (result.Success && result.Data != null)
                {
                    roomName = result.Data.Name ?? result.Data.CanonicalAlias ?? result.Data.RoomId;
                    roomMedia = result.Data.Media;
                    roomLoaded = true;
                }
                else if (Snackbar.ShowResult(result))
                {
                    errorMessage = result.Message;
                }
                isLoading = false;
            }
        }

        private CancellationToken ResetLoadToken()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            return _loadCts.Token;
        }

        private void GoBackToRoom()
        {
            Navigation.NavigateTo($"/rooms/{Uri.EscapeDataString(RoomId)}");
        }

        public void Dispose()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
