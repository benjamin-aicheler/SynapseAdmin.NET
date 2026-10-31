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

    private IMatrixGateway? Gateway => sessionService.Gateway;

    private string GetRoomNameCacheKey(string roomId) =>
        $"room_name:{Gateway?.HomeserverUrl?.TrimEnd('/').ToLowerInvariant()}:{roomId}";

    private void CacheRoomName(string roomId, string? name)
    {
        if (Gateway == null || string.IsNullOrEmpty(roomId)) return;
        var resolvedName = string.IsNullOrEmpty(name) ? roomId : name;
        memoryCache.Set(GetRoomNameCacheKey(roomId), resolvedName, RoomNameCacheOptions);
    }

    public async Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Failure(L["NotAuthenticated"]);

        try
        {
            if (orderBy == "room_id") orderBy = "alphabetical";
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var result = await Gateway.GetRoomListAsync(offset, limit, orderBy, dir, searchTerm, token);
            if (result == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((0, []));

            foreach (var room in result.Rooms)
            {
                CacheRoomName(room.RoomId, room.Name);
            }

            var vms = result.Rooms.ToViewModels();

            return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((result.TotalRooms, vms));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching room list (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})", offset, limit, searchTerm.SanitizeForLogging());
            return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Failure(L["ErrorFetchingRoomList"]);
        }
    }

    public async Task<OperationResult<RoomDetailViewModel>> GetRoomDetailsAsync(string roomId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<RoomDetailViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var r = await Gateway.GetRoomDetailsAsync(roomId, token);
            
            if (r == null) return OperationResult<RoomDetailViewModel>.Failure(L["RoomNotFound"]);

            CacheRoomName(roomId, r.Name);

            var membersTask = Gateway.GetRoomMembersAsync(roomId, token);
            var stateTask = Gateway.GetRoomStateAsync(roomId, cancellationToken: token);
            var mediaTask = Gateway.GetRoomMediaListAsync(roomId, token);

            await Task.WhenAll(membersTask, stateTask, mediaTask);

            var members = await membersTask;
            var stateEvents = await stateTask;
            var media = await mediaTask;

            var vm = r.ToDetailViewModel();
            
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
            var memberStateEvents = stateEvents?.Events
                .Where(e => e.Type == "m.room.member" && !string.IsNullOrEmpty(e.StateKey))
                .GroupBy(e => e.StateKey!)
                .ToDictionary(g => g.Key, g => g.First());

            vm.Members = members?.Members.Select(userId =>
            {
                var memberVm = new RoomMemberViewModel { UserId = userId };
                if (memberStateEvents != null && memberStateEvents.TryGetValue(userId, out var memberEvent))
                {
                    if (memberEvent.RawContent != null)
                    {
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
                    }
                }
                return memberVm;
            }).ToList() ?? [];
            vm.StateEvents = stateEvents?.Events.Select(e => new RoomStateEventViewModel
            {
                Type = e.Type,
                StateKey = e.StateKey,
                Sender = e.Sender,
                RawContent = e.RawContent?.ToJsonString()
            }).ToList() ?? [];
            vm.Media = media == null ? null : new RoomMediaViewModel
            {
                Local = media.Local.Select(m => new RoomMediaItemViewModel { MediaId = m, IsLocal = true }).ToList(),
                Remote = media.Remote.Select(m => new RoomMediaItemViewModel { MediaId = m, IsLocal = false }).ToList()
            };
            
            return OperationResult<RoomDetailViewModel>.Ok(vm);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<RoomDetailViewModel>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching room details for {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<RoomDetailViewModel>.Failure(L["ErrorFetchingRoomDetails"]);
        }
    }

    public async Task<OperationResult<List<RoomMediaItemViewModel>>> GetMediaMetadataBatchAsync(List<string> mxcUris, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<List<RoomMediaItemViewModel>>.Failure(L["NotAuthenticated"]);

        try
        {
            using var semaphore = new SemaphoreSlim(10, 10);
            var tasks = mxcUris.Select(async m =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    var vm = new RoomMediaItemViewModel { MediaId = m };
                    try
                    {
                        var meta = await Gateway.GetMediaMetadataAsync(m, token);
                        if (meta != null)
                        {
                            vm.UploadName = meta.UploadName;
                            vm.MediaType = meta.MediaType;
                            vm.MediaLength = meta.MediaLength;
                            vm.CreatedTimestamp = meta.CreatedTimestamp;
                            vm.QuarantinedBy = meta.QuarantinedBy;
                            vm.SafeFromQuarantine = meta.SafeFromQuarantine ?? false;
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
        }
        catch (OperationCanceledException)
        {
            return OperationResult<List<RoomMediaItemViewModel>>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching media metadata batch");
            return OperationResult<List<RoomMediaItemViewModel>>.Failure(L["ErrorFetchingMedia"]);
        }
    }

    public async Task<OperationResult> DeleteRoomAsync(string roomId, bool block = false, bool purge = true, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminRoomDeleteRequest
            {
                Block = block,
                Purge = purge
            };
            await Gateway.DeleteRoomAsync(roomId, req, token);
            memoryCache.Remove(GetRoomNameCacheKey(roomId));
            ClearActivePurgeId(roomId);
            logger.LogInformation("Successfully deleted room {RoomId} (block: {Block}, purge: {Purge})", roomId.SanitizeForLogging(), block, purge);
            return OperationResult.Ok(L["RoomDeletedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeletingRoom"]);
        }
    }

    public async Task<OperationResult> QuarantineMediaAsync(string roomId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.QuarantineMediaByRoomIdAsync(roomId, token);
            logger.LogInformation("Successfully quarantined media for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Ok(L["RoomMediaQuarantinedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorQuarantiningRoomMedia"]);
        }
    }

    public async Task<OperationResult> BlockRoomAsync(string roomId, bool block, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.BlockRoomAsync(roomId, block, token);
            logger.LogInformation("Successfully {Action} room {RoomId}", block ? "blocked" : "unblocked", roomId.SanitizeForLogging());
            return OperationResult.Ok(block ? L["RoomBlockedSuccessfully"] : L["RoomUnblockedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error blocking/unblocking room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Failure(block ? L["ErrorBlockingRoom"] : L["ErrorUnblockingRoom"]);
        }
    }

    public async Task<OperationResult<List<RoomStatisticsViewModel>>> GetLargestRoomsAsync(CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<List<RoomStatisticsViewModel>>.Failure(L["NotAuthenticated"]);
        try
        {
            var stats = await Gateway.GetLargestRoomsAsync(token);
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
                    var roomDetails = await Gateway.GetRoomDetailsAsync(roomStat.RoomId, token);
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
        }
        catch (OperationCanceledException)
        {
            return OperationResult<List<RoomStatisticsViewModel>>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching largest rooms");
            return OperationResult<List<RoomStatisticsViewModel>>.Failure(L["ErrorFetchingLargestRooms"]);
        }
    }

    public async Task<OperationResult<RoomMessagesViewModel>> GetRoomMessagesAsync(string roomId, string? from = null, int limit = 10, string dir = "f", string? filter = null, string? to = null, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<RoomMessagesViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetRoomMessagesAsync(roomId, limit, from, dir, filter, to, token);
            if (result == null) return OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel());

            var vm = new RoomMessagesViewModel
            {
                StartToken = result.Start,
                EndToken = result.End,
                Messages = result.Chunk
                    .Where(m => m.Type == "m.room.message")
                    .Select(m =>
                    {
                        if (m.Content is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } contentJson)
                        {
                            return new RoomMessageItemViewModel
                            {
                                EventId = m.EventId,
                                Sender = m.Sender,
                                OriginServerTs = DateTimeOffset.FromUnixTimeMilliseconds(m.OriginServerTs),
                                Type = m.Type,
                                StateKey = m.StateKey
                            };
                        }

                        string? body = null;
                        if (contentJson.ValueKind == JsonValueKind.Object && contentJson.TryGetProperty("body", out var bodyProp))
                        {
                            body = bodyProp.GetString();
                        }

                        return new RoomMessageItemViewModel
                        {
                            EventId = m.EventId,
                            Sender = m.Sender,
                            OriginServerTs = DateTimeOffset.FromUnixTimeMilliseconds(m.OriginServerTs),
                            Type = m.Type,
                            StateKey = m.StateKey,
                            Content = contentJson.GetRawText(),
                            Body = body
                        };
                    }).ToList()
            };

            return OperationResult<RoomMessagesViewModel>.Ok(vm);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<RoomMessagesViewModel>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching room messages for {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<RoomMessagesViewModel>.Failure(L["ErrorFetchingRoomMessages"]);
        }
    }

    private static readonly MemoryCacheEntryOptions ActivePurgeCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    private string GetActivePurgeCacheKey(string roomId) =>
        $"active_purge:{Gateway?.HomeserverUrl?.TrimEnd('/').ToLowerInvariant()}:{roomId}";

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

    public async Task<OperationResult<string>> PurgeRoomHistoryAsync(string roomId, PurgeHistoryViewModel request, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<string>.Failure(L["NotAuthenticated"]);

        try
        {
            var gatewayReq = new SynapseAdminPurgeHistoryRequest
            {
                DeleteLocalEvents = request.DeleteLocalEvents,
                PurgeUpToEventId = request.PurgeUpToEventId,
                PurgeUpToTs = request.PurgeUpToTs
            };
            var result = await Gateway.PurgeRoomHistoryAsync(roomId, gatewayReq, token);
            if (result == null) return OperationResult<string>.Failure(L["ErrorPurgingHistory"]);
            SetActivePurgeId(roomId, result.PurgeId);
            return OperationResult<string>.Ok(result.PurgeId, L["PurgeStarted"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error purging history for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<string>.Failure(L["ErrorPurgingHistory"]);
        }
    }

    public async Task<OperationResult<SynapseAdminPurgeHistoryStatusResponse>> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetPurgeHistoryStatusAsync(purgeId, token);
            if (result == null) return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Failure(L["ErrorFetchingPurgeStatus"]);
            return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting purge status for purge {PurgeId}", purgeId.SanitizeForLogging());
            return OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Failure(L["ErrorFetchingPurgeStatus"]);
        }
    }

    public async Task<OperationResult<PublicRoomDirectoryViewModel>> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<PublicRoomDirectoryViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetPublicRoomsAsync(limit, since, searchTerm, token);
            if (result == null) return OperationResult<PublicRoomDirectoryViewModel>.Failure(L["ErrorFetchingRoomDirectory"]);
            var vm = new PublicRoomDirectoryViewModel
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
            return OperationResult<PublicRoomDirectoryViewModel>.Ok(vm);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<PublicRoomDirectoryViewModel>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching public room directory");
            return OperationResult<PublicRoomDirectoryViewModel>.Failure(L["ErrorFetchingRoomDirectory"]);
        }
    }

    public async Task<OperationResult<string>> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<string>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetRoomDirectoryVisibilityAsync(roomId, token);
            if (result == null) return OperationResult<string>.Failure(L["ErrorFetchingRoomDirectoryVisibility"]);
            return OperationResult<string>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching directory visibility for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<string>.Failure(L["ErrorFetchingRoomDirectoryVisibility"]);
        }
    }

    public async Task<OperationResult> SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await Gateway.SetRoomDirectoryVisibilityAsync(roomId, visibility, token);
            var msg = string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase)
                ? L["RoomRemovedFromDirectory"]
                : L["RoomDirectoryVisibilityUpdated"];
            return OperationResult.Ok(msg);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting directory visibility for room {RoomId} to {Visibility}", roomId.SanitizeForLogging(), visibility.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorSettingRoomDirectoryVisibility"]);
        }
    }

    public async Task<OperationResult> PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await Gateway.PutRoomAliasAsync(roomAlias, roomId, token);
            return OperationResult.Ok(L["AliasAddedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting alias {Alias} for room {RoomId}", roomAlias.SanitizeForLogging(), roomId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorSettingRoomAlias"]);
        }
    }

    public async Task<OperationResult> DeleteRoomAliasAsync(string roomAlias, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await Gateway.DeleteRoomAliasAsync(roomAlias, token);
            return OperationResult.Ok(L["AliasDeletedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting alias {Alias}", roomAlias.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeletingRoomAlias"]);
        }
    }

    public async Task<OperationResult<List<string>>> GetLocalRoomAliasesAsync(string roomId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<List<string>>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetLocalRoomAliasesAsync(roomId, token);
            if (result == null) return OperationResult<List<string>>.Failure(L["ErrorFetchingRoomAliases"]);
            return OperationResult<List<string>>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<List<string>>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching aliases for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<List<string>>.Failure(L["ErrorFetchingRoomAliases"]);
        }
    }

    private static string? GetSafeString(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is null) return null;
        return node is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var str) ? str : node.ToString();
    }
}
