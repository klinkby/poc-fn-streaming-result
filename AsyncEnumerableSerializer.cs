async private static Task WriteStreamAsync<T>(
    ChannelWriter<Memory<byte>> writer,
    IAsyncEnumerable<T> data,
    JsonSerializerOptions serializerOptions,
    CancellationToken cancellation)
{
    try
    {
        JsonWriterOptions writerOptions = new()
        {
            Encoder = serializerOptions.Encoder,
            Indented = serializerOptions.WriteIndented
        };

        // Reuse buffer to minimize allocations
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = bufferOwner.Memory.Span;

        await writer.WriteAsync(OpenArray, cancellation);

        bool first = true;
        await foreach (T item in data.WithCancellation(cancellation))
        {
            if (!first)
            {
                await writer.WriteAsync(Comma, cancellation);
            }
            else
            {
                first = false;
            }

            // Reset buffer position
            buffer = bufferOwner.Memory.Span;

            using var jsonWriter = new Utf8JsonWriter(buffer, writerOptions);
            JsonSerializer.Serialize(jsonWriter, item, serializerOptions);
            jsonWriter.Flush();

            // Write directly from the buffer without creating a new array
            await writer.WriteAsync(buffer.Slice(0, (int)jsonWriter.BytesWritten), cancellation);
        }

        await writer.WriteAsync(CloseArray, cancellation);
        writer.TryComplete();
    }
    catch (Exception ex)
    {
        writer.TryComplete(ex);
    }
}
