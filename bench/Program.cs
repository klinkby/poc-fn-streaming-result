using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using fnstream;

BenchmarkRunner.Run<StreamingVsBlocking>();

[MemoryDiagnoser]
//[ShortRunJob]
public class StreamingVsBlocking
{
    
    private const int CollectionSize = 250;

    [Benchmark(Baseline = true)]
    public async Task Blocking()
    {
        List<MyDto> list = await MyFunction.GenerateData(CollectionSize).ToListAsync();
        var bufferedStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(bufferedStream, list, AsyncEnumerableSerializer.DefaultSerializerOptions);
    }
    
    [Benchmark]
    public async Task Streaming()
    {
        Stream source = MyFunction.GenerateData(CollectionSize).ToJsonSteam();
        await source.CopyToAsync(Stream.Null);
    }
}