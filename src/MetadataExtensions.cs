using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Gainsway.MetadataEndpoints;

public static class MetadataExtensions
{
    public static void AddMetadataEndpointss(this WebApplication app)
    {
        var meta = new MetadataDto
        {
            ServiceName = app.Configuration["ServiceName"] ?? string.Empty,
            CommitShortSha = app.Configuration["GIT_SHORT_SHA"] ?? string.Empty,
            InstanceName = Environment.MachineName
        };

        app.MapGet("/metadata", () => Results.Ok(meta))
            .WithTags(["Metadata"])
            .WithOpenApi(operation =>
                new(operation)
                {
                    Summary =
                        "Returns metadata about the service like git sha, service name, and instance name",
                }
            );
    }
}
