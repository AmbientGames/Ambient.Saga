namespace Ambient.Domain.Contracts;

/// <summary>
/// Factory interface for creating world instances.
/// Allows game-specific projects to provide their own World implementations.
/// </summary>
public interface IWorldFactory
{
    /// <summary>
    /// Creates a new world instance.
    /// </summary>
    IWorld CreateWorld();
}
