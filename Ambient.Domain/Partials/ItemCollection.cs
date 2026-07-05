namespace Ambient.Domain;

/// <summary>
/// Extensions for ItemCollection (generated from XSD).
/// </summary>
public partial class ItemCollection
{
    /// <summary>
    /// True when the collection contains at least one takeable entry
    /// (positive-quantity stackables or any degradable entry).
    /// Used by the victory-loot flow to decide whether a defeated character
    /// has anything left to collect.
    /// </summary>
    public bool HasAnyItems()
    {
        if (Equipment != null && Equipment.Length > 0) return true;
        if (Tools != null && Tools.Length > 0) return true;
        if (Spells != null && Spells.Length > 0) return true;
        if (Consumables != null && Consumables.Any(c => c.Quantity > 0)) return true;
        if (BuildingMaterials != null && BuildingMaterials.Any(m => m.Quantity > 0)) return true;
        if (Blocks != null && Blocks.Any(b => b.Quantity >= 1)) return true;
        return false;
    }
}
