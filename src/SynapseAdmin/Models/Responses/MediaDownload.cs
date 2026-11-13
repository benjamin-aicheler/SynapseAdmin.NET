namespace SynapseAdmin.Models.Responses;

public enum MediaDownloadStatus
{
    /// <summary>The homeserver returned the whole object (<c>200</c>), possibly ignoring a requested range.</summary>
    Full,

    /// <summary>The homeserver honoured the requested range (<c>206</c>).</summary>
    Partial,

    /// <summary>The requested range lies outside the object (<c>416</c>). <see cref="MediaDownload.Content"/> is empty.</summary>
    RangeNotSatisfiable
}

/// <summary>
/// An open media download from the homeserver. The body has not been read yet; disposing this object
/// (or <see cref="Content"/>) releases the upstream response and its connection.
/// </summary>
public sealed class MediaDownload : IDisposable, IAsyncDisposable
{
    public required MediaDownloadStatus Status { get; init; }

    /// <summary>The response body. Network streams are not seekable.</summary>
    public required Stream Content { get; init; }

    /// <summary>Length of <see cref="Content"/> as declared by the homeserver, if known.</summary>
    public long? ContentLength { get; init; }

    /// <summary>For <see cref="MediaDownloadStatus.Partial"/>: first byte position of the returned range.</summary>
    public long? RangeFrom { get; init; }

    /// <summary>For <see cref="MediaDownloadStatus.Partial"/>: last byte position (inclusive) of the returned range.</summary>
    public long? RangeTo { get; init; }

    /// <summary>Total size of the media object, if the homeserver reported it in <c>Content-Range</c>.</summary>
    public long? CompleteLength { get; init; }

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
