namespace SynapseAdmin.Models.ViewModels;

public class UserDetailViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public byte[]? AvatarData { get; set; }
    public bool Deactivated { get; set; }
    public bool Admin { get; set; }
    public long CreationTs { get; set; }
    public DateTime CreationTsDateTime => DateTimeOffset.FromUnixTimeMilliseconds(CreationTs).DateTime;
    public string UserType { get; set; } = string.Empty;
    public bool Locked { get; set; }
    public bool ShadowBanned { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public string ConsentServerNoticeSent { get; set; } = string.Empty;
    public string AppserviceId { get; set; } = string.Empty;
    
    // Threepids, external ids etc could be added here as flattened properties or specific VMs
    
    public UserMediaViewModel? Media { get; set; }
    public List<UserMembershipViewModel> Memberships { get; set; } = [];
}

public class UserMembershipViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string Membership { get; set; } = string.Empty;
}

public class UserMediaViewModel
{
    public long TotalCount { get; set; }
    public long TotalSize { get; set; }
    public List<UserMediaItemViewModel> Media { get; set; } = [];
}

public class UserMediaItemViewModel
{
    public string MediaId { get; set; } = string.Empty;
    public string? UploadName { get; set; }
    public string? MediaType { get; set; }
    public long MediaLength { get; set; }
    public long CreatedTimestamp { get; set; }

    public string DownloadName
    {
        get
        {
            if (!string.IsNullOrEmpty(UploadName)) return UploadName;
            var mediaIdPart = "media";
            try
            {
                mediaIdPart = LibMatrix.StructuredData.MxcUri.Parse(MediaId).MediaId;
            }
            catch
            {
                // Fallback to "media" if parsing fails
            }

            return mediaIdPart + Infrastructure.Helpers.MediaHelper.GetExtensionFromMediaType(MediaType);
        }
    }
}
