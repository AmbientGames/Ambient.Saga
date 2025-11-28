namespace Ambient.Domain.Enums;

/// <summary>
/// Defines the type of update for a block extension.
/// </summary>
public enum BlockExtensionUpdateType : byte
{
    /// <summary>
    /// A standard update.
    /// </summary>
    Update = 0,
    
    /// <summary>
    /// The block extension is being processed.
    /// </summary>
    Processing = 1,
    
    /// <summary>
    /// The block extension is being initialized by the server.
    /// </summary>
    ServerInitialization = 2
}