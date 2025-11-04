using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

await FunctionsApplication
    .CreateBuilder(args)
    .ConfigureFunctionsWebApplication()
    .Build()
    .RunAsync();