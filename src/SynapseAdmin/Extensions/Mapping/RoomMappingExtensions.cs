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
            RoomType = room.RoomType ?? ""
        };
    }

    /// <summary>
    /// Synapse sends the status in lower case; any other value is <see cref="RoomDeletionState.Unknown"/>, because the
    /// status API is experimental (#518). The error text isn't copied: it goes only to the log.
    /// </summary>
    public static RoomDeletionStatusViewModel ToViewModel(this SynapseAdminRoomDeleteStatusResponse status) => new()
    {
        DeleteId = status.DeleteId ?? string.Empty,
        RoomId = status.RoomId ?? string.Empty,
        State = status.Status switch
        {
            "scheduled" => RoomDeletionState.Scheduled,
            "active" => RoomDeletionState.Active,
            "complete" => RoomDeletionState.Complete,
            "failed" => RoomDeletionState.Failed,
            _ => RoomDeletionState.Unknown
        }
    };

    public static List<RoomListViewModel> ToViewModels(this IEnumerable<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom> rooms)
    {
        return rooms.Select(ToViewModel).ToList();
    }

    public static PublicRoomDirectoryViewModel ToViewModel(this MatrixPublicRoomDirectoryResult result)
    {
        return new PublicRoomDirectoryViewModel
        {
            NextBatch = result.NextBatch,
            PrevBatch = result.PrevBatch,
            TotalRoomCountEstimate = result.TotalRoomCountEstimate,
            Chunk = result.Chunk.Select(c => new PublicRoomItemViewModel
            {
                AvatarUrl = c.AvatarUrl,
                CanonicalAlias = c.CanonicalAlias,
                GuestCanJoin = c.GuestCanJoin,
                JoinRule = c.JoinRule,
                Name = c.Name,
                NumJoinedMembers = c.NumJoinedMembers,
                RoomId = c.RoomId,
                Topic = c.Topic,
                WorldReadable = c.WorldReadable
            }).ToList()
        };
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

    /// <summary>
    /// Copies the 6 metadata fields of <paramref name="meta"/> onto <paramref name="vm"/>, reading a missing
    /// <c>safe_from_quarantine</c> as false. The view model keeps its ID (the MXC URI) and its local/remote source.
    /// </summary>
    public static void ApplyMetadata(this RoomMediaItemViewModel vm, SynapseAdminMediaMetadataResponse.MediaInfo meta)
    {
        vm.UploadName = meta.UploadName;
        vm.MediaType = meta.MediaType;
        vm.MediaLength = meta.MediaLength;
        vm.CreatedTimestamp = meta.CreatedTimestamp;
        vm.QuarantinedBy = meta.QuarantinedBy;
        vm.SafeFromQuarantine = meta.SafeFromQuarantine ?? false;
    }
}
