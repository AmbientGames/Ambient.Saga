using Ambient.Saga.Engine.Contracts;

namespace Ambient.Saga.Presentation.UI.Services;

/// <summary>
/// Provides access to the WorldStateRepository instance for the currently loaded world.
/// Repository is null until MainViewModel loads a world and calls SetRepository().
/// </summary>
public class WorldStateRepositoryProvider
{
    private IWorldStateRepository? _repository;

    /// <summary>
    /// Gets the current WorldStateRepository (null if no world loaded).
    /// </summary>
    public IWorldStateRepository? Repository => _repository;

    /// <summary>
    /// Sets the WorldStateRepository instance (called by MainViewModel after world loads).
    /// </summary>
    public void SetRepository(IWorldStateRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Clears the repository (called when world is unloaded).
    /// </summary>
    public void ClearRepository()
    {
        _repository = null;
    }
}
