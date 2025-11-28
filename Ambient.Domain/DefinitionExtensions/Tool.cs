using Ambient.Domain.Contracts;

namespace Ambient.Domain;

/// <summary>
/// Represents a tool in the game system with classification and visual properties.
/// </summary>
public partial class Tool : ITradeable
{
    /// <summary>
    /// The classification category for this tool.
    /// </summary>
    public uint Class { get; set; }

    /// <summary>
    /// The identifier for the texture associated with this tool.
    /// </summary>
    public int TextureId { get; set; }
}