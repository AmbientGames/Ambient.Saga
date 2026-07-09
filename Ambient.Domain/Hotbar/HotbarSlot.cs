namespace Ambient.Domain.Hotbar;

/// <summary>
/// Represents a single slot in the hotbar that can hold any item type.
/// </summary>
public class HotbarSlot
{
    /// <summary>
    /// The type of item in this slot.
    /// </summary>
    public HotbarItemType ItemType { get; set; } = HotbarItemType.Empty;

    /// <summary>
    /// The RefName of the item in this slot (null if empty). For block slots this is the
    /// full stack identity, with any variation folded into the ref.
    /// </summary>
    public string? RefName { get; set; }

    /// <summary>
    /// Creates an empty hotbar slot.
    /// </summary>
    public HotbarSlot()
    {
    }

    /// <summary>
    /// Creates a hotbar slot with the specified item.
    /// </summary>
    public HotbarSlot(HotbarItemType itemType, string refName)
    {
        ItemType = itemType;
        RefName = refName;
    }

    /// <summary>
    /// Clears this slot.
    /// </summary>
    public void Clear()
    {
        ItemType = HotbarItemType.Empty;
        RefName = null;
    }

    /// <summary>
    /// Sets this slot to the specified item.
    /// </summary>
    public void Set(HotbarItemType itemType, string refName)
    {
        ItemType = itemType;
        RefName = refName;
    }

    /// <summary>
    /// Returns true if this slot is empty.
    /// </summary>
    public bool IsEmpty => ItemType == HotbarItemType.Empty || string.IsNullOrEmpty(RefName);
}
