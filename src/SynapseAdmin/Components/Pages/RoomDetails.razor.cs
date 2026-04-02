using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDetails
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IRoomService RoomService { get; set; } = null!;
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
            var result = await RoomService.GetRoomDetailsAsync(RoomId);
            if (result.Success)
            {
                room = result.Data;
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task DeleteRoom()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete Room", 
                "Are you sure you want to delete this room? This will NOT block it. This action cannot be undone.", 
                yesText: "Delete", cancelText: "Cancel");
            
            if (confirmed == true)
            {
                var result = await RoomService.DeleteRoomAsync(RoomId, block: false, purge: true);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task DeleteAndBlockRoom()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete & Block Room", 
                "Are you sure you want to delete AND block this room? This action cannot be undone.", 
                yesText: "Delete & Block", cancelText: "Cancel");
            
            if (confirmed == true)
            {
                var result = await RoomService.DeleteRoomAsync(RoomId, block: true, purge: true);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task QuarantineMedia()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Quarantine Media", 
                "Are you sure you want to quarantine all media in this room?", 
                yesText: "Quarantine", cancelText: "Cancel");
            
            if (confirmed == true)
            {
                var result = await RoomService.QuarantineMediaAsync(RoomId);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task BlockRoom()
        {
            var result = await RoomService.BlockRoomAsync(RoomId, true);
            Snackbar.Add(result.Message, result.Severity);
        }
    }
}
