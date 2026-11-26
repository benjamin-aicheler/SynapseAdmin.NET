namespace SynapseAdmin.Models.ViewModels;

public class UserDetailViewModel
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
    public bool ShadowBanned { get; set; }
    public bool Suspended { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public string ConsentServerNoticeSent { get; set; } = string.Empty;
    public string AppserviceId { get; set; } = string.Empty;

    /// <summary>
    /// The account's expiration, if known. Synapse's user details don't include it, so it is <c>null</c> (unknown, not
    /// "never expires") until a renewal on the page sets it (#546).
    /// </summary>
    public long? ExpirationTs { get; set; }
    public DateTimeOffset? ExpirationTsDateTime => ExpirationTs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ExpirationTs.Value) : null;

    public List<UserMembershipViewModel> Memberships { get; set; } = [];
    public List<UserExperimentalFeatureViewModel>? ExperimentalFeatures { get; set; }
}

public class UserExperimentalFeatureViewModel
{
    public string FeatureKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool IsUpdating { get; set; }
}

public class AccountValidityRenewViewModel
{
    public string UserId { get; set; } = string.Empty;
    public long? ExpirationTs { get; set; }
    public bool EnableRenewalEmails { get; set; } = true;
}

public class UserMembershipViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string Membership { get; set; } = string.Empty;
}

/// <summary>One server-side page of a user's uploads. Statistics on this type describe the page only.</summary>
public class UserMediaPageViewModel
{
    /// <summary>Offset of the first item of this page within all uploads.</summary>
    public int Offset { get; set; }

    /// <summary>Number of uploads across all pages, as reported by the server.</summary>
    public long TotalCount { get; set; }

    public List<UserMediaItemViewModel> Items { get; set; } = [];

    /// <summary>Bytes of the items on this page only.</summary>
    public long PageBytes => Items.Sum(i => i.MediaLength);
}

/// <summary>Totals across all of a user's uploads (from the user media statistics endpoint).</summary>
public class UserMediaTotalsViewModel
{
    public long Count { get; set; }
    public long Bytes { get; set; }
}

public class UserMediaItemViewModel
{
    public string MediaId { get; set; } = string.Empty;
    public string? UploadName { get; set; }
    public string? MediaType { get; set; }
    public long MediaLength { get; set; }
    public long CreatedTimestamp { get; set; }
    public string? QuarantinedBy { get; set; }
    public bool IsQuarantined => !string.IsNullOrEmpty(QuarantinedBy);
    public bool SafeFromQuarantine { get; set; }

    public string DownloadName
    {
        get
        {
            if (!string.IsNullOrEmpty(UploadName)) return UploadName;
            var mediaIdPart = Infrastructure.Helpers.MediaHelper.GetMediaIdFromMxc(MediaId);
            return mediaIdPart + Infrastructure.Helpers.MediaHelper.GetExtensionFromMediaType(MediaType);
        }
    }
}
