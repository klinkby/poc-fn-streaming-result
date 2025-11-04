using System.Buffers;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;

namespace fnstream;

public static class AsyncEnumerableSerializer
{
    private sealed class OwnedArray : IMemoryOwner<byte>
    {
        private readonly byte[]? _array;
        private readonly IMemoryOwner<byte>? _owner;
        private readonly int _length;
        private readonly bool _returnable;

        public OwnedArray(byte[] array, int length, bool returnable)
        {
            _array = array;
            _length = length;
            _returnable = returnable;
        }

        public OwnedArray(IMemoryOwner<byte> owner, int length)
        {
            _owner = owner;
            _length = length;
            _returnable = true;
        }

        public Memory<byte> Memory => _array != null
            ? new(_array, 0, _length)
            : _owner!.Memory[.._length];

        public void Dispose()
        {
            if (_returnable)
                _owner?.Dispose();
        }
    }

    private readonly static IMemoryOwner<byte> OpenArray = new OwnedArray([(byte)'['], 1, false);
    private readonly static IMemoryOwner<byte> CloseArray = new OwnedArray([(byte)']'], 1, false);
    private readonly static IMemoryOwner<byte> Comma = new OwnedArray([(byte)','], 1, false);

    private readonly static UnboundedChannelOptions ChannelOptions = new()
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false
        
    };

    public readonly static JsonSerializerOptions DefaultSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Stream ToJsonSteam<T>(this IAsyncEnumerable<T> data, JsonSerializerOptions? opts = null,
        CancellationToken cancellation = default)
    {
        var channel = Channel.CreateUnbounded<IMemoryOwner<byte>>(ChannelOptions);
        _ = WriteStreamAsync(channel.Writer, data, opts ?? DefaultSerializerOptions, cancellation);
        return new ChannelStream(channel.Reader);
    }

    public static IActionResult ToActionResult<T>(this IAsyncEnumerable<T> data, JsonSerializerOptions? opts = null,
        CancellationToken cancellation = default)
        => new FileStreamResult(data.ToJsonSteam(opts, cancellation),
            MediaTypeNames.Application.Json);

    async private static Task WriteStreamAsync<T>(
        ChannelWriter<IMemoryOwner<byte>> writer,
        IAsyncEnumerable<T> data,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellation)
    {
        ArrayBufferWriter<byte> buffer = new(256);

        try
        {
            JsonWriterOptions writerOptions = new()
            {
                Encoder = serializerOptions.Encoder,
                Indented = serializerOptions.WriteIndented
            };

            await writer.WriteAsync(OpenArray, cancellation).ConfigureAwait(false);

            bool first = true;
            using Utf8JsonWriter jsonWriter = new(buffer, writerOptions);

            await foreach (T item in data.WithCancellation(cancellation).ConfigureAwait(false))
            {
                if (!first)
                {
                    await writer.WriteAsync(Comma, cancellation).ConfigureAwait(false);
                }

                first = false;

                buffer.Clear();
                jsonWriter.Reset();
                JsonSerializer.Serialize(jsonWriter, item, serializerOptions);
                jsonWriter.Flush();

                int length = buffer.WrittenCount;
                IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(length);
                buffer.WrittenSpan.CopyTo(memoryOwner.Memory.Span);
                await writer.WriteAsync(new OwnedArray(memoryOwner, length), cancellation).ConfigureAwait(false);
            }

            await writer.WriteAsync(CloseArray, cancellation).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }
}