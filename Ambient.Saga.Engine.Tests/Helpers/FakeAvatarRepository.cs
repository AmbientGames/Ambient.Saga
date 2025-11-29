using Ambient.Application.Contracts;

namespace Ambient.Saga.Engine.Tests.Helpers;

/// <summary>
/// Fake in-memory avatar repository for testing.
/// Stores avatars in memory to enable testing of avatar state changes.
/// Implements the generic IGameAvatarRepository interface.
/// </summary>
public class FakeAvatarRepository : IGameAvatarRepository
{
    private readonly Dictionary<Type, object> _avatars = new();

    public Task<TAvatar?> LoadAvatarAsync<TAvatar>() where TAvatar : class
    {
        if (_avatars.TryGetValue(typeof(TAvatar), out var avatar))
        {
            return Task.FromResult(avatar as TAvatar);
        }
        return Task.FromResult<TAvatar?>(null);
    }

    public Task SaveAvatarAsync<TAvatar>(TAvatar avatar) where TAvatar : class
    {
        _avatars[typeof(TAvatar)] = avatar;
        return Task.CompletedTask;
    }

    public Task DeleteAvatarsAsync()
    {
        _avatars.Clear();
        return Task.CompletedTask;
    }
}
