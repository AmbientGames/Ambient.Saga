namespace Ambient.Domain.Hotbar;

/// <summary>
/// Represents the type of item stored in a hotbar slot.
/// </summary>
public enum HotbarItemType
{
    /// <summary>Empty slot</summary>
    Empty = 0,
    /// <summary>A tool (sets CurrentToolRef when activated)</summary>
    Tool = 1,
    /// <summary>A block (sets CurrentBlockRef when activated)</summary>
    Block = 2,
    // BuildingMaterial (3) removed - materials auto-select based on substance compatibility
    /// <summary>A consumable item (uses when activated)</summary>
    Consumable = 4,
    /// <summary>Equipment (equips when activated)</summary>
    Equipment = 5
}
