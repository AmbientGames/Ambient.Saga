using Ambient.Domain.Contracts;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Provides access to the current World instance.
/// This is a mutable singleton that gets updated when a world is loaded.
/// Used to bridge the gap between DI registration at startup and per-world loading.
/// </summary>
public class WorldProvider
{
    private IWorld _world;

    /// <summary>
    /// Gets the current World instance.
    /// Returns null if not yet initialized (world not loaded).
    /// Handlers should check for null and handle appropriately.
    /// </summary>
    public IWorld World => _world;

    /// <summary>
    /// Sets the World instance (called by MainViewModel when world loads).
    /// </summary>
    public void SetWorld(IWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Clears the World instance (called when world is unloaded).
    /// </summary>
    public void ClearWorld()
    {
        _world = null;
    }
}
