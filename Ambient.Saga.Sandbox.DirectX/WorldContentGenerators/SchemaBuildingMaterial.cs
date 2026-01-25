using Ambient.Domain.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Schema-driven building material implementation using the IGameplayItem pattern.
/// </summary>
public class SchemaBuildingMaterial : IGameplayItem
{
    public string RefName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string? Description { get; }
    public string? TextureRef { get; }
    public int WholesalePrice { get; }
    public float MerchantMarkupMultiplier { get; }

    public SchemaBuildingMaterial(
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
