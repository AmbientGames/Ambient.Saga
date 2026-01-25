using Ambient.Domain.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Schema-driven block implementation using the new IGameplayItem pattern.
/// Demonstrates how games can provide their own item types through the generic interface.
/// </summary>
public class SchemaBlock : IGameplayItem
{
    public string RefName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string? Description { get; }
    public string? TextureRef { get; }
    public int WholesalePrice { get; }
    public float MerchantMarkupMultiplier { get; }

    public SchemaBlock(
        string refName,
        string displayName,
        string category,
        string? description,
        int wholesalePrice,
        float merchantMarkupMultiplier,
        string? textureRef = null)
    {
        RefName = refName;
        DisplayName = displayName;
        Category = category;
        Description = description;
        WholesalePrice = wholesalePrice;
        MerchantMarkupMultiplier = merchantMarkupMultiplier;
        TextureRef = textureRef ?? refName;
    }
}
