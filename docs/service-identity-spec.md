# Spec: `/metadata` reports the service identity

Date: 2026-11-05

## Summary

`/metadata` tells you which build an instance reports running, and which ASP.NET environment it started with. Today it returns the service name, the commit and the pod. This change adds the release tag and the environment name, and makes every field either a real value or an explicit `null`. Today some fields fall back to an empty string, which looks like a real value.

The change is in this library only. Every value it needs is already inside each service's image or configuration, so no build, chart or Dockerfile changes.

## The problem

- **You can't tell from a running instance which release it is.** `/metadata` returns a commit SHA. To know whether `v2.2.3` is running, someone has to look up which commit that tag points to.
- **A missing value looks like a real one.** Under `dotnet run`, `CommitShortSha` returns `""`, and a missing `ServiceName` would also return `""`. A blank string reads like a real but empty value.
- **A wrong environment name is invisible.** A prod pod started with `ASPNETCORE_ENVIRONMENT=Development` loads `appsettings.Development.json` and turns on development-only behaviour. Nothing outside the pod shows it.

## The response

| Field | Source | Change |
| --- | --- | --- |
| `ServiceName` | `ServiceName` in `appsettings.json` | fails at startup when missing or blank, instead of `""` |
| `ReleaseTag` | `GIT_REF_NAME` env var | new |
| `CommitSha` | `GIT_SHORT_SHA` env var | renamed from `CommitShortSha`; `null` when missing or blank, instead of `""` |
| `Environment` | `app.Environment.EnvironmentName` | new |
| `InstanceName` | `Environment.MachineName` | unchanged |

These follow the identity attributes that OpenTelemetry defines (`service.name`, `service.version`, `service.instance.id`, `deployment.environment.name`), and what Spring Boot serves at `/actuator/info`. Our services only set some of them on telemetry today: `service.version` is the commit SHA, and the environment name is not set.

### What each field means

- **`ServiceName`:** which service answered.
- **`ReleaseTag`:** the release tag the build supplied. It answers "is my release running on this instance?". It is information the build passes in, not proof that CI produced the image.
- **`CommitSha`:** the exact source, as the full 40-character SHA. It is the only identifier for a build without a tag. Today the field is called `CommitShortSha`, but it already holds the full SHA, because `job.build.yml` passes `github.sha` unchanged and the Dockerfiles keep it as it is. The full value is what you verify against, so the field is renamed to match it rather than shortened.
- **`Environment`:** the ASP.NET environment name the app started with. It catches a pod running with the wrong name, such as a prod pod on `Development`. It is not a full configuration identity: two deployments can both legitimately run as `Production`, and a wrong value inside one environment, such as an Auth0 domain or a connection string, does not show. `/metadata` stops at the name, because reporting configuration values would leak them.
- **`InstanceName`:** the pod that answered. In Kubernetes the hostname is the pod name, for example `user-service-7c9f8d6b5-x2k4p`. It matches OpenTelemetry's `service.instance.id`, so a response can be traced to that pod in Grafana.

### Where the values come from

- **The release tag and the commit.** `github-workflows/.github/workflows/job.build.yml` passes `GIT_REF_NAME` (the image tag) and `GIT_SHA` (`github.sha`) as build args. The service Dockerfiles turn them into env vars (`user-service/src/Dockerfile:8-9`).
- **The service name.** Each service's `appsettings.json`. All 10 consuming services set it.
- **The environment name.** `ASPNETCORE_ENVIRONMENT`. Prod charts don't set it, so prod reports `Production` through ASP.NET's documented fallback.

### Missing values

- **A missing or blank `ServiceName` is a configuration mistake.** The library throws at startup. Services already throw for a missing value when they set up observability (for example `account-service/src/Core/Extensions/ConfigurationExtensions.cs:11`), so no running service is affected.
- **A missing or blank `ReleaseTag` or `CommitSha` returns `null`, meaning "build identity unavailable".** Whitespace counts as blank. A local `dotnet run` returns `null`, and so would a misconfigured prod image. So `null` does not mean "local". Any check that relies on these fields, such as a release check, must reject `null`.

| Where it runs | `ReleaseTag` | `CommitSha` | `Environment` |
| --- | --- | --- | --- |
| Image built by CI from a tag | `v2.2.3` | full SHA | `Production` |
| Local Docker build | `local` | `<local-sha>` | as set in compose, e.g. `Development` |
| `dotnet run` | `null` | `null` | `Development` from the launch profile |

### Examples

Prod:

```json
{
  "serviceName": "user-service",
  "releaseTag": "v2.2.3",
  "commitSha": "8f3c2a1d9b7e4f60a5c3e2d1b0a9f8e7d6c5b4a3",
  "environment": "Production",
  "instanceName": "user-service-7c9f8d6b5-x2k4p"
}
```

`dotnet run`:

```json
{
  "serviceName": "user-service",
  "releaseTag": null,
  "commitSha": null,
  "environment": "Development",
  "instanceName": "rui-macbook"
}
```

## What a response proves

A response describes the one instance that answered. During a rollout, each request reaches one pod, and old and new pods answer side by side. So a matching tag on one response does not prove the rollout finished. A release check pairs `/metadata` with the rollout status, and with a smoke test through the real gateway route, which port-forwarding bypasses.

`/metadata` also does not identify the API contract. The tag tells you where to find the spec committed for that release, but not whether that file matches the running code.

## Benefits

- **A developer can see whether their release is running.** One request returns the tag, so there is no mapping from commit to tag.
- **Release and rollback checks are quick.** Combined with the rollout status, the tag shows which release each instance runs.
- **Mixed versions during a rollout explain themselves.** With `InstanceName` and `ReleaseTag` together, two different answers read as "this pod is still on the old release" rather than a fault.
- **A wrong ASP.NET environment shows up.** A prod pod on `Development`, or a local run on `Production`, shows up in one request.
- **Missing build identity is explicit.** `null` says the identity is unavailable, so it can't be mistaken for a release.
- **One response leads to the pod's telemetry.** Service, release, commit, environment and pod in one response. The service name and pod match the attributes on the same pod's traces, and the commit matches their `service.version`.
- **It supports the public API docs prod check.** That check compares the `/metadata` tags with the tags listed in the docs PR (`public_api_docs_refresh_plan.md` at the workspace root).
- **No new infrastructure.** The values already exist in every image and config. This is a library change and a version bump.

## Changes

**In this library**
- `src/MetadataDto.cs`: add `ReleaseTag` and `Environment`, and rename `CommitShortSha` to `CommitSha`. `ReleaseTag` and `CommitSha` are `string?`. `ServiceName`, `Environment` and `InstanceName` stay `required string`.
- `src/MetadataExtensions.cs`: read `GIT_REF_NAME` and `app.Environment.EnvironmentName`. Throw at startup when `ServiceName` is missing or blank. Return `null` for missing, empty or whitespace build values. Update the OpenAPI summary to list the new fields. The summary is set through `.WithOpenApi`, which is obsolete in .NET 10, and the code already notes it needs replacing. Since this line changes anyway, set the summary with `.WithSummary(...)`, and check that the summary still appears in a consumer's generated `openapi.json`, which Swashbuckle 10 produces. If it doesn't, use the `IOperationFilter` approach from `lib-dotnet-liquid-api-auth` that the code comment suggests.
- `README.md` and `docs/index.md`: list `GIT_REF_NAME` next to `GIT_SHORT_SHA`, say that `CommitSha` holds the full SHA, and document what `null` means.
- `test/`: the project has no tests yet. Add tests that check:
  - the HTTP response uses `commitSha` and `releaseTag`, and writes missing build values as explicit `null`, not omitted fields;
  - build values that are missing, empty or whitespace all return `null`;
  - a missing, empty or whitespace `ServiceName` fails at startup.
- **Check in a consumer:** after the bump, the generated `openapi.json` keeps the summary, and shows `commitSha` and `releaseTag` as nullable.

**Release**
- A major version, `3.0.0`. Renaming `CommitShortSha` to `CommitSha` removes a public property and changes a JSON field name, which breaks the contract even though no consumer reads it today. Failing at startup on a blank `ServiceName` is also new. No migration is needed: nothing reads the old field, and all 10 services set `ServiceName`.
- Bump user-service and OMS first, because the public API docs prod check needs them. The other eight services and `backstage-templates` follow on their next change.
- Each consumer regenerates its `openapi.json` on a Debug build, so the bump also updates the documented `/metadata` response.

## Not in this change

- **Renaming the `GIT_SHORT_SHA` env var.** It also holds the full SHA. Renaming it touches every service Dockerfile, `lib-dotnet-observability` and each service's `ApplicationMetadata`, so the library keeps reading `GIT_SHORT_SHA` for now.
- **The observability version.** `lib-dotnet-observability` sets OpenTelemetry's `service.version` to the commit SHA (`src/ObservabilityExtensions.cs:33`), so traces and `/metadata` name the version differently. Aligning them is a change to that library.
- **Duplicate reading in services.** Services read the same env vars into their own `ApplicationMetadata` record (`src/Core/AppMetadata.cs`), with `""` fallbacks, to feed observability. Moving that onto this library is separate.
- **A route to `/metadata`.** It is not exposed on `api.gainsway.io` or `api.gainsway.int`, so today it needs `kubectl port-forward`. A private route on `api.gainsway.int` would make it a single request.
- **More configuration values.** Domains, URLs and connection strings stay out, so `/metadata` doesn't leak configuration.
