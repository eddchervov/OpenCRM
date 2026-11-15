namespace OpenCRM.Modules.Organizations.Export;

/// <summary> Освобождает слот скачивания вместе с потоком ответа, включая обрыв соединения. </summary>
internal sealed class OrganizationDataExportReadStream(Stream source, OrganizationDataExportLock lease) : Stream
{
    private int _disposed;

    public override bool CanRead => source.CanRead;
    public override bool CanSeek => source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => source.Read(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        source.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        source.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void Flush() => source.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref this._disposed, 1) == 0)
        {
            try
            {
                source.Dispose();
            }
            finally
            {
                lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await source.DisposeAsync();
        }
        finally
        {
            await lease.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
