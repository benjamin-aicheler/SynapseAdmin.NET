using Microsoft.AspNetCore.Components;
using MudBlazor;
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
        private bool isLoading = true;
        private string? _loadedRoomId;
        private readonly CancellationTokenSource _cts = new();

        protected override async Task OnParametersSetAsync()
        {
            if (!string.IsNullOrEmpty(RoomId) && RoomId != _loadedRoomId)
            {
                _loadedRoomId = RoomId;
                isLoading = true;
                var result = await RoomService.GetRoomDetailsAsync(RoomId, _cts.Token);
                if (result.Success && result.Data != null)
                {
                    roomName = result.Data.Name ?? result.Data.CanonicalAlias ?? result.Data.RoomId;
                    roomMedia = result.Data.Media;
                }
                else if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
                isLoading = false;
            }
        }

        private void GoBackToRoom()
        {
            Navigation.NavigateTo($"/rooms/{Uri.EscapeDataString(RoomId)}");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
