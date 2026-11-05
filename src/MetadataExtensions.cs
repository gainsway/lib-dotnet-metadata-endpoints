using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Gainsway.MetadataEndpoints;

public static class MetadataExtensions
{
    // Written by the WriteOpenApiSpecHashes target this package ships, and copied to the output folder.
    internal const string OpenApiSpecHashesFileName = "openapi-hashes.json";

    public static void AddMetadataEndpoints(this WebApplication app) =>
        app.AddMetadataEndpoints(AppContext.BaseDirectory);

    internal static void AddMetadataEndpoints(this WebApplication app, string hashesDirectory)
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
            CommitShortSha = BuildValue(app.Configuration["GIT_SHORT_SHA"]),
            OpenApiSpecHashes = ReadOpenApiSpecHashes(
                Path.Combine(hashesDirectory, OpenApiSpecHashesFileName)
            ),
            Environment = app.Environment.EnvironmentName,
            InstanceName = Environment.MachineName,
        };

        app.MapGet("/metadata", () => TypedResults.Ok(meta))
            .WithTags(["Metadata"])
            .WithSummary(
                "Returns the service name, release tag, commit SHA, OpenAPI spec hashes, ASP.NET environment and instance name"
            );
    }

    // The build sets these values. When they are absent, the build identity is unavailable,
    // so the response says null rather than a blank that looks like a real value.
    private static string? BuildValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    // A missing file means the spec versions are unavailable. A file that exists but can't be
    // read is a build mistake, so it fails at startup rather than reporting partial hashes.
    private static IReadOnlyDictionary<string, string>? ReadOpenApiSpecHashes(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        Dictionary<string, string?>? hashes;
        try
        {
            hashes = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                File.ReadAllText(path)
            );
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{path} is not valid JSON.", exception);
        }

        if (hashes is null || hashes.Any(hash => string.IsNullOrWhiteSpace(hash.Value)))
        {
            throw new InvalidOperationException(
                $"{path} must be a JSON object mapping each spec file to its hash."
            );
        }

        return new SortedDictionary<string, string>(
            hashes.ToDictionary(hash => hash.Key, hash => hash.Value!),
            StringComparer.Ordinal
        );
    }
}
