using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Infrastructure.Helpers;
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

    // Guards writes of the tracked purge IDs. Static, because every session's (scoped) RoomService shares the
    // process-wide IMemoryCache, and IMemoryCache has no compare-and-remove of its own (#522).
    private static readonly Lock ActivePurgeLock = new();

    private static readonly MemoryCacheEntryOptions ActivePurgeCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    /// <summary>Names the target in a log line when Make room admin is sent without a user (#587).</summary>
    private const string SignedInAdminLogLabel = "the signed-in admin";

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
                var dir = direction.ToSynapseDir(whenUnsorted: "f");
                // Synapse sorts these keys largest first, so smallest first (ascending) is dir=b. Unsorted keeps
                // Synapse's own order. The gateway corrects servers that sort them differently (#541).
                if (direction != SortDirection.None && RoomSortKeys.IsLargestFirst(orderBy)) dir = RoomSortKeys.Reverse(dir);
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
                // A null media list is a collection read: no media (#395). A null Media would make RoomGalleryGrid
                // load the room again and keep RoomDetails' media tab loading forever (#524).
                vm.Media = new RoomMediaViewModel
                {
                    Local = UniqueMxcs(media?.Local).Select(m => new RoomMediaItemViewModel { MediaId = m, IsLocal = true }).ToList(),
                    Remote = UniqueMxcs(media?.Remote).Select(m => new RoomMediaItemViewModel { MediaId = m, IsLocal = false }).ToList()
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

    public Task<OperationResult<string>> DeleteRoomAsync(string roomId, bool block = false, bool purge = true, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingRoom",
            ex => logger.LogError(ex, "Error deleting room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var req = new SynapseAdminRoomDeleteRequest
                {
                    Block = block,
                    Purge = purge
                };
                // The homeserver only starts the deletion; the room page follows it by its delete_id (#518).
                var deleteId = await RequestRoomDeletionAsync(gateway, roomId, req, token);
                memoryCache.Remove(GetRoomNameCacheKey(roomId));
                ClearActivePurgeId(roomId);
                return OperationResult<string>.Ok(deleteId ?? string.Empty, L["RoomDeletionRequested"]);
            });

    /// <summary>
    /// Sends the deletion and returns its <c>delete_id</c>, or <c>null</c> (logged) if the homeserver accepted it without
    /// one or with an answer that isn't JSON. Both are still a requested deletion, not an error.
    /// </summary>
    private async Task<string?> RequestRoomDeletionAsync(IMatrixGateway gateway, string roomId, SynapseAdminRoomDeleteRequest req, CancellationToken token)
    {
        try
        {
            var response = await gateway.DeleteRoomAsync(roomId, req, token);
            if (!string.IsNullOrWhiteSpace(response?.DeleteId))
            {
                logger.LogInformation("Requested deletion of room {RoomId} (delete ID {DeleteId}, block: {Block}, purge: {Purge})",
                    roomId.SanitizeForLogging(), response.DeleteId.SanitizeForLogging(), req.Block, req.Purge);
                return response.DeleteId;
            }

            logger.LogWarning("Deletion of room {RoomId} was requested, but the homeserver returned no delete_id (block: {Block}, purge: {Purge})",
                roomId.SanitizeForLogging(), req.Block, req.Purge);
        }
        catch (JsonException ex)
        {
            // A 2xx answer: the homeserver accepted the deletion, only its body can't be read.
            logger.LogWarning(ex, "Deletion of room {RoomId} was requested, but the homeserver's response is not JSON (block: {Block}, purge: {Purge})",
                roomId.SanitizeForLogging(), req.Block, req.Purge);
        }

        return null;
    }

    public Task<OperationResult<RoomDeletionStatusViewModel>> GetRoomDeletionStatusAsync(string deleteId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomDeletionStatus",
            ex => logger.LogError(ex, "Error getting room deletion status for delete ID {DeleteId}", deleteId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetRoomDeleteStatusAsync(deleteId, token);
                // Unlike other single-resource reads, null is an answer: the homeserver no longer knows the deletion
                // (its status is kept 24 h, or until it restarts), or has no status endpoint (#518).
                if (result == null)
                {
                    return OperationResult<RoomDeletionStatusViewModel>.Ok(new RoomDeletionStatusViewModel { DeleteId = deleteId, State = RoomDeletionState.Unknown });
                }

                var status = result.ToViewModel();
                if (status.State == RoomDeletionState.Failed)
                {
                    // The homeserver's error text goes only to the log; the UI shows a localized message.
                    logger.LogWarning("Room deletion {DeleteId} of room {RoomId} failed: {Error}",
                        deleteId.SanitizeForLogging(), result.RoomId?.SanitizeForLogging(), result.Error?.SanitizeForLogging());
                }
                return OperationResult<RoomDeletionStatusViewModel>.Ok(status);
            });

    public Task<OperationResult<RoomDeletionStatusViewModel?>> GetActiveRoomDeletionAsync(string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomDeletionStatus",
            ex => logger.LogError(ex, "Error looking up deletions of room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetRoomDeleteStatusesAsync(roomId, token);
                // Collection read: null means no deletion the homeserver knows of (#395, #518).
                var running = (result?.Results ?? [])
                    .Select(r => r.ToViewModel())
                    .Where(s => s.IsRunning && !string.IsNullOrEmpty(s.DeleteId))
                    .ToList();
                var active = running.FirstOrDefault(s => s.State == RoomDeletionState.Active) ?? running.FirstOrDefault();
                return OperationResult<RoomDeletionStatusViewModel?>.Ok(active);
            },
            // A page loads this by itself: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support looking up deletions of room {RoomId}", roomId.SanitizeForLogging()));

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

    public Task<OperationResult<RoomBlockStatusViewModel>> GetRoomBlockStatusAsync(string roomId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingRoomBlockStatus",
            ex => logger.LogError(ex, "Error fetching block status for room {RoomId}", roomId.SanitizeForLogging()),
            async gateway =>
            {
                var status = await gateway.GetRoomBlockStatusAsync(roomId, token);
                // Single-resource read: Synapse always answers with the state, so null is a failure (#395, #586).
                return status == null
                    ? OperationResult<RoomBlockStatusViewModel>.Failure(L["ErrorFetchingRoomBlockStatus"])
                    : OperationResult<RoomBlockStatusViewModel>.Ok(status.ToViewModel());
            },
            // A page loads this by itself: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support reading the block status of room {RoomId}", roomId.SanitizeForLogging()));

    public Task<OperationResult> MakeRoomAdminAsync(string roomId, string? userId, CancellationToken token = default)
    {
        // Blank means the signed-in admin, Synapse's default when no user_id is sent.
        var target = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        return sessionService.RunGatewayCallAsync(L, "ErrorMakingRoomAdmin",
            ex => logger.LogError(ex, "Error making {UserId} room admin in room {RoomId}",
                (target ?? SignedInAdminLogLabel).SanitizeForLogging(), roomId.SanitizeForLogging()),
            async gateway =>
            {
                if (target != null && !MatrixIdHelper.IsUserId(target))
                {
                    return OperationResult.Failure(L["InvalidUserId"], Severity.Warning);
                }

                await gateway.MakeRoomAdminAsync(roomId, target, token);
                var grantee = target ?? gateway.UserId;
                logger.LogInformation("Made {UserId} room admin in room {RoomId}", grantee.SanitizeForLogging(), roomId.SanitizeForLogging());
                return OperationResult.Ok(L["RoomAdminGranted", grantee, roomId]);
            });
    }

    public Task<OperationResult> JoinUserToRoomAsync(string roomIdOrAlias, string userId,
        IReadOnlyList<string>? serverNames = null, CancellationToken token = default)
    {
        var target = userId.Trim();
        var room = roomIdOrAlias.Trim();
        var servers = DistinctServerNames(serverNames);
        return sessionService.RunGatewayCallAsync(L, "ErrorJoiningUserToRoom",
            ex => logger.LogError(ex, "Error joining {UserId} to room {RoomId} through {ServerCount} given servers",
                target.SanitizeForLogging(), room.SanitizeForLogging(), servers.Count),
            async gateway =>
            {
                // The user page lets the admin type the room (#605, #606): a room ID or an alias.
                var isAlias = MatrixIdHelper.IsRoomAlias(room);
                if (!isAlias && !MatrixIdHelper.IsRoomId(room))
                {
                    return OperationResult.Failure(L["InvalidRoomId"], Severity.Warning);
                }

                // Synapse and Tuwunel join an alias through the servers its resolution returns, never through given ones.
                if (isAlias && servers.Count > 0)
                {
                    return OperationResult.Failure(L["JoinServersNeedRoomId"], Severity.Warning);
                }

                var invalidServer = servers.FirstOrDefault(server => !MatrixIdHelper.IsServerName(server));
                if (invalidServer != null)
                {
                    return OperationResult.Failure(L["InvalidServerName", invalidServer], Severity.Warning);
                }

                if (!MatrixIdHelper.IsUserId(target))
                {
                    return OperationResult.Failure(L["InvalidUserId"], Severity.Warning);
                }

                // Exact, as the homeserver compares it; MatrixIdHelper.IsOnServer ignores case.
                if (!MatrixIdHelper.IsLocalUserId(target, gateway.ServerName))
                {
                    return OperationResult.Failure(L["JoinUserNotLocal", gateway.ServerName], Severity.Warning);
                }

                var response = await gateway.JoinUserToRoomAsync(room, target, servers, token);
                // An operation: Synapse always answers with the room ID, so null is a failure (#395).
                if (response == null) return OperationResult.Failure(L["ErrorJoiningUserToRoom"]);

                // A join by alias names the room it reached (#606).
                var joined = string.IsNullOrEmpty(response.RoomId) ? room : response.RoomId;
                logger.LogInformation("Joined {UserId} to room {RoomId} through {ServerCount} given servers",
                    target.SanitizeForLogging(), joined.SanitizeForLogging(), servers.Count);
                return OperationResult.Ok(L["UserJoinedRoom", target, joined]);
            });
    }

    /// <summary>
    /// The servers to join through, trimmed, without blanks and duplicates, in the given order (#606). The names aren't
    /// checked here.
    /// </summary>
    private static List<string> DistinctServerNames(IReadOnlyList<string>? serverNames) =>
        serverNames == null
            ? []
            : serverNames.Select(server => server.Trim()).Where(server => server.Length > 0).Distinct(StringComparer.Ordinal).ToList();

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
            },
            // A page loads this by itself: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support room statistics by database size"));

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
        lock (ActivePurgeLock)
        {
            memoryCache.Set(GetActivePurgeCacheKey(roomId), purgeId, ActivePurgeCacheOptions);
        }
    }

    public void ClearActivePurgeId(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        lock (ActivePurgeLock)
        {
            memoryCache.Remove(GetActivePurgeCacheKey(roomId));
        }
    }

    public void ClearActivePurgeId(string roomId, string expectedPurgeId)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        // Compare and remove in one step: another session must not set a newer purge in between (#522).
        lock (ActivePurgeLock)
        {
            if (GetActivePurgeId(roomId) == expectedPurgeId) memoryCache.Remove(GetActivePurgeCacheKey(roomId));
        }
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
                if (result.Status == "failed")
                {
                    // The homeserver's error text goes only to the log; the room page shows a localized message (#518).
                    logger.LogWarning("History purge {PurgeId} failed: {Error}", purgeId.SanitizeForLogging(), result.Error?.SanitizeForLogging());
                }
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
