using System.Collections.Frozen;

namespace SynapseAdmin.Infrastructure.Helpers;

public static class MediaHelper
{
    private static readonly FrozenDictionary<string, string> MediaTypeToExtension = new Dictionary<string, string>
    {
        // Images
        { "image/jpeg", ".jpg" },
        { "image/png", ".png" },
        { "image/gif", ".gif" },
        { "image/webp", ".webp" },
        { "image/svg+xml", ".svg" },
        { "image/bmp", ".bmp" },
        { "image/x-icon", ".ico" },
        { "image/tiff", ".tiff" },
        
        // Video
        { "video/mp4", ".mp4" },
        { "video/webm", ".webm" },
        { "video/ogg", ".ogv" },
        { "video/quicktime", ".mov" },
        { "video/x-msvideo", ".avi" },
        
        // Audio
        { "audio/mpeg", ".mp3" },
        { "audio/ogg", ".ogg" },
        { "audio/wav", ".wav" },
        { "audio/webm", ".webm" },
        { "audio/flac", ".flac" },
        { "audio/aac", ".aac" },

        // Application
        { "application/pdf", ".pdf" },
        { "application/zip", ".zip" },
        { "application/x-tar", ".tar" },
        { "application/x-rar-compressed", ".rar" },
        { "application/x-7z-compressed", ".7z" },
        { "application/json", ".json" },
        { "application/xml", ".xml" },
        { "application/msword", ".doc" },
        { "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx" },
        { "application/vnd.ms-excel", ".xls" },
        { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx" },
        { "application/vnd.ms-powerpoint", ".ppt" },
        { "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx" },
        
        // Text
        { "text/plain", ".txt" },
        { "text/html", ".html" },
        { "text/css", ".css" },
        { "text/javascript", ".js" },
        { "text/markdown", ".md" }
    }.ToFrozenDictionary();

    public static string GetExtensionFromMediaType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType)) return ".bin";
        
        // Handle types with parameters (e.g. text/html; charset=utf-8)
        var cleanType = mediaType.Split(';')[0].Trim().ToLowerInvariant();
        
        if (MediaTypeToExtension.TryGetValue(cleanType, out var extension))
        {
            return extension;
        }

        return ".bin";
    }

    public static string GetMediaIdFromMxc(string? mxc)
    {
        if (string.IsNullOrEmpty(mxc)) return "media";
        if (!mxc.StartsWith("mxc://")) return mxc;
        
        var parts = mxc.Split('/');
        return parts.Last();
    }
}
