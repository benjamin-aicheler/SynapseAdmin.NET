using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class ServerNotices
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
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
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                isSending = true;
                StateHasChanged();
                
                try
                {
                    var content = new RoomMessageEventContent(body: noticeMessage);
                    
                    await synapseAdmin.SendServerNoticeAsync(targetUserId, content);
                    
                    Snackbar.Add("Server notice sent successfully!", Severity.Success);
                    
                    // Reset form
                    noticeMessage = string.Empty;
                    targetUserId = string.Empty;
                    if (form != null)
                    {
                        await form.ResetAsync();
                    }
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Failed to send notice: {ex.Message}", Severity.Error);
                }
                finally
                {
                    isSending = false;
                    StateHasChanged();
                }
            }
        }
    }
}