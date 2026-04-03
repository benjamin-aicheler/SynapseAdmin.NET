using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class ServerNotices
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IUserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudForm? form;
        private bool success;
        private bool isSending = false;
        
        private string targetUserId = string.Empty;
        private string noticeMessage = string.Empty;

        private async Task SendNotice()
        {
            isSending = true;
            StateHasChanged();
            
            var result = await UserService.SendServerNoticeAsync(targetUserId, noticeMessage);
            
            Snackbar.Add(result.Message, result.Severity);
            
            if (result.Success)
            {
                // Reset form
                noticeMessage = string.Empty;
                targetUserId = string.Empty;
                if (form != null)
                {
                    await form.ResetAsync();
                }
            }

            isSending = false;
            StateHasChanged();
        }
    }
}
