using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HomeScreenCleanup.Web;

/// <summary>
/// Write-only stream that buffers up to a fixed capacity. On overflow it flushes what it buffered to the
/// pass-through stream and forwards every later write unchanged, so memory use stays bounded.
/// </summary>
internal sealed class CappedBufferStream : Stream
{
    private readonly MemoryStream _buffer = new();
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Not owned: the original response body stream belongs to the server.")]
    private readonly Stream _passthrough;
    private readonly int _capacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="CappedBufferStream"/> class.
    /// </summary>
    /// <param name="passthrough">The stream to forward to after overflow (not owned).</param>
    /// <param name="capacity">The buffer capacity in bytes.</param>
    public CappedBufferStream(Stream passthrough, int capacity)
    {
        ArgumentNullException.ThrowIfNull(passthrough);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _passthrough = passthrough;
        _capacity = capacity;
    }

    /// <summary>
    /// Gets a value indicating whether the capacity was exceeded and the stream switched to pass-through.
    /// </summary>
    public bool Overflowed { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the buffered bytes (empty after overflow).
    /// </summary>
    /// <returns>The buffered bytes, valid until the stream is written to or disposed.</returns>
    public ReadOnlyMemory<byte> GetBufferedBytes()
    {
        return _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);
    }

    /// <inheritdoc />
    public override void Flush()
    {
        if (Overflowed)
        {
            _passthrough.Flush();
        }
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Overflowed ? _passthrough.FlushAsync(cancellationToken) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Overflowed)
        {
            _passthrough.Write(buffer);
            return;
        }

        if (_buffer.Length + buffer.Length <= _capacity)
        {
            _buffer.Write(buffer);
            return;
        }

        Overflowed = true;
        _passthrough.Write(_buffer.GetBuffer().AsSpan(0, (int)_buffer.Length));
        _buffer.SetLength(0);
        _passthrough.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Overflowed)
        {
            await _passthrough.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_buffer.Length + buffer.Length <= _capacity)
        {
            _buffer.Write(buffer.Span);
            return;
        }

        Overflowed = true;
        var buffered = _buffer.ToArray();
        _buffer.SetLength(0);
        await _passthrough.WriteAsync(buffered, cancellationToken).ConfigureAwait(false);
        await _passthrough.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Dispose();
        }

        base.Dispose(disposing);
    }
}
