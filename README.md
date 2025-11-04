# fnstream — Streaming collection results from Azure Functions HTTP Trigger (PoC)

This proof‑of‑concept demonstrates how to return large JSON collections from an Azure Functions HTTP trigger using true server‑side streaming, as opposed to buffering the entire collection in memory first.

It shows:
- A traditional buffered response that materializes the entire list (`/api/buffered`).
- A streaming response that writes each element as it becomes available (`/api/streamed`).

The goal is to reduce peak memory usage and begin sending data to the client earlier, while keeping the JSON output valid and compatible with standard clients.

---

## Projects
- `fnstream/` — Azure Functions (isolated worker) app with two endpoints implemented in `MyFunction`.
- `bench/` — BenchmarkDotNet micro‑benchmarks that compare buffered vs streaming serialization using the same data source.

Target framework: .NET 10.0

---

## Endpoints
Azure Functions routes (default prefix `api/`):
- `GET /api/buffered` — Buffers the entire collection in memory, then writes a single JSON array (default Fn behavior).
- `GET /api/streamed` — Streams a JSON array response. Items are produced on demand and written to the output as they are generated.

See `fnstream/MyFunction.cs` for details.

---

## Benchmark results
The PoC includes `bench.exe` to compare the two approaches. A representative run produced the following:

```
| Method    | Mean     | Error   | StdDev   | Median   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------- |---------:|--------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| Blocking  | 153.6 us | 5.20 us | 15.34 us | 147.9 us |  1.01 |    0.14 | 23.9258 | 4.8828 | 196.07 KB |        1.00 |
| Streaming | 332.3 us | 6.54 us |  5.80 us | 331.4 us |  2.18 |    0.21 | 20.5078 |      - | 170.53 KB |        0.87 |
```

Interpretation (for the current setup with 250 items):
- Streaming uses 13% less memory (170KB vs 196KB) with no Gen1 collections observed.
- Streaming reduces Gen0 pressure slightly (20.5 vs 23.9 per operation).
- Buffered is faster in raw CPU time for this specific payload size, but at the cost of higher allocation and delayed first byte.
- In end‑to‑end scenarios with network latency and larger payloads, streaming's reduced memory footprint and progressive delivery can be advantageous.

---

## How it works (high level)
- Data source: `MyFunction.GenerateData(int)` yields items asynchronously (`IAsyncEnumerable<MyDto>`), simulating a producer that can yield results incrementally.
- Streaming serialization: the collection is serialized as a valid JSON array while items are produced, writing `[` then each element (with commas) and finally `]`.
- The implementation wires an async sequence to the HTTP response body to avoid materializing the entire list.
- Cancellation is respected and backpressure is naturally applied by the HTTP output stream.

### Memory optimization
The streaming implementation uses several techniques to minimize heap allocations:
- **Pooled memory**: Uses `MemoryPool<byte>.Shared` to rent buffers instead of allocating new arrays for each item.
- **Buffer reuse**: A single `ArrayBufferWriter<byte>` and `Utf8JsonWriter` are reused across all items in the stream.
- **Zero-copy transfer**: `IMemoryOwner<byte>` instances pass ownership of pooled memory through a channel, eliminating unnecessary copies.
- **Explicit disposal**: `ChannelStream` disposes of each memory owner after consumption, immediately returning buffers to the pool.
- **ConfigureAwait(false)**: All async operations avoid capturing `SynchronizationContext`, reducing allocation overhead.

Key files to explore:
- `fnstream/MyFunction.cs` — Defines `/api/streamed` and `/api/buffered`.
- `fnstream/AsyncEnumerableSerializer.cs` — Serializer utilities to turn an `IAsyncEnumerable<T>` into a JSON stream with minimal allocations.
- `fnstream/ChannelStream.cs` — Bridges asynchronous production to a readable `Stream`, managing pooled memory lifecycle.

---

## Running locally
Prerequisites:
- .NET SDK 10.0+
- Azure Functions Core Tools v4 (for local Functions host)

1) Run the benchmarks (Release build recommended):
```
dotnet run -c Release --project .\bench
```

2) Start the Azure Functions app locally:
- Using Functions Core Tools (recommended):
```
func start --csharp --prefix api --dotnet-isolated
```
  or simply run from the project directory:
```
dotnet run --project .\fnstream
```

Then browse:
- http://localhost:7071/api/streamed
- http://localhost:7071/api/buffered

Note: Port and base URL may vary depending on your local host settings.

---

## When to use streaming
- Very large collections where buffering would cause high memory usage.
- Scenarios where you want clients to receive the first results as soon as possible.
- Long‑running pipelines that gradually produce output.

Potential trade‑offs:
- Slightly higher CPU overhead per element in some cases.
- Client must tolerate chunked/streamed responses (most HTTP clients do).

---

## Caveats and considerations
- The JSON output remains a single valid array; items are not emitted as separate JSON documents.
- Ensure downstream systems/clients parse streaming JSON correctly. Many HTTP clients will read the stream progressively while still producing a final in‑memory object if you ask them to.
- Be mindful of timeouts and cancellation; the sample propagates `CancellationToken` throughout.

---

## License
This repository is a PoC and provided as‑is for demonstration purposes.
