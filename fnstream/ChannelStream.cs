using System.Buffers;
using System.Threading.Channels;

namespace fnstream;

/// <summary>
///     Represents a custom stream implementation that reads data from a
///     <see cref="System.Threading.Channels.ChannelReader{T}" />.
///     This class is designed to enable stream consumption of asynchronous data
///     provided by a channel sequentially with minimal allocations.
/// </summary>
internal sealed class ChannelStream(ChannelReader<IMemoryOwner<byte>> reader) : Stream
{
    private ReadOnlySequence<byte> _current;
    private IMemoryOwner<byte>? _currentOwner;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 0;

    public override long Position
    {
        get => 0;
        set { }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public async override ValueTask<int> ReadAsync(Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        if (destination.Length == 0)
        {
            return 0;
        }

        if (_current.Length == 0)
        {
            if (!await TryLoadNextBufferAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }
        }

        long bytesToCopyLong = Math.Min(destination.Length, _current.Length);
        int bytesToCopy = (int)bytesToCopyLong;
        _current.Slice(0, bytesToCopy).CopyTo(destination.Span);
        _current = _current.Slice(bytesToCopy);

        return bytesToCopy;
    }

    async private ValueTask<bool> TryLoadNextBufferAsync(CancellationToken cancellationToken)
    {
        _currentOwner?.Dispose();
        _currentOwner = null;

        while (true)
            try
            {
                IMemoryOwner<byte> owner = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (owner.Memory.Length == 0)
                {
                    owner.Dispose();
                    continue;
                }

                _currentOwner = owner;
                _current = new ReadOnlySequence<byte>(owner.Memory);
                return true;
            }
            catch (ChannelClosedException)
            {
                return false;
            }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentOwner?.Dispose();
            _currentOwner = null;
        }
        base.Dispose(disposing);
    }
}