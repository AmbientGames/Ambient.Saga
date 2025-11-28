namespace Ambient.SagaEngine.Application.Results.Saga;

/// <summary>
/// DTO representing loot awarded to a player.
/// </summary>
public record LootAwardedDto
{
    public required Guid CharacterInstanceId { get; init; }
    public required string CharacterRef { get; init; }
    public List<ItemAwardedDto> Items { get; init; } = new();
    public int CurrencyAwarded { get; init; }
    public List<string> QuestTokensAwarded { get; init; } = new();
}

/// <summary>
/// DTO representing a single item awarded.
/// </summary>
public record ItemAwardedDto
{
    public required string ItemRef { get; init; }
    public required string DisplayName { get; init; }
    public required int Quantity { get; init; }
    public required string ItemType { get; init; } // Consumable, Equipment, Tool, etc.
}
