using Ambient.Application.Contracts;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Provides access to the current IGameAvatarRepository.
/// This is a mutable singleton that gets updated when a world is loaded.
/// Used to bridge the gap between DI registration at startup and per-world database initialization.
/// </summary>
public class GameAvatarRepositoryProvider
{
    private IGameAvatarRepository? _repository;

    /// <summary>
    /// Gets the current repository instance.
    /// Throws if not yet initialized (world not loaded).
    /// </summary>
    public IGameAvatarRepository Repository
    {
        get
        {
            if (_repository == null)
            {
                throw new InvalidOperationException(
                    "IGameAvatarRepository not initialized. " +
                    "A world must be loaded before using avatar operations. " +
                    "This is set by MainViewModel.InitializeWorldDatabase().");
            }
            return _repository;
        }
    }

    /// <summary>
    /// Sets the repository instance (called by MainViewModel when world loads).
    /// </summary>
    public void SetRepository(IGameAvatarRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Clears the repository instance (called when world is unloaded).
    /// </summary>
    public void ClearRepository()
    {
        _repository = null;
    }
}
