namespace SynapseAdmin.Models.ViewModels;

/// <summary>What the Reset password dialog returns to the user page (#584).</summary>
public class UserPasswordResetViewModel
{
    /// <summary>The shortest password the dialogs accept, as for a new user (<see cref="UserCreateViewModel"/>).</summary>
    public const int MinPasswordLength = 8;

    public string Password { get; set; } = string.Empty;

    /// <summary>Off by default: logging out every session of a user is disruptive, so the admin turns it on deliberately.</summary>
    public bool LogoutDevices { get; set; }
}

/// <summary>What the Reactivate dialog returns to the user page (#584). An empty password means none.</summary>
public class UserReactivateViewModel
{
    public string? Password { get; set; }
}

/// <summary>
/// What the Edit profile dialog returns to the user page: only what the admin changed (#584). <see cref="DisplayName"/>
/// is <c>null</c> when unchanged, and <c>""</c> when emptied, which removes the display name.
/// </summary>
public class UserProfileEditViewModel
{
    public string? DisplayName { get; set; }

    public bool RemoveAvatar { get; set; }

    public bool HasChanges => DisplayName != null || RemoveAvatar;
}
