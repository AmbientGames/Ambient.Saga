namespace Ambient.Domain.Hotbar;

/// <summary>
/// Represents the type of item stored in a hotbar slot.
/// </summary>
public enum HotbarItemType
{
    /// <summary>Empty slot</summary>
    Empty = 0,
    /// <summary>A tool (sets CurrentToolRef when activated)</summary>
    Tool,
    /// <summary>A block (sets CurrentBlockRef when activated)</summary>
    Block,
    /// <summary>A building material (sets CurrentBuildingMaterialRef when activated)</summary>
    BuildingMaterial,
    /// <summary>A consumable item (uses when activated)</summary>
    Consumable,
    /// <summary>Equipment (equips when activated)</summary>
    Equipment
}
