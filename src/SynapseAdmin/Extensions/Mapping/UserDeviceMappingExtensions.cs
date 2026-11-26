using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using Device = SynapseAdmin.Models.Responses.SynapseAdminUserDeviceListResult.SynapseAdminUserDevice;

namespace SynapseAdmin.Extensions.Mapping;

public static class UserDeviceMappingExtensions
{
    public static UserDeviceViewModel ToViewModel(this Device device)
    {
        return new UserDeviceViewModel
        {
            DeviceId = device.DeviceId,
            DisplayName = device.DisplayName,
            LastSeenIp = device.LastSeenIp,
            LastSeenUserAgent = device.LastSeenUserAgent,
            LastSeen = FromUnixMilliseconds(device.LastSeenTs),
            // Tuwunel sends dehydrated only for a user who has a dehydrated device.
            Dehydrated = device.Dehydrated == true
        };
    }

    public static List<UserDeviceViewModel> ToViewModels(this IEnumerable<Device> devices)
    {
        return devices.Select(ToViewModel).ToList();
    }

    public static UserConnectionViewModel ToViewModel(this SynapseAdminWhoisResponse.WhoisConnection connection)
    {
        return new UserConnectionViewModel
        {
            Ip = connection.Ip,
            UserAgent = connection.UserAgent,
            LastSeen = FromUnixMilliseconds(connection.LastSeen)
        };
    }

    /// <summary>
    /// The connections of every device key and session in one list, newest first, those without a time last. Synapse
    /// and Tuwunel put them all under the empty device key.
    /// </summary>
    public static List<UserConnectionViewModel> ToConnectionViewModels(this SynapseAdminWhoisResponse whois)
    {
        return (whois.Devices ?? [])
            .SelectMany(d => d.Value?.Sessions ?? [])
            .SelectMany(s => s?.Connections ?? [])
            .Where(c => c != null)
            .Select(ToViewModel)
            .OrderByDescending(c => c.LastSeen.HasValue)
            .ThenByDescending(c => c.LastSeen)
            .ToList();
    }

    // A device that was never seen has no time; 0 means the same.
    private static DateTimeOffset? FromUnixMilliseconds(long? milliseconds) =>
        milliseconds is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value) : null;
}
