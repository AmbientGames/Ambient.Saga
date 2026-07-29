namespace Ambient.Rpg.Engine.Application.Results.Arcs;

/// <summary>
/// DTO representing an arc discovery event.
/// </summary>
public record ArcDiscoveryDto
{
    public required string ArcRef { get; init; }
    public required string DisplayName { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double Y { get; init; }
    public DateTime DiscoveredAt { get; init; }
    public bool IsFirstDiscovery { get; init; }
    public int TotalTriggers { get; init; }
    public int CompletedTriggers { get; init; }
}
