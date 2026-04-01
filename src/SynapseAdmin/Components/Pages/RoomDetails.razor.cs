using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDetails
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public RoomService RoomService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        [Parameter]
        public string RoomId { get; set; } = string.Empty;

        private RoomDetailViewModel? room;

        protected override async Task OnParametersSetAsync()
        {
            await LoadRoomDetails();
        }

        private async Task LoadRoomDetails()
        {
            try
            {
                room = await RoomService.GetRoomDetailsAsync(RoomId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching room details: {ex.Message}");
                Snackbar.Add($"Error fetching room details: {ex.Message}", Severity.Error);
            }
        }

        private async Task DeleteRoom()
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Delete Room", 
                "Are you sure you want to delete this room? This will NOT block it. This action cannot be undone.", 
                yesText: "Delete", cancelText: "Cancel");
            
            if (result == true)
            {
                try {
                    await RoomService.DeleteRoomAsync(RoomId, block: false, purge: true);
                    Snackbar.Add("Room deletion initiated successfully.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error deleting room: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task DeleteAndBlockRoom()
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Delete & Block Room", 
                "Are you sure you want to delete AND block this room? This action cannot be undone.", 
                yesText: "Delete & Block", cancelText: "Cancel");
            
            if (result == true)
            {
                try {
                    await RoomService.DeleteRoomAsync(RoomId, block: true, purge: true);
                    Snackbar.Add("Room deletion and blocking initiated successfully.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error deleting and blocking room: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task QuarantineMedia()
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Quarantine Media", 
                "Are you sure you want to quarantine all media in this room?", 
                yesText: "Quarantine", cancelText: "Cancel");
            
            if (result == true)
            {
                try {
                    await RoomService.QuarantineMediaAsync(RoomId);
                    Snackbar.Add("Room media quarantined successfully.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error quarantining media: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task BlockRoom()
        {
            try {
                await RoomService.BlockRoomAsync(RoomId, true);
                Snackbar.Add("Room blocked successfully.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error blocking room: {ex.Message}", Severity.Error);
            }
        }
    }
}
