using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models;

namespace SynapseAdmin.Interfaces;

public interface IMediaService
{
    Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc);
    Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc);
}
