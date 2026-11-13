namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// A read-only view of an HTTP response body that also owns the <see cref="HttpResponseMessage"/>, so that
/// disposing the stream releases the response (and its pooled connection) without leaking the message itself
/// beyond the gateway layer.
/// </summary>
internal sealed class ResponseOwningStream(HttpResponseMessage response, Stream inner) : Stream
{
    private int _disposed;

    public static async Task<Stream> OpenAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var inner = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new ResponseOwningStream(response, inner);
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
        inner.CopyToAsync(destination, bufferSize, cancellationToken);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            inner.Dispose();
            response.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await inner.DisposeAsync();
            response.Dispose();
        }
        await base.DisposeAsync();
    }
}
