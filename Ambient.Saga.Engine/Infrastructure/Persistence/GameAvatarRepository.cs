using Ambient.Application.Contracts;
using LiteDB;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// LiteDB implementation of IGameAvatarRepository.
/// </summary>
internal class GameAvatarRepository : IGameAvatarRepository
{
    private readonly ILiteDatabase _database;
    private const string CollectionName = "PlayerAvatar";

    public GameAvatarRepository(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<TAvatar?> LoadAvatarAsync<TAvatar>() where TAvatar : class
    {
        var collection = _database.GetCollection<TAvatar>(CollectionName);
        var result = collection.FindAll().FirstOrDefault();
        return Task.FromResult(result);
    }

    public Task SaveAvatarAsync<TAvatar>(TAvatar gameAvatar) where TAvatar : class
    {
        var collection = _database.GetCollection<TAvatar>(CollectionName);
        collection.Upsert(gameAvatar);
        return Task.CompletedTask;
    }

    public Task DeleteAvatarsAsync()
    {
        var collection = _database.GetCollection<BsonDocument>(CollectionName);
        collection.DeleteAll();
        return Task.CompletedTask;
    }
}
