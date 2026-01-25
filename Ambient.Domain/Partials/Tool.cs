using Ambient.Domain.Contracts;

namespace Ambient.Domain;

/// <summary>
/// Represents a tool in the game system with classification and visual properties.
/// Implements IGameplayItem for integration with the gameplay item provider system.
/// </summary>
public partial class Tool : IGameplayItem
{
    /// <summary>
    /// The classification category for this tool.
    /// </summary>
    public uint Class { get; set; }

    /// <summary>
    /// The identifier for the texture associated with this tool.
    /// </summary>
    public int TextureId { get; set; }

    /// <summary>
    /// Category for IGameplayItem - defaults to "Tool" but can be set for subcategories.
    /// </summary>
    public string Category { get; set; } = "Tool";
}