using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace fnstream;

public class MyFunction
{
    private const int CollectionSize = 250;
    
    [Function("StreamedData")]
    public IActionResult StreamedData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "streamed")]
        HttpRequest req,
        CancellationToken cancellation)
    {
        IAsyncEnumerable<MyDto> data = GenerateData(CollectionSize, cancellation);
        return data.ToActionResult(cancellation: cancellation);
    }
    
    [Function("BufferedData")]
    public async Task<IActionResult> StreamingData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "buffered")]
        HttpRequest req,
        CancellationToken cancellation)
    {
        IAsyncEnumerable<MyDto> data = GenerateData(CollectionSize, cancellation);
        List<MyDto> result = await data.ToListAsync(cancellation);
        return new OkObjectResult(result);
    }

    public async static IAsyncEnumerable<MyDto> GenerateData(int length, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        for (int i=0; i<length; i++)
        {
            await Task.Yield();
            yield return new MyDto(i, $"Name{Guid.NewGuid().ToString()}", new string('#', 50));
        }
    }
}