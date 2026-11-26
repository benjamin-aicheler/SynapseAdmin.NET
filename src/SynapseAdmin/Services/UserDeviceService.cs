using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

/// <summary>
/// A local user's devices and recent connections (#598). IP addresses, user agents and device names are personal data:
/// they are shown, but never logged. The log templates carry only user and device IDs.
/// </summary>
public class UserDeviceService(IMatrixSessionService sessionService, ILogger<UserDeviceService> logger, IStringLocalizer<SharedResources> L) : IUserDeviceService
{
    /// <summary>Synapse and Tuwunel refuse a longer device name; both count code points.</summary>
    public const int MaxDeviceDisplayNameLength = 100;

    public Task<OperationResult<List<UserDeviceViewModel>>> GetUserDevicesAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingDevices",
            ex => logger.LogError(ex, "Error fetching devices for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetUserDevicesAsync(userId, token);
                return OperationResult<List<UserDeviceViewModel>>.Ok(result?.Devices.ToViewModels() ?? []);
            });

    public Task<OperationResult<List<UserConnectionViewModel>>> GetUserConnectionsAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingConnections",
            ex => logger.LogError(ex, "Error fetching recent connections for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetUserWhoisAsync(userId, token);
                return OperationResult<List<UserConnectionViewModel>>.Ok(result?.ToConnectionViewModels() ?? []);
            });

    public Task<OperationResult> RenameUserDeviceAsync(string userId, string deviceId, string displayName, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorRenamingDevice",
            ex => logger.LogError(ex, "Error renaming device {DeviceId} of user {UserId}", deviceId.SanitizeForLogging(), userId.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    return OperationResult.Failure(L["DeviceNameRequired"], Severity.Warning);
                }

                if (IsTooLong(displayName))
                {
                    return OperationResult.Failure(L["DeviceNameTooLong", MaxDeviceDisplayNameLength], Severity.Warning);
                }

                await gateway.RenameUserDeviceAsync(userId, deviceId, new SynapseAdminDeviceUpdateRequest { DisplayName = displayName }, token);
                logger.LogInformation("Successfully renamed device {DeviceId} of user {UserId}", deviceId.SanitizeForLogging(), userId.SanitizeForLogging());
                return OperationResult.Ok(L["DeviceRenamedSuccessfully"]);
            });

    public Task<OperationResult> DeleteUserDevicesAsync(string userId, IReadOnlyCollection<string> deviceIds, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingDevices",
            ex => logger.LogError(ex, "Error deleting devices {DeviceIds} of user {UserId}", JoinForLogging(deviceIds), userId.SanitizeForLogging()),
            async gateway =>
            {
                if (deviceIds.Count == 0)
                {
                    return OperationResult.Failure(L["NoDevicesSelected"], Severity.Warning);
                }

                if (IncludesOwnSessionDevice(gateway, userId, deviceIds))
                {
                    logger.LogWarning("Refused to delete devices {DeviceIds} of user {UserId}: they include this session's device, or it is unknown",
                        JoinForLogging(deviceIds), userId.SanitizeForLogging());
                    return OperationResult.Failure(L["CannotDeleteOwnSessionDevice"], Severity.Warning);
                }

                await gateway.DeleteUserDevicesAsync(userId, new SynapseAdminDeleteDevicesRequest { Devices = deviceIds.ToList() }, token);
                logger.LogInformation("Successfully deleted devices {DeviceIds} of user {UserId}", JoinForLogging(deviceIds), userId.SanitizeForLogging());
                return OperationResult.Ok(L["DevicesDeletedSuccessfully", deviceIds.Count]);
            });

    /// <summary>Counted in code points (runes), as Synapse (Python <c>len</c>) and Tuwunel (<c>chars().count()</c>) do.</summary>
    public static bool IsTooLong(string displayName) => displayName.EnumerateRunes().Count() > MaxDeviceDisplayNameLength;

    /// <summary>
    /// Whether deleting <paramref name="deviceIds"/> would sign out this session: they belong to the signed-in user and
    /// include the session's device, or the session's device is unknown. User and device IDs are compared ordinally.
    /// </summary>
    public static bool IncludesOwnSessionDevice(IMatrixGateway gateway, string userId, IEnumerable<string> deviceIds) =>
        string.Equals(userId, gateway.UserId, StringComparison.Ordinal) &&
        (gateway.DeviceId is not { } own || deviceIds.Contains(own, StringComparer.Ordinal));

    private static string? JoinForLogging(IEnumerable<string> deviceIds) => string.Join(", ", deviceIds).SanitizeForLogging();
}
