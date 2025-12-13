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
    private readonly IBlockProvider? _blockProvider;

    /// <summary>
    /// Creates a WorldProvider without a block provider (blocks disabled).
    /// </summary>
    public WorldProvider()
    {
    }

    /// <summary>
    /// Creates a WorldProvider with a block provider (blocks enabled).
    /// </summary>
    public WorldProvider(IBlockProvider blockProvider)
    {
        _blockProvider = blockProvider;
    }

    /// <summary>
    /// Gets the current World instance.
    /// Returns null if not yet initialized (world not loaded).
    /// Handlers should check for null and handle appropriately.
    /// </summary>
    public IWorld World => _world;

    /// <summary>
    /// Sets the World instance (called by MainViewModel when world loads).
    /// Automatically injects the BlockProvider if one was configured.
    /// </summary>
    public void SetWorld(IWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        // Inject block provider if configured
        if (_blockProvider != null)
        {
            _world.BlockProvider = _blockProvider;
        }
    }

    /// <summary>
    /// Clears the World instance (called when world is unloaded).
    /// </summary>
    public void ClearWorld()
    {
        _world = null;
    }
}
