namespace SynapseAdmin.Models.ViewModels;

public class UserListViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool Deactivated { get; set; }
    public bool Admin { get; set; }
    public long CreationTs { get; set; }
    public DateTimeOffset CreationTsDateTime => DateTimeOffset.FromUnixTimeMilliseconds(CreationTs);
    public string UserType { get; set; } = string.Empty;
    public bool Locked { get; set; }
    public bool IsGuest { get; set; }
}
