namespace Ambient.Saga.Engine.Application.Results.Saga;

/// <summary>
/// DTO representing a Saga discovery event.
/// </summary>
public record SagaDiscoveryDto
{
    public required string SagaRef { get; init; }
    public required string DisplayName { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double Y { get; init; }
    public DateTime DiscoveredAt { get; init; }
    public bool IsFirstDiscovery { get; init; }
    public int TotalTriggers { get; init; }
    public int CompletedTriggers { get; init; }
}
