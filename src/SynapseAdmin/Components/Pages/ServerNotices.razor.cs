using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class ServerNotices
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
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
            
            if (result.Success)
            {
                Snackbar.Add("Server notice sent successfully!", Severity.Success);
                
                // Reset form
                noticeMessage = string.Empty;
                targetUserId = string.Empty;
                if (form != null)
                {
                    await form.ResetAsync();
                }
            }
            else
            {
                Snackbar.Add(result.ErrorMessage ?? "Failed to send notice", Severity.Error);
            }

            isSending = false;
            StateHasChanged();
        }
    }
}
