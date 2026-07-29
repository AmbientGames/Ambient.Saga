using Ambient.Rpg.Engine.Contracts.Persistence;

namespace Ambient.Rpg.Ui.Services;

public class AvatarProgressRepositoryProvider
{
    private IAvatarProgressRepository? _repository;

    public IAvatarProgressRepository Repository
    {
        get
        {
            if (_repository == null)
            {
                throw new InvalidOperationException(
                    "IAvatarProgressRepository not initialized. " +
                    "A world must be loaded before using CQRS commands.");
            }
            return _repository;
        }
    }

    public void SetRepository(IAvatarProgressRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public void ClearRepository()
    {
        _repository = null;
    }
}
