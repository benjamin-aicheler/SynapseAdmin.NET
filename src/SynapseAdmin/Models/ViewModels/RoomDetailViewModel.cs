using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec.State.RoomInfo;

namespace SynapseAdmin.Models.ViewModels;

public class RoomDetailViewModel
{
    public SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom Details { get; set; } = null!;
    public SynapseAdminRoomMemberListResult? Members { get; set; }
    public SynapseAdminRoomStateResult? StateEvents { get; set; }
    public RoomTombstoneEventContent? Tombstone { get; set; }
}
