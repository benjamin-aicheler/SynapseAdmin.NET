using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using System.Text.Json;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Services;

public class RoomService(IMatrixSessionService sessionService, ILogger<RoomService> logger, IStringLocalizer<SharedResources> L) : IRoomService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Failure(L["NotAuthenticated"]);

        try
        {
            if (orderBy == "room_id") orderBy = "alphabetical";
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var result = await Gateway.GetRoomListAsync(offset, limit, orderBy, dir, searchTerm, token);
            if (result == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((0, []));
            
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

            var membersTask = Gateway.GetRoomMembersAsync(roomId, token);
            var stateTask = Gateway.GetRoomStateAsync(roomId, cancellationToken: token);
            var mediaTask = Gateway.GetRoomMediaListAsync(roomId, token);

            await Task.WhenAll(membersTask, stateTask, mediaTask);

            var members = await membersTask;
            var stateEvents = await stateTask;
            var media = await mediaTask;

            var vm = r.ToDetailViewModel();
            
            if (r.Tombstoned == null)
            {
                var tombstone = stateEvents?.Events
                    .FirstOrDefault(x => x.Type == "m.room.tombstone");
                if (tombstone != null)
                {
                    vm.IsTombstoned = true;
                    if (tombstone.RawContent != null && tombstone.RawContent.TryGetPropertyValue("replacement_room", out var replacementNode))
                    {
                        vm.ReplacementRoom = replacementNode?.GetValue<string>();
                    }
                }
            }
            vm.Members = members?.Members ?? [];
            vm.StateEvents = stateEvents?.Events.Select(e => new RoomStateEventViewModel
            {
                Type = e.Type,
                StateKey = e.StateKey,
                Sender = e.Sender,
                RawContent = e.RawContent?.ToJsonString()
            }).ToList() ?? [];
            vm.Media = media == null ? null : new RoomMediaViewModel
            {
                Local = media.Local.Select(m => new RoomMediaItemViewModel { MediaId = m }).ToList(),
                Remote = media.Remote.Select(m => new RoomMediaItemViewModel { MediaId = m }).ToList()
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
            var tasks = mxcUris.Select(async m =>
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
                    logger.LogDebug(ex, "Failed to fetch metadata for media {Mxc}", m);
                }
                return vm;
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

                try
                {
                    var roomDetails = await Gateway.GetRoomDetailsAsync(roomStat.RoomId, token);
                    if (roomDetails != null && !string.IsNullOrEmpty(roomDetails.Name))
                    {
                        vm.Name = roomDetails.Name;
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
                        if (m.Content == null) return new RoomMessageItemViewModel {
                            EventId = m.EventId,
                            Sender = m.Sender,
                            OriginServerTs = DateTimeOffset.FromUnixTimeMilliseconds(m.OriginServerTs),
                            Type = m.Type,
                            StateKey = m.StateKey
                        };

                        var contentJson = JsonSerializer.SerializeToElement(m.Content);
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
                            Content = JsonSerializer.Serialize(m.Content),
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

    private readonly Dictionary<string, string> _activePurges = new();

    public string? GetActivePurgeId(string roomId)
    {
        lock (_activePurges)
        {
            return _activePurges.TryGetValue(roomId, out var purgeId) ? purgeId : null;
        }
    }

    public void SetActivePurgeId(string roomId, string purgeId)
    {
        lock (_activePurges)
        {
            _activePurges[roomId] = purgeId;
        }
    }

    public void ClearActivePurgeId(string roomId)
    {
        lock (_activePurges)
        {
            _activePurges.Remove(roomId);
        }
    }

    public async Task<OperationResult<SynapseAdminPurgeHistoryResponse>> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<SynapseAdminPurgeHistoryResponse>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.PurgeRoomHistoryAsync(roomId, request, token);
            if (result == null) return OperationResult<SynapseAdminPurgeHistoryResponse>.Failure(L["ErrorPurgingHistory"]);
            return OperationResult<SynapseAdminPurgeHistoryResponse>.Ok(result, L["PurgeStarted"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminPurgeHistoryResponse>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error purging history for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<SynapseAdminPurgeHistoryResponse>.Failure(L["ErrorPurgingHistory"]);
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
}
