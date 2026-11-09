## lib-dotnet-metadata-endpoints

This library add metadata endpoints to a .NET Core Api

## Configuration

1. Install `Gainsway.MetadataEndpoints` package in the target project
  ```sh
  dotnet add package Gainsway.MetadataEndpoints
  ```
2. Register the Metadata Endpoints services in `Program.cs`:
  ```csharp
    /// Program.cs

    //... 
    app.AddMetadataEndpoints();

    //...
    app.MapControllers();
    app.Run();
  ```

## Response

`GET /metadata` returns:

| Field | Source | Notes |
| --- | --- | --- |
| `serviceName` | `ServiceName` configuration value | Required. The app fails at startup when it is missing or blank. |
| `releaseTag` | `GIT_REF_NAME` env var | The release tag the build supplied, for example `v2.2.3`. |
| `commitShortSha` | `GIT_SHORT_SHA` env var | The short commit SHA, 7 characters. |
| `openApiSpecHashes` | committed `openapi-hashes.json` | The SHA-256 of each OpenAPI spec file, keyed by file name. |
| `environment` | ASP.NET environment name | Not a full configuration identity. |
| `instanceName` | Machine name | The pod name in Kubernetes. |

The shared image build passes `GIT_REF_NAME` and `GIT_SHA` as build args, and each service's Dockerfile sets them as env vars.

`releaseTag` and `commitShortSha` are `null` when the value is missing or blank. `null` means the build identity is unavailable. A local `dotnet run` returns `null`, and so would a misconfigured image, so any release check must reject `null`.

A response describes the one instance that answered. During a rollout, check the rollout status as well.

## OpenAPI spec hashes

The package adds an MSBuild target to the API project that references it. After the project's Debug `PostBuild` step generates its specs, the target hashes every `openapi*.json` in the repo root and writes `openapi-hashes.json` into the API project folder. Commit that file with the specs it describes. The package copies it into build and publish output, so a Release build or the Docker image carries the committed hashes without regenerating them.

- The hashes cover each file's exact bytes, so `sha256sum` gives the same values.
- The file is rewritten from scratch each time, so a deleted spec drops out. Delete a spec file when its document is no longer generated.
- Set `OpenApiSpecDirectory` in the API project if the specs are not at the repo root.
- Run `dotnet msbuild -t:WriteOpenApiSpecHashes` to rehash specs generated another way, such as from a Release build.

`openApiSpecHashes` is `null` when the file is missing. The app fails at startup if the file exists but is not a JSON object of file names to hashes.