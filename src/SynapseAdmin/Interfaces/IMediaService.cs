using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models;

namespace SynapseAdmin.Interfaces;

public interface IMediaService
{
    Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc, CancellationToken token = default);
    Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc, CancellationToken token = default);
    Task<OperationResult> QuarantineMediaAsync(string mxc, CancellationToken token = default);
    Task<OperationResult> UnquarantineMediaAsync(string mxc, CancellationToken token = default);
    Task<OperationResult> DeleteMediaAsync(string mxc, CancellationToken token = default);
    Task<OperationResult> ProtectMediaAsync(string mxc, CancellationToken token = default);
    Task<OperationResult> UnprotectMediaAsync(string mxc, CancellationToken token = default);
    Task<OperationResult<SynapseAdminPurgeMediaCacheResponse>> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken token = default);
    Task<OperationResult<SynapseAdminDeleteMediaResponse>> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken token = default);
}
