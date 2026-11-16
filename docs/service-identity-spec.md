# `/metadata`: the service identity

`/metadata` tells you which build an instance reports running, and which ASP.NET environment it started with. Every field is either a real value or an explicit `null`, never an empty string that looks like a real value.

## Why it exists

- **Which release is running.** A commit SHA alone means looking up which commit a tag points to. `releaseTag` answers directly.
- **A missing value must not look like a real one.** A blank string reads like a real but empty value, so missing build values are `null` and a missing `ServiceName` fails at startup.
- **A wrong environment name is otherwise invisible.** A prod pod started with `ASPNETCORE_ENVIRONMENT=Development` loads `appsettings.Development.json` and turns on development-only behaviour.

## The response

| Field | Source | When missing |
| --- | --- | --- |
| `ServiceName` | `ServiceName` in `appsettings.json` | fails at startup |
| `ReleaseTag` | `GIT_REF_NAME` env var | `null` |
| `CommitShortSha` | `GIT_SHORT_SHA` env var | `null` |
| `Environment` | `app.Environment.EnvironmentName` | always set |
| `InstanceName` | `Environment.MachineName` | always set |

These follow the identity attributes that OpenTelemetry defines (`service.name`, `service.version`, `service.instance.id`, `deployment.environment.name`), and what Spring Boot serves at `/actuator/info`. Our services only set some of them on telemetry: `service.version` is the commit SHA, and the environment name is not set.

### What each field means

- **`ServiceName`:** which service answered.
- **`ReleaseTag`:** the release tag the build supplied. It answers "is my release running on this instance?". It is information the build passes in, not proof that CI produced the image.
- **`CommitShortSha`:** the source, as the 7-character short SHA GitHub shows. It is the only identifier for a build without a tag. `job.build.yml` shortens `github.sha` before passing it as the `GIT_SHA` build arg, and the Dockerfiles copy it into `GIT_SHORT_SHA`.
- **`Environment`:** the ASP.NET environment name the app started with. It catches a pod running with the wrong name, such as a prod pod on `Development`. It is not a full configuration identity: two deployments can both legitimately run as `Production`, and a wrong value inside one environment, such as an Auth0 domain or a connection string, does not show. `/metadata` stops at the name, because reporting configuration values would leak them.
- **`InstanceName`:** the pod that answered. In Kubernetes the hostname is the pod name, for example `user-service-7c9f8d6b5-x2k4p`. It matches OpenTelemetry's `service.instance.id`, so a response can be traced to that pod in Grafana.

### Where the values come from

- **The release tag and the commit.** `github-workflows/.github/workflows/job.build.yml` passes `GIT_REF_NAME` (the image tag) and `GIT_SHA` (the first 7 characters of `github.sha`) as build args. The service Dockerfiles turn them into env vars (`user-service/src/Dockerfile:8-9`).
- **The service name.** Each service's `appsettings.json`. All 10 services set it.
- **The environment name.** `ASPNETCORE_ENVIRONMENT`. Prod charts don't set it, so prod reports `Production` through ASP.NET's documented fallback.

### Missing values

- **A missing or blank `ServiceName` is a configuration mistake.** The library throws at startup.
- **A missing or blank `ReleaseTag` or `CommitShortSha` returns `null`, meaning "build identity unavailable".** Whitespace counts as blank. A local `dotnet run` returns `null`, and so would a misconfigured prod image. So `null` does not mean "local". Any check that relies on these fields, such as a release check, must reject `null`.

| Where it runs | `ReleaseTag` | `CommitShortSha` | `Environment` |
| --- | --- | --- | --- |
| Image built by CI from a tag | `v2.2.3` | short SHA | `Production` |
| Local Docker build | `local` | `<local-sha>` | as set in compose, e.g. `Development` |
| `dotnet run` | `null` | `null` | `Development` from the launch profile |

### Examples

Prod:

```json
{
  "serviceName": "user-service",
  "releaseTag": "v2.2.3",
  "commitShortSha": "8f3c2a1",
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
  "environment": "Development",
  "instanceName": "rui-macbook"
}
```

## Spec hashes

`/metadata` doesn't report OpenAPI spec hashes, and services don't commit a hash file. `ReleaseTag` already names the release, and the spec at that tag is the contract it carries. The public API docs in app-liquid-monorepo keep the hash of each published spec in their manifest, and hash the spec at a release tag when they run. A committed hash file was a frequent merge conflict that only repeated what the spec already says.

## Where to read it

Each service has an internal hostname on the internal ALB, for example `https://user-service.gainsway.int/metadata`, reachable over Twingate. `/metadata` is not on the public gateway (`api.gainsway.io`).

## What a response proves

A response describes the one instance that answered. During a rollout, each request reaches one pod, and old and new pods answer side by side. So a matching tag on one response does not prove the rollout finished. A release check pairs `/metadata` with the rollout status, and with a smoke test through the public gateway route, which the internal hostname bypasses.

## Versions

- **`3.0.0`** added `releaseTag`, `environment` and `openApiSpecHashes`, made `commitShortSha` nullable, and made a missing `ServiceName` fail at startup.
- **`3.0.1`** removed `openApiSpecHashes` and the MSBuild target that wrote `openapi-hashes.json`. Nothing read the field. A service moving to `3.0.1` deletes its committed `src/Api/openapi-hashes.json`.

## Limits

- **The `GIT_SHA` build arg carries the short SHA.** Renaming it touches every service Dockerfile, so it keeps its name.
- **The observability version.** `lib-dotnet-observability` sets OpenTelemetry's `service.version` to the commit SHA (`src/ObservabilityExtensions.cs:33`), so traces and `/metadata` name the version differently. Aligning them is a change to that library.
- **Duplicate reading in services.** Services read the same env vars into their own `ApplicationMetadata` record (`src/Core/AppMetadata.cs`), with `""` fallbacks, to feed observability.
- **No configuration values.** Domains, URLs and connection strings stay out on purpose, so `/metadata` doesn't leak configuration.
