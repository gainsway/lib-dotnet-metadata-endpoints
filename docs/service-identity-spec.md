# Spec: `/metadata` reports the service identity

Date: 2026-11-05

## Summary

`/metadata` tells you which build an instance reports running, which ASP.NET environment it started with, and which version of each API spec it carries. Today it returns the service name, the commit and the pod. This change adds the release tag, the environment name and a hash of each generated OpenAPI spec. It also makes every field either a real value or an explicit `null`. Today some fields fall back to an empty string, which looks like a real value.

Most of the change is in this library's code. The spec hashes also need an MSBuild target, which this package ships. It writes the hashes when a service's Debug build generates its specs, so no service needs build, chart or Dockerfile changes beyond the version bump.

## The problem

- **You can't tell from a running instance which release it is.** `/metadata` returns a commit SHA. To know whether `v2.2.3` is running, someone has to look up which commit that tag points to.
- **You can't tell whether a release changed an API contract.** A new release may change only internal code, or it may change a spec. Today the only way to know is to compare spec files by hand.
- **A missing value looks like a real one.** Under `dotnet run`, `CommitShortSha` returns `""`, and a missing `ServiceName` would also return `""`. A blank string reads like a real but empty value.
- **A wrong environment name is invisible.** A prod pod started with `ASPNETCORE_ENVIRONMENT=Development` loads `appsettings.Development.json` and turns on development-only behaviour. Nothing outside the pod shows it.

## The response

| Field | Source | Change |
| --- | --- | --- |
| `ServiceName` | `ServiceName` in `appsettings.json` | fails at startup when missing or blank, instead of `""` |
| `ReleaseTag` | `GIT_REF_NAME` env var | new |
| `CommitShortSha` | `GIT_SHORT_SHA` env var | `null` when missing or blank, instead of `""` |
| `OpenApiSpecHashes` | committed `openapi-hashes.json` | new |
| `Environment` | `app.Environment.EnvironmentName` | new |
| `InstanceName` | `Environment.MachineName` | unchanged |

These follow the identity attributes that OpenTelemetry defines (`service.name`, `service.version`, `service.instance.id`, `deployment.environment.name`), and what Spring Boot serves at `/actuator/info`. Our services only set some of them on telemetry today: `service.version` is the commit SHA, and the environment name is not set.

### What each field means

- **`ServiceName`:** which service answered.
- **`ReleaseTag`:** the release tag the build supplied. It answers "is my release running on this instance?". It is information the build passes in, not proof that CI produced the image.
- **`CommitShortSha`:** the source, as the 7-character short SHA GitHub shows. It is the only identifier for a build without a tag. `job.build.yml` shortens `github.sha` before passing it as the `GIT_SHA` build arg, and the Dockerfiles copy it into `GIT_SHORT_SHA`.
- **`OpenApiSpecHashes`:** the version of each API contract the instance carries. It maps each committed spec file to the SHA-256 of its exact bytes, for example `openapi.json`, `openapi-internal.json` and `openapi-api-v1.json`. A release that leaves a spec's bytes unchanged keeps its hash. It covers every generated spec, public or internal, whoever reads it.
- **`Environment`:** the ASP.NET environment name the app started with. It catches a pod running with the wrong name, such as a prod pod on `Development`. It is not a full configuration identity: two deployments can both legitimately run as `Production`, and a wrong value inside one environment, such as an Auth0 domain or a connection string, does not show. `/metadata` stops at the name, because reporting configuration values would leak them.
- **`InstanceName`:** the pod that answered. In Kubernetes the hostname is the pod name, for example `user-service-7c9f8d6b5-x2k4p`. It matches OpenTelemetry's `service.instance.id`, so a response can be traced to that pod in Grafana.

### Where the values come from

- **The release tag and the commit.** `github-workflows/.github/workflows/job.build.yml` passes `GIT_REF_NAME` (the image tag) and `GIT_SHA` (the first 7 characters of `github.sha`) as build args. The service Dockerfiles turn them into env vars (`user-service/src/Dockerfile:8-9`).
- **The spec hashes.** See [Spec hashes](#spec-hashes).
- **The service name.** Each service's `appsettings.json`. All 10 consuming services set it.
- **The environment name.** `ASPNETCORE_ENVIRONMENT`. Prod charts don't set it, so prod reports `Production` through ASP.NET's documented fallback.

### Missing values

- **A missing or blank `ServiceName` is a configuration mistake.** The library throws at startup. Services already throw for a missing value when they set up observability (for example `account-service/src/Core/Extensions/ConfigurationExtensions.cs:11`), so no running service is affected.
- **A missing or blank `ReleaseTag` or `CommitShortSha` returns `null`, meaning "build identity unavailable".** Whitespace counts as blank. A local `dotnet run` returns `null`, and so would a misconfigured prod image. So `null` does not mean "local". Any check that relies on these fields, such as a release check, must reject `null`.
- **A missing `openapi-hashes.json` returns `null` for `OpenApiSpecHashes`, meaning "spec versions unavailable".** That covers a service that generates no specs and one whose hash file was never committed. A hash file that exists but can't be read is a mistake, and the library throws at startup.

| Where it runs | `ReleaseTag` | `CommitShortSha` | `OpenApiSpecHashes` | `Environment` |
| --- | --- | --- | --- | --- |
| Image built by CI from a tag | `v2.2.3` | short SHA | the committed hashes | `Production` |
| Local Docker build | `local` | `<local-sha>` | the committed hashes | as set in compose, e.g. `Development` |
| `dotnet run` | `null` | `null` | the hashes from the last Debug build | `Development` from the launch profile |

### Examples

Prod:

```json
{
  "serviceName": "user-service",
  "releaseTag": "v2.2.3",
  "commitShortSha": "8f3c2a1",
  "openApiSpecHashes": {
    "openapi-api-v1.json": "3b1f…",
    "openapi-internal.json": "a94c…",
    "openapi.json": "e07d…"
  },
  "environment": "Production",
  "instanceName": "user-service-7c9f8d6b5-x2k4p"
}
```

`dotnet run`:

```json
{
  "serviceName": "user-service",
  "releaseTag": null,
  "commitShortSha": null,
  "openApiSpecHashes": {
    "openapi-api-v1.json": "3b1f…",
    "openapi-internal.json": "a94c…",
    "openapi.json": "e07d…"
  },
  "environment": "Development",
  "instanceName": "rui-macbook"
}
```

Hashes are shortened here. Real values are 64 lowercase hex characters, the same as `sha256sum` prints.

## Spec hashes

Every .NET service generates its specs in a Debug `PostBuild` step with `dotnet swagger tofile`, and writes them to the repo root: `openapi.json` for the `v1` document, and `openapi-<document>.json` for the others. All 10 consuming services follow that convention, which produces 15 spec files today.

**Generated in development, carried into releases.** The hashes are written when a developer's Debug build generates the specs, and committed with them. A release build never regenerates them from the release binary. It carries the committed file into the image. That is only trustworthy because a drift check in CI regenerates the specs and hashes from a Release build and fails when they differ from what is committed. The public API docs plan adds that check to the services that publish a public API.

**What a hash means.** A hash covers the exact bytes of a spec file. A changed hash means changed bytes, which can be a reworded description, a different ordering or formatting, not necessarily changed API behaviour. An unchanged hash means the spec is identical. Exact-byte hashing is kept because it is simple and anyone can reproduce it with `sha256sum`. Whether a change is breaking is a question for `oasdiff`.

### The build target

The package ships it in `build/`, not `buildTransitive/`. `build/` assets apply only to projects that reference the package directly, which is each service's API project. A test project that references the API project does not get the target, so it never writes its own hash file or copies a duplicate one.

Generation and packaging are separate concerns, because they run at different times:

- **Generation** (`WriteOpenApiSpecHashes`) runs after the service's `PostBuild` target, so only in Debug builds that have just generated the specs. It:
  - hashes every `openapi*.json` in the spec folder with MSBuild's `GetFileHash` task (SHA-256);
  - writes `openapi-hashes.json` into the API project folder from scratch each time, so a removed spec drops out. It is a JSON object mapping each file name to its lowercase hex hash, with keys sorted so the output is stable;
  - copies the new file into the build output itself. By the time `PostBuild` runs, the normal copy to the output folder has already happened, and on a first build the file did not exist when MSBuild evaluated the project.
- **Packaging** is an item, not a target. When `openapi-hashes.json` exists in the API project, it is added to the build and publish output. That covers a clean Release publish, such as the Docker build, where generation never runs and only the committed file is available.

The spec folder defaults to the repo root (`$(ProjectDir)../../`), and a service can override it with an `OpenApiSpecDirectory` property. The target can also be run on its own (`-t:WriteOpenApiSpecHashes`), so a drift check can rehash specs it generated from a Release build.

**Why the API project folder.** The specs live at the repo root, outside the Docker build context (`src/`). The hash file lives inside it, so the image gets the hashes without the specs and without changing the build context.

### Every file present, not every document generated

The target hashes the `openapi*.json` files it finds, not the documents the service generates. Those differ when a spec is left behind after its `swagger tofile` command is removed. The convention is that removing a document also deletes its committed spec file. The drift check enforces it: it deletes every `openapi*.json` before regenerating, so a file no command produces shows up as a deletion and fails the check.

### Committed with the spec

The hash file changes in the same commit as the spec it describes, so each tag carries both. The hash only changes when a spec's bytes change, so builds don't produce noise. In a service PR, a changed hash shows that a spec changed, which gives reviewers a signal to look at it.

### Reading

At startup, the library reads `openapi-hashes.json` from `AppContext.BaseDirectory` and reports it as `OpenApiSpecHashes`.

## Where to read it

Each service has an internal hostname on the internal ALB, for example `https://user-service.gainsway.int/metadata`, reachable over Twingate. `/metadata` is not on the public gateway (`api.gainsway.io`).

## What a response proves

A response describes the one instance that answered. During a rollout, each request reaches one pod, and old and new pods answer side by side. So a matching tag on one response does not prove the rollout finished. A release check pairs `/metadata` with the rollout status, and with a smoke test through the public gateway route, which the internal hostname bypasses.

## Benefits

- **A developer can see whether their release is running.** One request returns the tag, so there is no mapping from commit to tag.
- **Anyone can see whether a release changed a spec.** `/metadata` shows the hashes running now. Git keeps the history: each tag carries its committed `openapi-hashes.json`, so `git diff v2.2.2 v2.2.3 -- src/Api/openapi-hashes.json` shows which specs changed between two releases. A docs pipeline, a client generator or a person can tell a release that left a spec untouched from one that changed it, without fetching or comparing the specs.
- **Release and rollback checks are quick.** Combined with the rollout status, the tag shows which release each instance runs.
- **Mixed versions during a rollout explain themselves.** With `InstanceName` and `ReleaseTag` together, two different answers read as "this pod is still on the old release" rather than a fault.
- **A wrong ASP.NET environment shows up.** A prod pod on `Development`, or a local run on `Production`, shows up in one request.
- **Missing build identity is explicit.** `null` says the identity is unavailable, so it can't be mistaken for a release.
- **One response leads to the pod's telemetry.** The service name and pod match the attributes on the same pod's traces, and the commit matches their `service.version`.
- **It supports the public API docs prod check.** That check compares the `/metadata` tags and spec hashes with the ones listed in the docs PR (`public_api_docs_refresh_plan.md` at the workspace root).
- **Little new infrastructure.** The tag and commit are already in every image. The hashes come from a build target in this package, with no Dockerfile, chart or workflow changes.

## Changes

**In this library**
- `src/MetadataDto.cs`: add `ReleaseTag`, `OpenApiSpecHashes` and `Environment`. `ReleaseTag` and `CommitShortSha` are `string?`. `OpenApiSpecHashes` is `IReadOnlyDictionary<string, string>?`. `ServiceName`, `Environment` and `InstanceName` stay `required string`.
- `src/MetadataExtensions.cs`: read `GIT_REF_NAME`, `app.Environment.EnvironmentName` and `openapi-hashes.json`. Throw at startup when `ServiceName` is missing or blank, or when the hash file exists but can't be read. Return `null` for missing, empty or whitespace build values, and for a missing hash file. Set the summary with `.WithSummary(...)` instead of the obsolete `.WithOpenApi`, and return `TypedResults.Ok` so Swashbuckle documents the response schema.
- `build/Gainsway.MetadataEndpoints.targets`: the generation target and the packaging item, as described in [The build target](#the-build-target).
- `README.md` and `docs/index.md`: document the response, the hash file, and what `null` means.
- `test/`: tests that check:
  - the HTTP response uses `commitShortSha`, `releaseTag` and `openApiSpecHashes`, and writes missing values as explicit `null`, not omitted fields;
  - build values that are missing, empty or whitespace all return `null`;
  - a missing, empty or whitespace `ServiceName` fails at startup;
  - a hash file is reported as written, a missing one gives `null`, and an unreadable one fails at startup;
  - the generated OpenAPI document keeps the summary and shows the nullable fields as nullable.
- **Check the target** in a scratch consumer, an API project with a test project that references it:
  - a first Debug build with no hash file writes one whose values match `sha256sum` of each spec, and the file is in the build output;
  - a second Debug build with no spec change leaves the file unchanged;
  - a clean Release publish, using only the committed hash file, includes it in the publish output;
  - deleting a spec and rebuilding removes its entry;
  - the test project neither writes a hash file nor copies a second one.

**Release**
- A major version, `3.0.0`. `commitShortSha` can now be `null` where it always held a string, and a missing or blank `ServiceName` now fails at startup. No migration is needed: nothing parses the response, and all 10 services set `ServiceName`.
- Bump user-service and OMS first, because the public API docs prod check needs them. The other eight services and `backstage-templates` follow on their next change.
- After the bump, each service's next Debug build writes `openapi-hashes.json` and regenerates its `openapi.json`, which updates the documented `/metadata` response. Both are committed with the bump.

## Not in this change

- **Renaming the `GIT_SHA` build arg.** It now carries the short SHA. Renaming it touches every service Dockerfile, so it keeps its name.
- **The observability version.** `lib-dotnet-observability` sets OpenTelemetry's `service.version` to the commit SHA (`src/ObservabilityExtensions.cs:33`), so traces and `/metadata` name the version differently. Aligning them is a change to that library.
- **Duplicate reading in services.** Services read the same env vars into their own `ApplicationMetadata` record (`src/Core/AppMetadata.cs`), with `""` fallbacks, to feed observability. Moving that onto this library is separate.
- **More configuration values.** Domains, URLs and connection strings stay out, so `/metadata` doesn't leak configuration.
