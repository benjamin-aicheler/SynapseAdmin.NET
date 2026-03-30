using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDetails
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        [Parameter]
        public string RoomId { get; set; } = string.Empty;

        private SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom? roomDetails;
        private SynapseAdminRoomMemberListResult? members;
        private SynapseAdminRoomStateResult? stateEvents;
        private RoomTombstoneEventContent? tombstone;

        protected override async Task OnParametersSetAsync()
        {
            await LoadRoomDetails();
        }

        private async Task LoadRoomDetails()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                try
                {
                    // Note: Blazor unescapes parameters by default, so we may need to escape it again for the API call
                    var encodedRoomId = Uri.EscapeDataString(RoomId);
                    
                    roomDetails = await synapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>($"/_synapse/admin/v1/rooms/{encodedRoomId}");
                    
                    var membersTask = synapseAdmin.Admin.GetRoomMembersAsync(RoomId);
                    var stateTask = synapseAdmin.Admin.GetRoomStateAsync(RoomId);
                    
                    await Task.WhenAll(membersTask, stateTask);
                    
                    members = membersTask.Result;
                    stateEvents = stateTask.Result;

                    tombstone = stateEvents?.Events
                        .FirstOrDefault(x => x.Type == RoomTombstoneEventContent.EventId)?
                        .ContentAs<RoomTombstoneEventContent>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching room details: {ex.Message}");
                    Snackbar.Add($"Error fetching room details: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task DeleteRoom()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                bool? result = await DialogService.ShowMessageBoxAsync(
                    "Delete Room", 
                    "Are you sure you want to delete this room? This will NOT block it. This action cannot be undone.", 
                    yesText: "Delete", cancelText: "Cancel");
                
                if (result == true)
                {
                    try {
                        var req = new SynapseAdminRoomDeleteRequest
                        {
                            Block = false,
                            Purge = true
                        };
                        await synapseAdmin.Admin.DeleteRoom(RoomId, req);
                        Snackbar.Add("Room deletion initiated successfully.", Severity.Success);
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Add($"Error deleting room: {ex.Message}", Severity.Error);
                    }
                }
            }
        }

        private async Task DeleteAndBlockRoom()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                bool? result = await DialogService.ShowMessageBoxAsync(
                    "Delete & Block Room", 
                    "Are you sure you want to delete AND block this room? This action cannot be undone.", 
                    yesText: "Delete & Block", cancelText: "Cancel");
                
                if (result == true)
                {
                    try {
                        var req = new SynapseAdminRoomDeleteRequest
                        {
                            Block = true,
                            Purge = true
                        };
                        await synapseAdmin.Admin.DeleteRoom(RoomId, req);
                        Snackbar.Add("Room deletion and blocking initiated successfully.", Severity.Success);
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Add($"Error deleting and blocking room: {ex.Message}", Severity.Error);
                    }
                }
            }
        }

        private async Task QuarantineMedia()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                bool? result = await DialogService.ShowMessageBoxAsync(
                    "Quarantine Media", 
                    "Are you sure you want to quarantine all media in this room?", 
                    yesText: "Quarantine", cancelText: "Cancel");
                
                if (result == true)
                {
                    try {
                        await synapseAdmin.Admin.QuarantineMediaByRoomId(RoomId);
                        Snackbar.Add("Room media quarantined successfully.", Severity.Success);
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Add($"Error quarantining media: {ex.Message}", Severity.Error);
                    }
                }
            }
        }

        private async Task BlockRoom()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                try {
                    await synapseAdmin.Admin.BlockRoom(RoomId, true);
                    Snackbar.Add("Room blocked successfully.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error blocking room: {ex.Message}", Severity.Error);
                }
            }
        }
    }
}