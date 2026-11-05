using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace Gainsway.MetadataEndpoints.Tests;

public class MetadataEndpointsTests
{
    private const string FullSha = "8f3c2a1d9b7e4f60a5c3e2d1b0a9f8e7d6c5b4a3";

    [Test]
    public async Task ReturnsTheBuildIdentity()
    {
        await using var app = await StartApp(
            new()
            {
                ["ServiceName"] = "user-service",
                ["GIT_REF_NAME"] = "v2.2.3",
                ["GIT_SHORT_SHA"] = FullSha,
            }
        );

        var body = await GetMetadata(app);

        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("serviceName").GetString(), Is.EqualTo("user-service"));
            Assert.That(body.GetProperty("releaseTag").GetString(), Is.EqualTo("v2.2.3"));
            Assert.That(body.GetProperty("commitSha").GetString(), Is.EqualTo(FullSha));
            Assert.That(body.GetProperty("environment").GetString(), Is.EqualTo("Production"));
            Assert.That(
                body.GetProperty("instanceName").GetString(),
                Is.EqualTo(Environment.MachineName)
            );
            Assert.That(body.TryGetProperty("commitShortSha", out _), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ReturnsExplicitNullWhenTheBuildIdentityIsUnavailable(string? value)
    {
        await using var app = await StartApp(
            new()
            {
                ["ServiceName"] = "user-service",
                ["GIT_REF_NAME"] = value,
                ["GIT_SHORT_SHA"] = value,
            }
        );

        var body = await GetMetadata(app);

        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("releaseTag").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("commitSha").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task ReportsTheAspNetEnvironmentName()
    {
        await using var app = await StartApp(
            new() { ["ServiceName"] = "user-service" },
            environment: "Development"
        );

        var body = await GetMetadata(app);

        Assert.That(body.GetProperty("environment").GetString(), Is.EqualTo("Development"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void FailsAtStartupWithoutAServiceName(string? serviceName)
    {
        var app = BuildApp(new() { ["ServiceName"] = serviceName });

        Assert.Throws<InvalidOperationException>(() => app.AddMetadataEndpoints());
    }

    [Test]
    public async Task DocumentsTheSummaryAndNullableFields()
    {
        await using var app = await StartApp(
            new() { ["ServiceName"] = "user-service" },
            services =>
                services
                    .AddEndpointsApiExplorer()
                    .AddSwaggerGen(options => options.SupportNonNullableReferenceTypes())
        );

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = document.Paths["/metadata"].Operations![HttpMethod.Get];
        var schema = document.Components!.Schemas!["MetadataDto"];

        Assert.Multiple(() =>
        {
            Assert.That(operation.Summary, Does.Contain("release tag"));
            Assert.That(IsNullable(schema, "releaseTag"), Is.True);
            Assert.That(IsNullable(schema, "commitSha"), Is.True);
            Assert.That(IsNullable(schema, "serviceName"), Is.False);
            Assert.That(IsNullable(schema, "environment"), Is.False);
            Assert.That(schema.Properties!.ContainsKey("commitShortSha"), Is.False);
        });
    }

    private static bool IsNullable(IOpenApiSchema schema, string property) =>
        schema.Properties![property].Type!.Value.HasFlag(JsonSchemaType.Null);

    private static async Task<JsonElement> GetMetadata(WebApplication app)
    {
        var response = await app.GetTestClient().GetAsync("/metadata");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<WebApplication> StartApp(
        Dictionary<string, string?> configuration,
        Action<IServiceCollection>? configureServices = null,
        string environment = "Production"
    )
    {
        var app = BuildApp(configuration, configureServices, environment);
        app.AddMetadataEndpoints();
        await app.StartAsync();
        return app;
    }

    private static WebApplication BuildApp(
        Dictionary<string, string?> configuration,
        Action<IServiceCollection>? configureServices = null,
        string environment = "Production"
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment }
        );
        builder.WebHost.UseTestServer();
        // Only the values each test sets, so env vars on the machine running the tests can't leak in.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(configuration);
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}
