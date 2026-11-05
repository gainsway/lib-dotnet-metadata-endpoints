using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Gainsway.MetadataEndpoints;

public static class MetadataExtensions
{
    public static void AddMetadataEndpoints(this WebApplication app)
    {
        var serviceName = app.Configuration["ServiceName"];
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new InvalidOperationException(
                "The ServiceName configuration value is missing or blank."
            );
        }

        var meta = new MetadataDto
        {
            ServiceName = serviceName,
            ReleaseTag = BuildValue(app.Configuration["GIT_REF_NAME"]),
            CommitSha = BuildValue(app.Configuration["GIT_SHORT_SHA"]),
            Environment = app.Environment.EnvironmentName,
            InstanceName = Environment.MachineName,
        };

        app.MapGet("/metadata", () => TypedResults.Ok(meta))
            .WithTags(["Metadata"])
            .WithSummary(
                "Returns the service name, release tag, commit SHA, ASP.NET environment and instance name"
            );
    }

    // The build sets these values. When they are absent, the build identity is unavailable,
    // so the response says null rather than a blank that looks like a real value.
    private static string? BuildValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
