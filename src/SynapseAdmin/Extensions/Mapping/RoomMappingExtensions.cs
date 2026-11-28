using System.Text.Json;
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

    public static RoomBlockStatusViewModel ToViewModel(this SynapseAdminRoomBlockStatusResponse status) => new()
    {
        IsBlocked = status.Block,
        BlockedBy = status.UserId ?? string.Empty
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
    /// <summary>
    /// A room event as a message row: <c>Content</c> is the raw JSON of <c>content</c>, and <c>Body</c> its <c>body</c>.
    /// A <c>body</c> that isn't a string throws, as <see cref="JsonElement.GetString"/> does.
    /// </summary>
    public static RoomMessageItemViewModel ToMessageItemViewModel(this SynapseAdminEvent evt)
    {
        string? content = null;
        string? body = null;
        if (evt.Content is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } contentJson)
        {
            if (contentJson.ValueKind == JsonValueKind.Object && contentJson.TryGetProperty("body", out var bodyProp))
            {
                body = bodyProp.GetString();
            }
            content = contentJson.GetRawText();
        }

        return new RoomMessageItemViewModel
        {
            EventId = evt.EventId,
            Sender = evt.Sender,
            OriginServerTs = DateTimeOffset.FromUnixTimeMilliseconds(evt.OriginServerTs),
            Type = evt.Type,
            StateKey = evt.StateKey,
            Content = content,
            Body = body
        };
    }

    public static List<RoomMessageItemViewModel> ToMessageItemViewModels(this IEnumerable<SynapseAdminEvent> events)
    {
        return events.Select(ToMessageItemViewModel).ToList();
    }

    /// <summary>
    /// A context's events newest first, as the Messages tab lists them: the events after (which the API returns oldest
    /// first) in reverse, the event itself, then the events before (which it returns newest first).
    /// </summary>
    public static IEnumerable<SynapseAdminEvent> NewestFirst(this SynapseAdminRoomMessageContextResponse context)
    {
        var events = context.EventsAfter.AsEnumerable().Reverse();
        if (context.Event != null) events = events.Append(context.Event);
        return events.Concat(context.EventsBefore);
    }

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
