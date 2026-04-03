using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDetails
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
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
                L["DeleteRoomTitle"], 
                L["DeleteRoomConfirmation"], 
                yesText: L["Delete"], cancelText: L["Cancel"]);
            
            if (confirmed == true)
            {
                var result = await RoomService.DeleteRoomAsync(RoomId, block: false, purge: true);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task DeleteAndBlockRoom()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteAndBlockRoomTitle"], 
                L["DeleteAndBlockRoomConfirmation"], 
                yesText: L["DeleteAndBlock"], cancelText: L["Cancel"]);
            
            if (confirmed == true)
            {
                var result = await RoomService.DeleteRoomAsync(RoomId, block: true, purge: true);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task QuarantineMedia()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"], 
                L["QuarantineRoomMediaConfirmation"], 
                yesText: L["Quarantine"], cancelText: L["Cancel"]);
            
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
