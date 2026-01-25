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
    private readonly IEnumerable<IGameplayItemProvider> _gameplayItemProviders;

    /// <summary>
    /// Creates a WorldProvider without providers (gameplay items disabled).
    /// </summary>
    public WorldProvider()
    {
        _gameplayItemProviders = Array.Empty<IGameplayItemProvider>();
    }

    /// <summary>
    /// Creates a WorldProvider with a block provider (legacy pattern).
    /// </summary>
    [Obsolete("Use constructor with IGameplayItemProviders for new code.")]
    public WorldProvider(IBlockProvider blockProvider)
    {
        _blockProvider = blockProvider;
        _gameplayItemProviders = Array.Empty<IGameplayItemProvider>();
    }

    /// <summary>
    /// Creates a WorldProvider with legacy BlockProvider and multiple GameplayItemProviders.
    /// </summary>
    public WorldProvider(IBlockProvider? blockProvider, IEnumerable<IGameplayItemProvider> gameplayItemProviders)
    {
        _blockProvider = blockProvider;
        _gameplayItemProviders = gameplayItemProviders;
    }

    /// <summary>
    /// Gets the current World instance.
    /// Returns null if not yet initialized (world not loaded).
    /// Handlers should check for null and handle appropriately.
    /// </summary>
    public IWorld World => _world;

    /// <summary>
    /// Sets the World instance (called by MainViewModel when world loads).
    /// Automatically injects the providers if configured.
    /// </summary>
    public void SetWorld(IWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        // Inject providers if configured
        if (_blockProvider != null)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _world.BlockProvider = _blockProvider;
#pragma warning restore CS0618
        }

        foreach (var provider in _gameplayItemProviders)
        {
            _world.GameplayItemProviders.Add(provider);
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
