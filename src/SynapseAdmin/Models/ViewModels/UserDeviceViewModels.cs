namespace SynapseAdmin.Models.ViewModels;

/// <summary>One of a user's devices (#598). A missing name, IP, user agent or time is <c>null</c> and shown as unknown.</summary>
public class UserDeviceViewModel
{
    public string DeviceId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? LastSeenIp { get; set; }
    public string? LastSeenUserAgent { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public bool Dehydrated { get; set; }
}

/// <summary>One recent connection of a user, as <c>whois</c> reports it (#598).</summary>
public class UserConnectionViewModel
{
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
}
