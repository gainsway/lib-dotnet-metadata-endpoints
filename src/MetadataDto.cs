namespace Gainsway.MetadataEndpoints;

public record MetadataDto
{
    public required string ServiceName { get; init; }
    public required string CommitShortSha { get; init; }
    public required string InstanceName { get; init; }
}
