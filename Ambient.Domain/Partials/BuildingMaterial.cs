using Ambient.Domain.Contracts;

namespace Ambient.Domain;

/// <summary>
/// Partial class extension to implement IGameplayItem for BuildingMaterial.
/// BuildingMaterial inherits from StackableAcquirable -> Acquirable, which provides
/// all required ITradeable properties (RefName, DisplayName, WholesalePrice, MerchantMarkupMultiplier).
/// Also provides Description and TextureRef from EntityBase.
/// </summary>
public partial class BuildingMaterial : IGameplayItem
{
    /// <summary>
    /// Category for IGameplayItem - defaults to "BuildingMaterial" but can be set for subcategories.
    /// </summary>
    public string Category { get; set; } = "BuildingMaterial";
}
