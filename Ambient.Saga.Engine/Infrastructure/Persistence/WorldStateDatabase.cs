using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using LiteDB;
using SharpDX;
using System.IO;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// Manages LiteDB database connection for world state persistence.
/// Database location: %LocalAppData%\AmbientGames\{GameName}\{WorldConfigRef}.db
/// </summary>
internal class WorldStateDatabase : IDisposable
{
    private readonly LiteDatabase _database;
    private bool _disposed;

    public WorldStateDatabase(string gameName, string worldConfigurationRef)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var gameDirectory = Path.Combine(appDataPath, "AmbientGames", gameName);

        // Ensure directory exists
        Directory.CreateDirectory(gameDirectory);

        var dbPath = Path.Combine(gameDirectory, $"{worldConfigurationRef}.db");

        // Configure BsonMapper to use InstanceId as the document ID
        var mapper = new BsonMapper();

        // Configure EntityInstance-derived types to use InstanceId as the document ID
        mapper.Entity<SagaInstance>().Id(x => x.InstanceId);
        mapper.Entity<AchievementInstance>().Id(x => x.InstanceId);

        // Configure AvatarEntity to use Id as document ID
        mapper.Entity<AvatarEntity>().Id(x => x.Id);

        // SharpDX Vector3 needs custom serialization
        mapper.RegisterType<Vector3>(
            serialize: v => new BsonDocument
            {
                ["X"] = v.X,
                ["Y"] = v.Y,
                ["Z"] = v.Z
            },
            deserialize: bson =>
            {
                // LiteDB stores numeric values as Double; read as double then cast to float
                var x = (float)bson["X"].AsDouble;
                var y = (float)bson["Y"].AsDouble;
                var z = (float)bson["Z"].AsDouble;
                return new Vector3(x, y, z);
            });

        // Ensure nested objects are serialized (LiteDB should handle this by default, but being explicit)
        mapper.IncludeNonPublic = false; // Only serialize public properties
        mapper.SerializeNullValues = false; // Don't waste space on nulls

        // REMOVED: CharacterInstance, LandmarkInstance, StructureInstance mappings
        // These are now tracked via SagaState (event-sourced from transactions)

        _database = new LiteDatabase(dbPath, mapper);
    }

    /// <summary>
    /// Gets the LiteDB database instance.
    /// Internal to restrict access to repository implementations only.
    /// </summary>
    internal LiteDatabase Database => _database;

    /// <summary>
    /// Gets a typed collection from the database.
    /// Internal to restrict access to service implementations within Sandbox.
    /// </summary>
    internal ILiteCollection<T> GetCollection<T>(string? collectionName = null)
    {
        return _database.GetCollection<T>(collectionName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _database?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
