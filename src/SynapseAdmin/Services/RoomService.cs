using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using System.Text.Json;
using System.Text.Json.Nodes;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Services;

public class RoomService(IMatrixSessionService sessionService, ILogger<RoomService> logger, IStringLocalizer<SharedResources> L, IMemoryCache memoryCache) : IRoomService
{
    private static readonly MemoryCacheEntryOptions RoomNameCacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(30),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
    };

    private static readonly MemoryCacheEntryOptions ActivePurgeCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    private IMatrixGateway? Gateway => sessionService.Gateway;

    private string? HomeserverCacheScope => Gateway?.HomeserverUrl?.TrimEnd('/').ToLowerInvariant();

    private string GetRoomNameCacheKey(string roomId) =>
        $"room_name:{HomeserverCacheScope}:{roomId}";

    private void CacheRoomName(string roomId, string? name)
    {
        if (Gateway == null || string.IsNullOrEmpty(roomId)) return;
        var resolvedName = string.IsNullOrEmpty(name) ? roomId : name;
        memoryCache.Set(GetRoomNameCacheKey(roomId), resolvedName, RoomNameCacheOptions);
    }

    public Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomList",
            ex => logger.LogError(ex, "Error fetching room list (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})", offset, limit, searchTerm.SanitizeForLogging()),
            async gateway =>
            {
                if (orderBy == "room_id") orderBy = "alphabetical";
                var dir = direction.ToSynapseDir(whenUnsorted: "f");
                var result = await gateway.GetRoomListAsync(offset, limit, orderBy, dir, searchTerm, token);
                if (result == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((0, []));

                foreach (var room in result.Rooms)
                {
                    CacheRoomName(room.RoomId, room.Name);
                }

                var vms = result.Rooms.ToViewModels();

                return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((result.TotalRooms, vms));
            });

    public Task<OperationResult<RoomDetailViewModel>> GetRoomDetailsAsync(string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomDetails",
            ex => logger.LogError(ex, "Error fetching room details for {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var r = await gateway.GetRoomDetailsAsync(roomId, token);

                if (r == null) return OperationResult<RoomDetailViewModel>.Failure(L["RoomNotFound"]);

                CacheRoomName(roomId, r.Name);

                var membersTask = gateway.GetRoomMembersAsync(roomId, token);
                var stateTask = gateway.GetRoomStateAsync(roomId, cancellationToken: token);
                var mediaTask = gateway.GetRoomMediaListAsync(roomId, token);

                await Task.WhenAll(membersTask, stateTask, mediaTask);

                var members = await membersTask;
                var stateEvents = await stateTask;
                var media = await mediaTask;

                var vm = r.ToDetailViewModel();
                ApplyTombstone(vm, stateEvents);
                vm.Members = MapMembers(members, stateEvents);
                vm.StateEvents = MapStateEvents(stateEvents);
                vm.Media = media == null ? null : new RoomMediaViewModel
                {
                    Local = UniqueMxcs(media.Local).Select(m => new RoomMediaItemViewModel { MediaId = m, IsLocal = true }).ToList(),
                    Remote = UniqueMxcs(media.Remote).Select(m => new RoomMediaItemViewModel { MediaId = m, IsLocal = false }).ToList()
                };

                return OperationResult<RoomDetailViewModel>.Ok(vm);
            });

    private static void ApplyTombstone(RoomDetailViewModel vm, SynapseAdminRoomStateResult? stateEvents)
    {
        if (!vm.IsTombstoned || string.IsNullOrEmpty(vm.ReplacementRoom))
        {
            var tombstone = stateEvents?.Events
                .FirstOrDefault(x => x.Type == "m.room.tombstone");
            if (tombstone != null)
            {
                vm.IsTombstoned = true;
                if (tombstone.RawContent != null && tombstone.RawContent.TryGetPropertyValue("replacement_room", out var replacementNode))
                {
                    vm.ReplacementRoom = GetSafeString(replacementNode);
                }
            }
        }
    }

    private static List<RoomMemberViewModel> MapMembers(SynapseAdminRoomMemberListResult? members, SynapseAdminRoomStateResult? stateEvents)
    {
        var memberStateEvents = stateEvents?.Events
            .Where(e => e.Type == "m.room.member" && !string.IsNullOrEmpty(e.StateKey))
            .GroupBy(e => e.StateKey!)
            .ToDictionary(g => g.Key, g => g.First());

        return members?.Members.Select(userId =>
        {
            var memberVm = new RoomMemberViewModel { UserId = userId };
            if (memberStateEvents == null || !memberStateEvents.TryGetValue(userId, out var memberEvent) || memberEvent.RawContent == null)
            {
                return memberVm;
            }

            if (memberEvent.RawContent.TryGetPropertyValue("displayname", out var dnNode))
            {
                memberVm.DisplayName = GetSafeString(dnNode);
            }
            if (memberEvent.RawContent.TryGetPropertyValue("avatar_url", out var avNode))
            {
                memberVm.AvatarUrl = GetSafeString(avNode);
            }
            if (memberEvent.RawContent.TryGetPropertyValue("membership", out var msNode))
            {
                memberVm.Membership = GetSafeString(msNode);
            }
            return memberVm;
        }).ToList() ?? [];
    }

    private static List<RoomStateEventViewModel> MapStateEvents(SynapseAdminRoomStateResult? stateEvents) =>
        stateEvents?.Events.Select(e => new RoomStateEventViewModel
        {
            Type = e.Type,
            StateKey = e.StateKey,
            Sender = e.Sender,
            RawContent = e.RawContent?.ToJsonString()
        }).ToList() ?? [];

    /// <summary>
    /// Synapse's room media list contains one entry per event reference, so the same MXC URI can
    /// appear several times (issue #384). Keeps the first occurrence of each URI, in order.
    /// URIs are compared ordinally as a whole: media IDs are case-sensitive and only unique per server.
    /// </summary>
    private static List<string> UniqueMxcs(IEnumerable<string?>? mxcUris)
    {
        if (mxcUris == null) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return mxcUris.Where(m => !string.IsNullOrEmpty(m) && seen.Add(m)).Select(m => m!).ToList();
    }

    public Task<OperationResult<List<RoomMediaItemViewModel>>> GetMediaMetadataBatchAsync(List<string> mxcUris, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingMedia",
            ex => logger.LogError(ex, "Error fetching media metadata batch"),
            async gateway =>
            {
                using var semaphore = new SemaphoreSlim(10, 10);
                var tasks = UniqueMxcs(mxcUris).Select(async m =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        var vm = new RoomMediaItemViewModel { MediaId = m };
                        try
                        {
                            var meta = await gateway.GetMediaMetadataAsync(m, token);
                            if (meta != null)
                            {
                                vm.ApplyMetadata(meta);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Silent cancellation for inner task
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Failed to fetch metadata for media {Mxc}", m.SanitizeForLogging());
                        }
                        return vm;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var results = (await Task.WhenAll(tasks)).ToList();
                return OperationResult<List<RoomMediaItemViewModel>>.Ok(results);
            });

    public Task<OperationResult> DeleteRoomAsync(string roomId, bool block = false, bool purge = true, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingRoom",
            ex => logger.LogError(ex, "Error deleting room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var req = new SynapseAdminRoomDeleteRequest
                {
                    Block = block,
                    Purge = purge
                };
                await gateway.DeleteRoomAsync(roomId, req, token);
                memoryCache.Remove(GetRoomNameCacheKey(roomId));
                ClearActivePurgeId(roomId);
                logger.LogInformation("Successfully deleted room {RoomId} (block: {Block}, purge: {Purge})", roomId.SanitizeForLogging(), block, purge);
                return OperationResult.Ok(L["RoomDeletedSuccessfully"]);
            });

    public Task<OperationResult> QuarantineMediaAsync(string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorQuarantiningRoomMedia",
            ex => logger.LogError(ex, "Error quarantining media for room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.QuarantineMediaByRoomIdAsync(roomId, token);
                logger.LogInformation("Successfully quarantined media for room {RoomId}", roomId.SanitizeForLogging());
                return OperationResult.Ok(L["RoomMediaQuarantinedSuccessfully"]);
            });

    public Task<OperationResult> BlockRoomAsync(string roomId, bool block, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, block ? "ErrorBlockingRoom" : "ErrorUnblockingRoom",
            ex => logger.LogError(ex, "Error blocking/unblocking room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.BlockRoomAsync(roomId, block, token);
                logger.LogInformation("Successfully {Action} room {RoomId}", block ? "blocked" : "unblocked", roomId.SanitizeForLogging());
                return OperationResult.Ok(block ? L["RoomBlockedSuccessfully"] : L["RoomUnblockedSuccessfully"]);
            });

    public Task<OperationResult<List<RoomStatisticsViewModel>>> GetLargestRoomsAsync(CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingLargestRooms",
            ex => logger.LogError(ex, "Error fetching largest rooms"),
            async gateway =>
            {
                var stats = await gateway.GetLargestRoomsAsync(token);
                if (stats == null) return OperationResult<List<RoomStatisticsViewModel>>.Ok([]);

                var tasks = stats.Rooms.Take(10).Select(async roomStat =>
                {
                    var vm = new RoomStatisticsViewModel
                    {
                        RoomId = roomStat.RoomId,
                        EstimatedSize = roomStat.EstimatedSize,
                        Name = roomStat.RoomId
                    };

                    if (memoryCache.TryGetValue<string>(GetRoomNameCacheKey(roomStat.RoomId), out var cachedName) &&
                        !string.IsNullOrEmpty(cachedName))
                    {
                        vm.Name = cachedName;
                        return vm;
                    }

                    try
                    {
                        var roomDetails = await gateway.GetRoomDetailsAsync(roomStat.RoomId, token);
                        if (roomDetails != null)
                        {
                            if (!string.IsNullOrEmpty(roomDetails.Name))
                            {
                                vm.Name = roomDetails.Name;
                            }
                            CacheRoomName(roomStat.RoomId, roomDetails.Name);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Silent cancellation for inner task
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to fetch metadata for room {RoomId} during statistics gathering.", roomStat.RoomId.SanitizeForLogging());
                    }
                    return vm;
                });

                var result = (await Task.WhenAll(tasks)).ToList();
                return OperationResult<List<RoomStatisticsViewModel>>.Ok(result);
            });

    public Task<OperationResult<RoomMessagesViewModel>> GetRoomMessagesAsync(string roomId, string? from = null, int limit = 10, string dir = "f", string? filter = null, string? to = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomMessages",
            ex => logger.LogError(ex, "Error fetching room messages for {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetRoomMessagesAsync(roomId, limit, from, dir, filter, to, token);
                if (result == null) return OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel());

                var vm = new RoomMessagesViewModel
                {
                    EndToken = result.End,
                    Messages = result.Chunk
                        .Where(m => m.Type == "m.room.message")
                        .Select(m =>
                        {
                            string? content = null;
                            string? body = null;
                            if (m.Content is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } contentJson)
                            {
                                if (contentJson.ValueKind == JsonValueKind.Object && contentJson.TryGetProperty("body", out var bodyProp))
                                {
                                    body = bodyProp.GetString();
                                }
                                content = contentJson.GetRawText();
                            }

                            return new RoomMessageItemViewModel
                            {
                                EventId = m.EventId,
                                Sender = m.Sender,
                                OriginServerTs = DateTimeOffset.FromUnixTimeMilliseconds(m.OriginServerTs),
                                Type = m.Type,
                                StateKey = m.StateKey,
                                Content = content,
                                Body = body
                            };
                        }).ToList()
                };

                return OperationResult<RoomMessagesViewModel>.Ok(vm);
            });

    private string GetActivePurgeCacheKey(string roomId) =>
        $"active_purge:{HomeserverCacheScope}:{roomId}";

    public string? GetActivePurgeId(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return null;
        return memoryCache.TryGetValue<string>(GetActivePurgeCacheKey(roomId), out var purgeId) ? purgeId : null;
    }

    public void SetActivePurgeId(string roomId, string purgeId)
    {
        if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(purgeId)) return;
        memoryCache.Set(GetActivePurgeCacheKey(roomId), purgeId, ActivePurgeCacheOptions);
    }

    public void ClearActivePurgeId(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        memoryCache.Remove(GetActivePurgeCacheKey(roomId));
    }

    public void ClearActivePurgeId(string roomId, string expectedPurgeId)
    {
        if (GetActivePurgeId(roomId) == expectedPurgeId) ClearActivePurgeId(roomId);
    }

    public Task<OperationResult<string>> PurgeRoomHistoryAsync(string roomId, PurgeHistoryViewModel request, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorPurgingHistory",
            ex => logger.LogError(ex, "Error purging history for room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var gatewayReq = new SynapseAdminPurgeHistoryRequest
                {
                    DeleteLocalEvents = request.DeleteLocalEvents,
                    PurgeUpToEventId = request.PurgeUpToEventId,
                    PurgeUpToTs = request.PurgeUpToTs
                };
                var result = await gateway.PurgeRoomHistoryAsync(roomId, gatewayReq, token);
                if (result == null) return OperationResult<string>.Failure(L["ErrorPurgingHistory"]);
                if (string.IsNullOrWhiteSpace(result.PurgeId))
                {
                    logger.LogWarning("Purge history request for room {RoomId} succeeded without returning a purge_id", roomId.SanitizeForLogging());
                    return OperationResult<string>.Ok(string.Empty, L["PurgeStarted"]);
                }

                SetActivePurgeId(roomId, result.PurgeId);
                return OperationResult<string>.Ok(result.PurgeId, L["PurgeStarted"]);
            });

    public Task<OperationResult<SynapseAdminPurgeHistoryStatusResponse>> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingPurgeStatus",
            ex => logger.LogError(ex, "Error getting purge status for purge {PurgeId}", purgeId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetPurgeHistoryStatusAsync(purgeId, token);
                if (result == null) return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Failure(L["ErrorFetchingPurgeStatus"]);
                return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Ok(result);
            });

    public Task<OperationResult<PublicRoomDirectoryViewModel>> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomDirectory",
            ex => logger.LogError(ex, "Error fetching public room directory"),
            async gateway =>
            {
                var result = await gateway.GetPublicRoomsAsync(limit, since, searchTerm, token);
                // Collection read: a null result is an empty directory, not an error (#395, H-12).
                if (result == null) return OperationResult<PublicRoomDirectoryViewModel>.Ok(new PublicRoomDirectoryViewModel());
                var vm = result.ToViewModel();
                return OperationResult<PublicRoomDirectoryViewModel>.Ok(vm);
            });

    public Task<OperationResult<string>> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomDirectoryVisibility",
            ex => logger.LogError(ex, "Error fetching directory visibility for room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetRoomDirectoryVisibilityAsync(roomId, token);
                if (result == null) return OperationResult<string>.Failure(L["ErrorFetchingRoomDirectoryVisibility"]);
                return OperationResult<string>.Ok(result);
            });

    public Task<OperationResult> SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorSettingRoomDirectoryVisibility",
            ex => logger.LogError(ex, "Error setting directory visibility for room {RoomId} to {Visibility}", roomId.SanitizeForLogging(), visibility.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.SetRoomDirectoryVisibilityAsync(roomId, visibility, token);
                var msg = string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase)
                    ? L["RoomRemovedFromDirectory"]
                    : L["RoomDirectoryVisibilityUpdated"];
                return OperationResult.Ok(msg);
            });

    public Task<OperationResult> PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorSettingRoomAlias",
            ex => logger.LogError(ex, "Error setting alias {Alias} for room {RoomId}", roomAlias.SanitizeForLogging(), roomId.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.PutRoomAliasAsync(roomAlias, roomId, token);
                return OperationResult.Ok(L["AliasAddedSuccessfully"]);
            });

    public Task<OperationResult> DeleteRoomAliasAsync(string roomAlias, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingRoomAlias",
            ex => logger.LogError(ex, "Error deleting alias {Alias}", roomAlias.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.DeleteRoomAliasAsync(roomAlias, token);
                return OperationResult.Ok(L["AliasDeletedSuccessfully"]);
            });

    public Task<OperationResult<List<string>>> GetLocalRoomAliasesAsync(string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomAliases",
            ex => logger.LogError(ex, "Error fetching aliases for room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetLocalRoomAliasesAsync(roomId, token);
                // Collection read: a null result means no aliases, not an error (#395, H-12).
                if (result == null) return OperationResult<List<string>>.Ok([]);
                return OperationResult<List<string>>.Ok(result);
            });

    private static string? GetSafeString(JsonNode? node)
    {
        if (node is null) return null;
        return node is JsonValue val && val.TryGetValue<string>(out var str) ? str : node.ToString();
    }
}
