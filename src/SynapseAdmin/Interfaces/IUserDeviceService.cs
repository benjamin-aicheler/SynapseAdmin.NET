using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

/// <summary>A local user's devices and recent connections (#598).</summary>
public interface IUserDeviceService
{
    Task<OperationResult<List<UserDeviceViewModel>>> GetUserDevicesAsync(string userId, CancellationToken token = default);

    /// <summary>The user's recent connections, newest first.</summary>
    Task<OperationResult<List<UserConnectionViewModel>>> GetUserConnectionsAsync(string userId, CancellationToken token = default);

    /// <summary>
    /// Renames a device. An empty name or one longer than <see cref="Services.UserDeviceService.MaxDeviceDisplayNameLength"/>
    /// characters fails with a warning, and nothing is sent.
    /// </summary>
    Task<OperationResult> RenameUserDeviceAsync(string userId, string deviceId, string displayName, CancellationToken token = default);

    /// <summary>
    /// Deletes devices, which signs them out. Deleting the device of this session's access token is refused with a
    /// warning, as is any device of the signed-in admin's own account while the session's device is unknown.
    /// </summary>
    Task<OperationResult> DeleteUserDevicesAsync(string userId, IReadOnlyCollection<string> deviceIds, CancellationToken token = default);
}
