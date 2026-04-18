using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Verifies that ImportTransactionsAsync projects newly-inserted transactions
/// into the avatar progress tables atomically. This is the cross-device sync
/// path: a client pulls server transactions, imports them, and the Avatar*
/// tables must be rebuilt without the caller having to do anything extra.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class ImportTransactionsProjectionTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly SagaInstanceRepository _sagaRepo;
    private readonly AvatarProgressRepository _progressRepo;
    private readonly Guid _avatarId = Guid.NewGuid();

    public ImportTransactionsProjectionTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _sagaRepo = new SagaInstanceRepository(_database);
        _progressRepo = new AvatarProgressRepository(_database);
        _sagaRepo.SetAvatarProgressRepository(_progressRepo);
    }

    public void Dispose() => _database?.Dispose();

    private SagaTransaction ServerToken(string tokenRef, long sequenceNumber) => new()
    {
        TransactionId = Guid.NewGuid(),
        SequenceNumber = sequenceNumber,
        Type = SagaTransactionType.QuestTokenAwarded,
        AvatarId = _avatarId.ToString(),
        Status = TransactionStatus.Committed,
        LocalTimestamp = DateTime.UtcNow,
        ServerTimestamp = DateTime.UtcNow,
        Data = new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = tokenRef,
            [TransactionDataKeys.Reason] = "ServerSync"
        }
    };

    private SagaTransaction ServerDefeat(string characterRef, long sequenceNumber) => new()
    {
        TransactionId = Guid.NewGuid(),
        SequenceNumber = sequenceNumber,
        Type = SagaTransactionType.CharacterDefeated,
        AvatarId = _avatarId.ToString(),
        Status = TransactionStatus.Committed,
        LocalTimestamp = DateTime.UtcNow,
        ServerTimestamp = DateTime.UtcNow,
        Data = new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = characterRef,
            [TransactionDataKeys.DefeatMethod] = "Combat"
        }
    };

    private SagaTransaction ServerReputation(string factionRef, int amount, long sequenceNumber) => new()
    {
        TransactionId = Guid.NewGuid(),
        SequenceNumber = sequenceNumber,
        Type = SagaTransactionType.ReputationChanged,
        AvatarId = _avatarId.ToString(),
        Status = TransactionStatus.Committed,
        LocalTimestamp = DateTime.UtcNow,
        ServerTimestamp = DateTime.UtcNow,
        Data = new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = factionRef,
            [TransactionDataKeys.Amount] = amount.ToString()
        }
    };

    [Fact]
    public async Task Import_PopulatesAvatarProgressTables()
    {
        var instance = await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var imported = await _sagaRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<SagaTransaction>
            {
                ServerToken("ANCIENT_SEAL", sequenceNumber: 1),
                ServerDefeat("DRAGON_LORD", sequenceNumber: 2),
                ServerReputation("GUILD_OF_MAGES", amount: 75, sequenceNumber: 3)
            });

        Assert.Equal(3, imported);
        Assert.True(_progressRepo.HasQuestToken(_avatarId, "ANCIENT_SEAL"));
        Assert.Equal(1, _progressRepo.GetBossDefeatedCount(_avatarId, "DRAGON_LORD"));
        Assert.Equal(75, _progressRepo.GetFactionReputation(_avatarId, "GUILD_OF_MAGES"));
    }

    [Fact]
    public async Task Import_OverlappingBatch_DoesNotDoubleCount()
    {
        // A real sync can pull a batch that overlaps with what we already have
        // (e.g. client retries, server resends). The non-idempotent projections
        // (BossDefeats increments, FactionReputation accumulates) would double
        // if the repository re-projected already-inserted transactions.
        var instance = await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var defeat = ServerDefeat("DRAGON_LORD", sequenceNumber: 1);
        var rep = ServerReputation("GUILD_OF_MAGES", amount: 100, sequenceNumber: 2);

        var first = await _sagaRepo.ImportTransactionsAsync(_avatarId, instance.InstanceId,
            new List<SagaTransaction> { defeat, rep });

        // Second pull includes the first batch again plus one new transaction
        var newDefeat = ServerDefeat("DRAGON_LORD", sequenceNumber: 3);
        var second = await _sagaRepo.ImportTransactionsAsync(_avatarId, instance.InstanceId,
            new List<SagaTransaction> { defeat, rep, newDefeat });

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal(2, _progressRepo.GetBossDefeatedCount(_avatarId, "DRAGON_LORD"));
        Assert.Equal(100, _progressRepo.GetFactionReputation(_avatarId, "GUILD_OF_MAGES"));
    }

    [Fact]
    public async Task Import_EmptyBatch_IsNoOp()
    {
        var instance = await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var imported = await _sagaRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<SagaTransaction>());

        Assert.Equal(0, imported);
        Assert.False(_progressRepo.HasQuestToken(_avatarId, "ANCIENT_SEAL"));
    }

    [Fact]
    public async Task Import_WhenProjectionRepositoryNotSet_DoesNotThrow()
    {
        // Saga can be hosted without avatar progress tables (e.g., server-side
        // validation). The import must still work, just without projection.
        using var db = new LiteDatabase(new MemoryStream());
        var sagaRepoOnly = new SagaInstanceRepository(db);

        var instance = await sagaRepoOnly.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var imported = await sagaRepoOnly.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<SagaTransaction> { ServerToken("SEAL_OF_ISOLATION", sequenceNumber: 1) });

        Assert.Equal(1, imported);
    }

    [Fact]
    public async Task Import_InvalidatesReadModelCache()
    {
        // If an arc has a cached SagaState and a sync imports new transactions for it,
        // the cache would otherwise keep serving pre-import state. Verify the import
        // drops it so the next read recomputes from the updated transaction log.
        var readModel = new InMemorySagaReadModelRepository();
        _sagaRepo.SetReadModelRepository(readModel);

        var instance = await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var staleState = new SagaState { SagaRef = "ArcA" };
        await readModel.UpdateCachedStateAsync(_avatarId, "ArcA", staleState, sequenceNumber: 0);
        Assert.NotNull(await readModel.GetCachedStateAsync(_avatarId, "ArcA"));

        await _sagaRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<SagaTransaction> { ServerToken("POST_IMPORT_TOKEN", sequenceNumber: 1) });

        Assert.Null(await readModel.GetCachedStateAsync(_avatarId, "ArcA"));
    }

    [Fact]
    public async Task Import_EmptyBatch_DoesNotInvalidateReadModelCache()
    {
        // No new rows inserted means no state change — leave any cached replay alone.
        var readModel = new InMemorySagaReadModelRepository();
        _sagaRepo.SetReadModelRepository(readModel);

        var instance = await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var cached = new SagaState { SagaRef = "ArcA" };
        await readModel.UpdateCachedStateAsync(_avatarId, "ArcA", cached, sequenceNumber: 0);

        await _sagaRepo.ImportTransactionsAsync(_avatarId, instance.InstanceId, new List<SagaTransaction>());

        Assert.NotNull(await readModel.GetCachedStateAsync(_avatarId, "ArcA"));
    }

    [Fact]
    public async Task Import_PreservesServerSequenceNumber()
    {
        var instance = await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        await _sagaRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<SagaTransaction>
            {
                ServerToken("T1", sequenceNumber: 42),
                ServerToken("T2", sequenceNumber: 43)
            });

        var stored = await _sagaRepo.GetTransactionsAsync(instance.InstanceId);
        Assert.Equal(new long[] { 42, 43 }, stored.Select(t => t.SequenceNumber).ToArray());
    }
}
