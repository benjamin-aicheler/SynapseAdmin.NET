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
            AvatarUrl = room.AvatarUrl ?? "",
            JoinRules = room.JoinRules,
            RoomType = room.RoomType ?? "",
            IsTombstoned = room.Tombstoned == true,
            ReplacementRoom = room.ReplacementRoom
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
            AvatarUrl = room.AvatarUrl ?? "",
            JoinRules = room.JoinRules,
            GuestAccess = room.GuestAccess,
            HistoryVisibility = room.HistoryVisibility,
            RoomType = room.RoomType ?? "",
            Forgotten = room.Forgotten == true,
            IsTombstoned = room.Tombstoned == true,
            ReplacementRoom = room.ReplacementRoom
        };
    }
}
