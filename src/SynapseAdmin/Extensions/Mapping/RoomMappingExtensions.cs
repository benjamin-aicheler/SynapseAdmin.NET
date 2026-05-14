using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Extensions.Mapping;

public static class RoomMappingExtensions
{
    public static RoomListViewModel ToViewModel(this SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom room)
    {
        return new RoomListViewModel
        {
            RoomId = room.RoomId,
            Name = room.Name,
            CanonicalAlias = room.CanonicalAlias,
            JoinedMembers = room.JoinedMembers,
            JoinedLocalMembers = room.JoinedLocalMembers,
            Version = room.Version ?? "1",
            Creator = room.Creator ?? "",
            Encryption = room.Encryption,
            Federated = room.Federatable,
            Public = room.Public,
            AvatarUrl = "",
            JoinRules = room.JoinRules,
            RoomType = ""
        };
    }

    public static List<RoomListViewModel> ToViewModels(this IEnumerable<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom> rooms)
    {
        return rooms.Select(ToViewModel).ToList();
    }

    public static RoomDetailViewModel ToDetailViewModel(this SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom room)
    {
        return new RoomDetailViewModel
        {
            RoomId = room.RoomId,
            Name = room.Name,
            CanonicalAlias = room.CanonicalAlias,
            JoinedMembers = room.JoinedMembers,
            JoinedLocalMembers = room.JoinedLocalMembers,
            Version = room.Version ?? "1",
            Creator = room.Creator ?? "",
            Encryption = room.Encryption,
            Federated = room.Federatable,
            Public = room.Public,
            AvatarUrl = "",
            JoinRules = room.JoinRules,
            GuestAccess = room.GuestAccess,
            HistoryVisibility = room.HistoryVisibility,
            RoomType = "",
            Forgotten = false
        };
    }
}
