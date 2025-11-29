namespace Ambient.Saga.Engine.Application.Results.Saga;

/// <summary>
/// DTO representing a character spawn event.
/// </summary>
public record CharacterSpawnedDto
{
    public required Guid CharacterInstanceId { get; init; }
    public required string CharacterRef { get; init; }
    public required string DisplayName { get; init; }
    public required string CharacterType { get; init; } // Boss, Merchant, Quest, Encounter
    public required double SpawnX { get; init; }
    public required double SpawnZ { get; init; }
    public required double SpawnY { get; init; }
    public DateTime SpawnedAt { get; init; }
    public required string SpawnedByTriggerRef { get; init; }
}
