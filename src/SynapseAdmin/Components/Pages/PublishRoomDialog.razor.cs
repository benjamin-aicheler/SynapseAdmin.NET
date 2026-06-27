using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Components.Pages
{
    public partial class PublishRoomDialog
    {
        [CascadingParameter]
        private IMudDialogInstance MudDialog { get; set; } = null!;

        [Inject]
        public IRoomService RoomService { get; set; } = null!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudForm? form;
        private bool success;
        private bool loading;

        private string roomId = string.Empty;
        private string visibility = "public";

        private void Cancel() => MudDialog.Cancel();

        private async Task Submit()
        {
            if (form == null) return;
            await form.ValidateAsync();
            if (!form.IsValid) return;

            loading = true;
            StateHasChanged();

            var result = await RoomService.SetRoomDirectoryVisibilityAsync(roomId, visibility);

            loading = false;
            StateHasChanged();

            if (result.Success)
            {
                Snackbar.Add(L["RoomDirectoryVisibilityUpdated"], Severity.Success);
                MudDialog.Close(DialogResult.Ok(true));
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }
    }
}
