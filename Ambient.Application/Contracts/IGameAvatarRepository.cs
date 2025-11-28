namespace Ambient.Application.Contracts;

/// <summary>
/// Repository interface for avatar persistence.
/// Generic interface that works with any avatar type (typically AvatarEntity).
/// </summary>
public interface IGameAvatarRepository
{
    /// <summary>
    /// Loads the player's avatar from database.
    /// Returns null if no avatar exists.
    /// </summary>
    Task<TAvatar?> LoadAvatarAsync<TAvatar>() where TAvatar : class;

    /// <summary>
    /// Saves avatar to database (creates if new, updates if exists).
    /// </summary>
    Task SaveAvatarAsync<TAvatar>(TAvatar avatar) where TAvatar : class;

    /// <summary>
    /// Deletes all avatars from database.
    /// </summary>
    Task DeleteAvatarsAsync();
}
