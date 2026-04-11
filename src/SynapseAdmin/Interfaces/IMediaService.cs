using SynapseAdmin.Models;

namespace SynapseAdmin.Interfaces;

public interface IMediaService
{
    Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc);
}
