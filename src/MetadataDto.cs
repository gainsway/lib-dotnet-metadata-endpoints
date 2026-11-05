namespace Gainsway.MetadataEndpoints;

public record MetadataDto
{
    public required string ServiceName { get; init; }

    /// <summary>
    /// The release tag the build supplied, for example <c>v2.2.3</c>.
    /// <c>null</c> when the build identity is unavailable.
    /// </summary>
    public required string? ReleaseTag { get; init; }

    /// <summary>
    /// The full commit SHA the image was built from.
    /// <c>null</c> when the build identity is unavailable.
    /// </summary>
    public required string? CommitSha { get; init; }

    /// <summary>
    /// The ASP.NET environment name the app started with, not a full configuration identity.
    /// </summary>
    public required string Environment { get; init; }

    /// <summary>
    /// The host that answered. In Kubernetes, this is the pod name.
    /// </summary>
    public required string InstanceName { get; init; }
}
