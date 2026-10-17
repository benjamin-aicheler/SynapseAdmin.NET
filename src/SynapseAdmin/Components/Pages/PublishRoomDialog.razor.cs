using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Components.Pages
{
    public partial class PublishRoomDialog : IDisposable
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
        private readonly CancellationTokenSource _cts = new();

        private void Cancel() => MudDialog.Cancel();

        private async Task Submit()
        {
            if (form == null) return;
            await form.ValidateAsync();
            if (!form.IsValid) return;

            loading = true;
            StateHasChanged();

            var result = await RoomService.SetRoomDirectoryVisibilityAsync(roomId, visibility, _cts.Token);

            loading = false;
            StateHasChanged();

            if (result.Success)
            {
                Snackbar.Add(result.Message, Severity.Success);
                MudDialog.Close(DialogResult.Ok(true));
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
