namespace Ambient.Saga.UI.ViewModels;

/// <summary>
/// Feature type for Saga center dots on the map.
/// Determines the color of the feature dot.
/// </summary>
public enum FeatureType
{
    /// <summary>Landmark/Lore marker (orange-red dot)</summary>
    Landmark,

    /// <summary>Structure with loot (blue dot)</summary>
    Structure,

    /// <summary>Quest signpost (green dot)</summary>
    QuestSignpost,

    /// <summary>Resource node for gathering (yellow dot)</summary>
    ResourceNode,

    /// <summary>Teleporter/Fast travel point (purple dot)</summary>
    Teleporter,

    /// <summary>Vendor/Merchant (gold dot)</summary>
    Vendor,

    /// <summary>Crafting station (cyan dot)</summary>
    CraftingStation
}
