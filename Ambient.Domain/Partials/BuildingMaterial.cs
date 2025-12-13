using Ambient.Domain.Contracts;

namespace Ambient.Domain;

/// <summary>
/// Partial class extension to implement ITradeable for BuildingMaterial.
/// BuildingMaterial inherits from StackableAcquirable -> Acquirable, which provides
/// all required ITradeable properties (RefName, DisplayName, WholesalePrice, MerchantMarkupMultiplier).
/// </summary>
public partial class BuildingMaterial : ITradeable
{
}
