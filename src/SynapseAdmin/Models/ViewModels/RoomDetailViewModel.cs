namespace SynapseAdmin.Models.ViewModels;

public class RoomDetailViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? CanonicalAlias { get; set; }
    public int JoinedMembers { get; set; }
    public int JoinedLocalMembers { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string? Encryption { get; set; }
    public bool Federated { get; set; }
    public bool Public { get; set; }
    public string? AvatarUrl { get; set; }
    public string? JoinRules { get; set; }
    public string? GuestAccess { get; set; }
    public string? HistoryVisibility { get; set; }
    public string? RoomType { get; set; }
    public bool Forgotten { get; set; }
    
    // We could add memberships or state here later
    public bool IsTombstoned { get; set; }
    public string? ReplacementRoom { get; set; }
    
    public List<RoomMemberViewModel> Members { get; set; } = [];
    public List<RoomStateEventViewModel> StateEvents { get; set; } = [];
    public RoomMediaViewModel? Media { get; set; }
}

public class RoomMemberViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Membership { get; set; }
}

public class RoomStateEventViewModel
{
    public string Type { get; set; } = string.Empty;
    public string? StateKey { get; set; }
    public string? Sender { get; set; }
    public string? RawContent { get; set; }
}

public class RoomMediaViewModel
{
    public List<RoomMediaItemViewModel> Local { get; set; } = [];
    public List<RoomMediaItemViewModel> Remote { get; set; } = [];
}

public class RoomMediaItemViewModel
{
    public string MediaId { get; set; } = string.Empty;
    public string? UploadName { get; set; }
    public string? MediaType { get; set; }
    public long MediaLength { get; set; }
    public long CreatedTimestamp { get; set; }
    public string? QuarantinedBy { get; set; }
    public bool IsQuarantined => !string.IsNullOrEmpty(QuarantinedBy);
    public bool SafeFromQuarantine { get; set; }
    public bool IsLocal { get; set; }

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
