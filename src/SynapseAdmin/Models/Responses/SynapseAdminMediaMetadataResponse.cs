using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminMediaMetadataResponse
{
    [JsonPropertyName("media_info")]
    public MediaInfo? Info { get; set; }

    public class MediaInfo
    {
        [JsonPropertyName("media_origin")]
        public string MediaOrigin { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("media_id")]
        public string MediaId { get; set; } = string.Empty;

        [JsonPropertyName("media_type")]
        public string MediaType { get; set; } = string.Empty;

        [JsonPropertyName("media_length")]
        public long MediaLength { get; set; }

        [JsonPropertyName("upload_name")]
        public string? UploadName { get; set; }

        [JsonPropertyName("created_ts")]
        public long CreatedTimestamp { get; set; }

        [JsonPropertyName("filesystem_id")]
        public string? FilesystemId { get; set; }

        [JsonPropertyName("url_cache")]
        public string? UrlCache { get; set; }

        [JsonPropertyName("last_access_ts")]
        public long? LastAccessTimestamp { get; set; }

        [JsonPropertyName("quarantined_by")]
        public string? QuarantinedBy { get; set; }

        [JsonPropertyName("authenticated")]
        public bool? Authenticated { get; set; }

        [JsonPropertyName("safe_from_quarantine")]
        public bool? SafeFromQuarantine { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }
}
