using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

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
    private readonly ArcInstanceRepository _arcRepo;
    private readonly AvatarProgressRepository _progressRepo;
    private readonly Guid _avatarId = Guid.NewGuid();

    public ImportTransactionsProjectionTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _arcRepo = new ArcInstanceRepository(_database);
        _progressRepo = new AvatarProgressRepository(_database);
        _arcRepo.SetAvatarProgressRepository(_progressRepo);
    }

    public void Dispose() => _database?.Dispose();

    private ArcTransaction ServerToken(string tokenRef, long sequenceNumber, Guid? author = null) => new()
    {
        TransactionId = Guid.NewGuid(),
        SequenceNumber = sequenceNumber,
        Type = ArcTransactionType.QuestTokenAwarded,
        AvatarId = (author ?? _avatarId).ToString(),
        Status = TransactionStatus.Committed,
        LocalTimestamp = DateTime.UtcNow,
        ServerTimestamp = DateTime.UtcNow,
        Data = new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = tokenRef,
            [TransactionDataKeys.Reason] = "ServerSync"
        }
    };

    private ArcTransaction ServerDefeat(string characterRef, long sequenceNumber, Guid? author = null) => new()
    {
        TransactionId = Guid.NewGuid(),
        SequenceNumber = sequenceNumber,
        Type = ArcTransactionType.CharacterDefeated,
        AvatarId = (author ?? _avatarId).ToString(),
        Status = TransactionStatus.Committed,
        LocalTimestamp = DateTime.UtcNow,
        ServerTimestamp = DateTime.UtcNow,
        Data = new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = characterRef,
            [TransactionDataKeys.DefeatMethod] = "Combat"
        }
    };

    private ArcTransaction ServerReputation(string factionRef, int amount, long sequenceNumber) => new()
    {
        TransactionId = Guid.NewGuid(),
        SequenceNumber = sequenceNumber,
        Type = ArcTransactionType.ReputationChanged,
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
        var instance = await _arcRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var imported = await _arcRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<ArcTransaction>
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
        var instance = await _arcRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var defeat = ServerDefeat("DRAGON_LORD", sequenceNumber: 1);
        var rep = ServerReputation("GUILD_OF_MAGES", amount: 100, sequenceNumber: 2);

        var first = await _arcRepo.ImportTransactionsAsync(_avatarId, instance.InstanceId,
            new List<ArcTransaction> { defeat, rep });

        // Second pull includes the first batch again plus one new transaction
        var newDefeat = ServerDefeat("DRAGON_LORD", sequenceNumber: 3);
        var second = await _arcRepo.ImportTransactionsAsync(_avatarId, instance.InstanceId,
            new List<ArcTransaction> { defeat, rep, newDefeat });

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal(2, _progressRepo.GetBossDefeatedCount(_avatarId, "DRAGON_LORD"));
        Assert.Equal(100, _progressRepo.GetFactionReputation(_avatarId, "GUILD_OF_MAGES"));
    }

    [Fact]
    public async Task Import_SharedArc_ProjectsPerAuthor_NeverToPuller()
    {
        // Shared multiplayer arcs (no owner) carry transactions authored by OTHER
        // avatars. Projection must credit each author — never the pulling avatar,
        // or playing near peers would inflate the puller's cross-arc quest/boss/
        // reputation state (which gates prerequisites, dialogue, and triggers).
        var instance = await _arcRepo.GetOrRegisterMultiplayerInstanceAsync("SharedArc");
        var peerId = Guid.NewGuid();

        var imported = await _arcRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<ArcTransaction>
            {
                ServerToken("PEER_SEAL", sequenceNumber: 1, author: peerId),
                ServerDefeat("SHARED_BOSS", sequenceNumber: 2, author: peerId),
                ServerToken("OWN_SEAL", sequenceNumber: 3) // authored by the puller
            });

        Assert.Equal(3, imported);
        // Peer-authored rows land in the peer's tables…
        Assert.True(_progressRepo.HasQuestToken(peerId, "PEER_SEAL"));
        Assert.Equal(1, _progressRepo.GetBossDefeatedCount(peerId, "SHARED_BOSS"));
        // …and never in the puller's
        Assert.False(_progressRepo.HasQuestToken(_avatarId, "PEER_SEAL"));
        Assert.Equal(0, _progressRepo.GetBossDefeatedCount(_avatarId, "SHARED_BOSS"));
        // The puller's own-authored transaction still projects to the puller
        Assert.True(_progressRepo.HasQuestToken(_avatarId, "OWN_SEAL"));
    }

    [Fact]
    public async Task Import_EmptyBatch_IsNoOp()
    {
        var instance = await _arcRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var imported = await _arcRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<ArcTransaction>());

        Assert.Equal(0, imported);
        Assert.False(_progressRepo.HasQuestToken(_avatarId, "ANCIENT_SEAL"));
    }

    [Fact]
    public async Task Import_WhenProjectionRepositoryNotSet_DoesNotThrow()
    {
        // Arc can be hosted without avatar progress tables (e.g., server-side
        // validation). The import must still work, just without projection.
        using var db = new LiteDatabase(new MemoryStream());
        var arcRepoOnly = new ArcInstanceRepository(db);

        var instance = await arcRepoOnly.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var imported = await arcRepoOnly.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<ArcTransaction> { ServerToken("SEAL_OF_ISOLATION", sequenceNumber: 1) });

        Assert.Equal(1, imported);
    }

    [Fact]
    public async Task Import_InvalidatesReadModelCache()
    {
        // If an arc has a cached ArcState and a sync imports new transactions for it,
        // the cache would otherwise keep serving pre-import state. Verify the import
        // drops it so the next read recomputes from the updated transaction log.
        var readModel = new InMemoryArcReadModelRepository();
        _arcRepo.SetReadModelRepository(readModel);

        var instance = await _arcRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var staleState = new ArcState { ArcRef = "ArcA" };
        await readModel.UpdateCachedStateAsync(_avatarId, "ArcA", staleState, sequenceNumber: 0);
        Assert.NotNull(await readModel.GetCachedStateAsync(_avatarId, "ArcA"));

        await _arcRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<ArcTransaction> { ServerToken("POST_IMPORT_TOKEN", sequenceNumber: 1) });

        Assert.Null(await readModel.GetCachedStateAsync(_avatarId, "ArcA"));
    }

    [Fact]
    public async Task Import_EmptyBatch_DoesNotInvalidateReadModelCache()
    {
        // No new rows inserted means no state change — leave any cached replay alone.
        var readModel = new InMemoryArcReadModelRepository();
        _arcRepo.SetReadModelRepository(readModel);

        var instance = await _arcRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        var cached = new ArcState { ArcRef = "ArcA" };
        await readModel.UpdateCachedStateAsync(_avatarId, "ArcA", cached, sequenceNumber: 0);

        await _arcRepo.ImportTransactionsAsync(_avatarId, instance.InstanceId, new List<ArcTransaction>());

        Assert.NotNull(await readModel.GetCachedStateAsync(_avatarId, "ArcA"));
    }

    [Fact]
    public async Task Import_PreservesServerSequenceNumber()
    {
        var instance = await _arcRepo.GetOrCreateInstanceAsync(_avatarId, "ArcA");

        await _arcRepo.ImportTransactionsAsync(
            _avatarId,
            instance.InstanceId,
            new List<ArcTransaction>
            {
                ServerToken("T1", sequenceNumber: 42),
                ServerToken("T2", sequenceNumber: 43)
            });

        var stored = await _arcRepo.GetTransactionsAsync(instance.InstanceId);
        Assert.Equal(new long[] { 42, 43 }, stored.Select(t => t.SequenceNumber).ToArray());
    }
}
