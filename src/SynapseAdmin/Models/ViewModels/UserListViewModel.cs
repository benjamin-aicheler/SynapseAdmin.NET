namespace SynapseAdmin.Models.ViewModels;

public class UserListViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool Deactivated { get; set; }
    public bool Admin { get; set; }
    /// <summary>The account's creation time in Unix milliseconds, or <c>null</c> if the homeserver doesn't know it.</summary>
    public long? CreationTs { get; set; }
    public DateTimeOffset? CreationTsDateTime => CreationTs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    public string UserType { get; set; } = string.Empty;
    public bool Locked { get; set; }
    public bool IsGuest { get; set; }
}
