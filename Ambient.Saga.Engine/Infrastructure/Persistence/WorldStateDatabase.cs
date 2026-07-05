using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using LiteDB;
using Microsoft.Extensions.Logging;
using SharpDX;
using System.IO;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// Manages LiteDB database connection for world state persistence.
/// Database location: %APPDATA%\{PublisherFolder}\{GameName}\saves\{WorldConfigRef}.db
/// </summary>
internal class WorldStateDatabase : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILogger<WorldStateDatabase>? _logger;
    private bool _disposed;

    public WorldStateDatabase(IGameSettings gameSettings, string worldConfigurationRef, ILogger<WorldStateDatabase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gameSettings);
        _logger = logger;

        var savesDirectory = Path.Combine(gameSettings.GetAppDataBasePath(), "saves");

        // Ensure directory exists
        Directory.CreateDirectory(savesDirectory);

        var dbPath = Path.Combine(savesDirectory, $"{worldConfigurationRef}.db");
        _logger?.LogInformation("Opening database: {DbPath}", dbPath);

        // Configure BsonMapper to use InstanceId as the document ID
        var mapper = new BsonMapper();

        // Configure EntityInstance-derived types to use InstanceId as the document ID
        mapper.Entity<SagaInstance>().Id(x => x.InstanceId);

        // BlockOwnership is a computed wrapper over Capabilities.Blocks — don't persist separately
        mapper.Entity<AvatarBase>().Ignore(x => x.BlockOwnership);

        // Configure AvatarEntity to use Id as document ID
        mapper.Entity<AvatarEntity>().Id(x => x.Id);

        // SharpDX Vector3 needs custom serialization
        mapper.RegisterType<Vector3>(
            serialize: v => new BsonDocument
            {
                [TransactionDataKeys.X] = v.X,
                [TransactionDataKeys.Y] = v.Y,
                [TransactionDataKeys.Z] = v.Z
            },
            deserialize: bson =>
            {
                // LiteDB stores numeric values as Double; read as double then cast to float
                var x = (float)bson[TransactionDataKeys.X].AsDouble;
                var y = (float)bson[TransactionDataKeys.Y].AsDouble;
                var z = (float)bson[TransactionDataKeys.Z].AsDouble;
                return new Vector3(x, y, z);
            });

        // SharpDX Int3 needs custom serialization (struct with fields, not properties)
        mapper.RegisterType<Int3>(
            serialize: v => new BsonDocument
            {
                [TransactionDataKeys.X] = v.X,
                [TransactionDataKeys.Y] = v.Y,
                [TransactionDataKeys.Z] = v.Z
            },
            deserialize: bson => new Int3(bson[TransactionDataKeys.X].AsInt32, bson[TransactionDataKeys.Y].AsInt32, bson[TransactionDataKeys.Z].AsInt32));

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
