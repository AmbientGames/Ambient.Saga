using Ambient.Application.Contracts;
using LiteDB;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// LiteDB implementation of IPlayerDiscoveryRepository.
/// </summary>
internal class PlayerDiscoveryRepository : IPlayerDiscoveryRepository
{
    private readonly ILiteDatabase _database;
    private const string CollectionName = "PlayerDiscoveries";

    public PlayerDiscoveryRepository(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<TDiscovery?> FindOneAsync<TDiscovery>(string avatarId, string entityType, string entityRef)
        where TDiscovery : class
    {
        var collection = _database.GetCollection<TDiscovery>(CollectionName);

        // Use dynamic query since we don't know the exact type at compile time
        var results = collection.Find(Query.And(
            Query.EQ("AvatarId", avatarId),
            Query.EQ("EntityType", entityType),
            Query.EQ("EntityRef", entityRef)
        ));

        var result = results.FirstOrDefault();
        return Task.FromResult(result);
    }

    public Task InsertAsync<TDiscovery>(TDiscovery discovery) where TDiscovery : class
    {
        var collection = _database.GetCollection<TDiscovery>(CollectionName);
        collection.Insert(discovery);
        return Task.CompletedTask;
    }

    public Task UpdateAsync<TDiscovery>(TDiscovery discovery) where TDiscovery : class
    {
        var collection = _database.GetCollection<TDiscovery>(CollectionName);
        collection.Update(discovery);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string avatarId, string entityType, string entityRef)
    {
        var collection = _database.GetCollection<BsonDocument>(CollectionName);

        var exists = collection.Exists(Query.And(
            Query.EQ("AvatarId", avatarId),
            Query.EQ("EntityType", entityType),
            Query.EQ("EntityRef", entityRef)
        ));

        return Task.FromResult(exists);
    }

    public Task<List<TDiscovery>> GetByAvatarIdAsync<TDiscovery>(string avatarId) where TDiscovery : class
    {
        var collection = _database.GetCollection<TDiscovery>(CollectionName);
        var results = collection.Find(Query.EQ("AvatarId", avatarId)).ToList();
        return Task.FromResult(results);
    }
}
