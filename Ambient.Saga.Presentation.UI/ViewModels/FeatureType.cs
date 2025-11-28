namespace Ambient.Saga.Presentation.UI.ViewModels;

/// <summary>
/// Feature type for Saga center dots on the map.
/// Determines the color of the feature dot.
/// </summary>
public enum FeatureType
{
    /// <summary>Landmark/Lore marker (purple dot)</summary>
    Landmark,

    /// <summary>Structure with loot (blue dot)</summary>
    Structure,

    /// <summary>Quest signpost (cyan dot)</summary>
    QuestSignpost
}
