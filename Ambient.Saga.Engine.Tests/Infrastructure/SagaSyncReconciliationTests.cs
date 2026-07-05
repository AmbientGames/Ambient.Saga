using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Engine.Tests.Infrastructure;

/// <summary>
/// Client-side sync reconciliation invariants (2026-07-05 audit F1/F2/F3):
/// - Push selection is per-transaction SyncState, never a sequence watermark —
///   pulled foreign transactions on shared arcs carry colliding sequence numbers
///   and must not bury local unsynced work (F1).
/// - Imported transactions never push and are never rejected/reversed locally (F1).
/// - Server rejection of a local transaction marks it Rejected, writes a committed
///   client-only TransactionReversed compensation, and reverses its cross-arc
///   projections (F2).
/// - Re-importing the same pull payload is idempotent, so the server's >= pull
///   filter can safely re-send watermark rows (F3).
/// </summary>
public class SagaSyncReconciliationTests : IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly LiteDatabase _db;
    private readonly SagaInstanceRepository _repo;
    private readonly AvatarProgressRepository _progress;
    private readonly Guid _avatarA = Guid.NewGuid();
    private readonly Guid _avatarB = Guid.NewGuid();

    public SagaSyncReconciliationTests()
    {
        _db = new LiteDatabase(_stream);
        _repo = new SagaInstanceRepository(_db);
        _progress = new AvatarProgressRepository(_db);
        _repo.SetAvatarProgressRepository(_progress);
    }

    public void Dispose()
    {
        _db.Dispose();
        _stream.Dispose();
    }

    private static SagaTransaction CreateLocal(SagaTransactionType type, Guid avatarId, Dictionary<string, string>? data = null)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = data ?? new Dictionary<string, string>()
        };

    private static SagaTransaction CreateImported(SagaTransactionType type, Guid avatarId, long sequenceNumber, Dictionary<string, string>? data = null)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = avatarId.ToString(),
            SequenceNumber = sequenceNumber,
            Status = TransactionStatus.Committed,
            SyncState = TransactionSyncState.Imported,
            LocalTimestamp = DateTime.UtcNow.AddMinutes(-1),
            ServerTimestamp = DateTime.UtcNow.AddSeconds(-30),
            Data = data ?? new Dictionary<string, string>()
        };

    /// <summary>The push predicate SagaSyncService uses (mirrored here — Engine.Core is not referenced).</summary>
    private static List<SagaTransaction> SelectForPush(IEnumerable<SagaTransaction> transactions)
        => transactions
            .Where(t => t.Status == TransactionStatus.Committed && t.SyncState == TransactionSyncState.LocalUnsynced)
            .OrderBy(t => t.SequenceNumber)
            .ToList();

    // ===== F1: watermark cross-contamination =====

    [Fact]
    public async Task LocalCommit_SurvivesImportOfHigherForeignSequences_AndStillPushes()
    {
        var instance = await _repo.GetOrRegisterMultiplayerInstanceAsync("shared-arc-f1");

        var local = CreateLocal(SagaTransactionType.EntityInteracted, _avatarA,
            new Dictionary<string, string> { [TransactionDataKeys.FeatureRef] = "well" });
        await _repo.AddAndCommitTransactionsAsync(instance.InstanceId, [local]);

        // Another avatar's transactions arrive via pull with HIGHER client-local
        // sequence numbers — under the old per-instance watermark these buried
        // the local transaction forever
        var foreign1 = CreateImported(SagaTransactionType.EntityInteracted, _avatarB, 5,
            new Dictionary<string, string> { [TransactionDataKeys.FeatureRef] = "well" });
        var foreign2 = CreateImported(SagaTransactionType.EntityInteracted, _avatarB, 6,
            new Dictionary<string, string> { [TransactionDataKeys.FeatureRef] = "well" });
        await _repo.ImportTransactionsAsync(_avatarA, instance.InstanceId, [foreign1, foreign2]);

        var all = await _repo.GetTransactionsAsync(instance.InstanceId);
        var toPush = SelectForPush(all);

        var pushed = Assert.Single(toPush);
        Assert.Equal(local.TransactionId, pushed.TransactionId);
    }

    [Fact]
    public async Task ImportedTransactions_ArePersistedAsImported_AndNeverSelectedForPush()
    {
        var instance = await _repo.GetOrRegisterMultiplayerInstanceAsync("shared-arc-imported");

        // Even if the caller forgets to stamp SyncState, import enforces Imported
        var foreign = CreateImported(SagaTransactionType.EntityInteracted, _avatarB, 3);
        foreign.SyncState = TransactionSyncState.LocalUnsynced;
        await _repo.ImportTransactionsAsync(_avatarA, instance.InstanceId, [foreign]);

        var all = await _repo.GetTransactionsAsync(instance.InstanceId);
        var stored = Assert.Single(all);
        Assert.Equal(TransactionSyncState.Imported, stored.SyncState);
        Assert.Empty(SelectForPush(all));
    }

    [Fact]
    public async Task RejectAndCompensate_ImportedTransaction_IsUntouched()
    {
        var instance = await _repo.GetOrRegisterMultiplayerInstanceAsync("shared-arc-reject-import");

        var foreign = CreateImported(SagaTransactionType.EntityInteracted, _avatarB, 4);
        await _repo.ImportTransactionsAsync(_avatarA, instance.InstanceId, [foreign]);

        // The inverse watermark race used to re-push imported transactions, get them
        // rejected server-side, and erase legitimate peer history from replay
        var reversals = await _repo.RejectAndCompensateTransactionsAsync(
            instance.InstanceId, [new TransactionRejection(foreign.TransactionId, "duplicate")]);

        Assert.Empty(reversals);
        var all = await _repo.GetTransactionsAsync(instance.InstanceId);
        var stored = Assert.Single(all); // no reversal written
        Assert.Equal(TransactionStatus.Committed, stored.Status);
    }

    // ===== F2: rejection compensation =====

    [Fact]
    public async Task RejectAndCompensate_LocalTransactions_MarksRejected_WritesReversals_AndUnprojects()
    {
        var instance = await _repo.GetOrCreateInstanceAsync(_avatarA, "arc-f2");

        var token = CreateLocal(SagaTransactionType.QuestTokenAwarded, _avatarA,
            new Dictionary<string, string> { [TransactionDataKeys.QuestTokenRef] = "token-f2" });
        var rep = CreateLocal(SagaTransactionType.ReputationChanged, _avatarA,
            new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = "faction-f2",
                [TransactionDataKeys.Amount] = "10"
            });
        await _repo.AddAndCommitTransactionsAsync(instance.InstanceId, [token, rep]);

        // Projections applied on commit
        Assert.True(_progress.HasQuestToken(_avatarA, "token-f2"));
        Assert.Equal(10, _progress.GetFactionReputation(_avatarA, "faction-f2"));

        var reversals = await _repo.RejectAndCompensateTransactionsAsync(instance.InstanceId,
        [
            new TransactionRejection(token.TransactionId, "Validation failed"),
            new TransactionRejection(rep.TransactionId, "Validation failed")
        ]);

        // Compensating transactions: committed, client-only, referencing the originals
        Assert.Equal(2, reversals.Count);
        Assert.All(reversals, r =>
        {
            Assert.Equal(SagaTransactionType.TransactionReversed, r.Type);
            Assert.Equal(TransactionStatus.Committed, r.Status);
            Assert.Equal(TransactionSyncState.LocalOnly, r.SyncState);
        });
        Assert.Contains(reversals, r => r.ReversesTransactionId == token.TransactionId);
        Assert.Contains(reversals, r => r.ReversesTransactionId == rep.TransactionId);

        // Originals rejected → excluded from replay
        var all = await _repo.GetTransactionsAsync(instance.InstanceId);
        Assert.Equal(TransactionStatus.Rejected, all.Single(t => t.TransactionId == token.TransactionId).Status);
        Assert.Equal(TransactionStatus.Rejected, all.Single(t => t.TransactionId == rep.TransactionId).Status);

        // Cross-arc projections reversed
        Assert.False(_progress.HasQuestToken(_avatarA, "token-f2"));
        Assert.Equal(0, _progress.GetFactionReputation(_avatarA, "faction-f2"));

        // Compensations never push
        Assert.Empty(SelectForPush(all));
    }

    [Fact]
    public async Task RejectAndCompensate_IsIdempotent()
    {
        var instance = await _repo.GetOrCreateInstanceAsync(_avatarA, "arc-f2-idem");

        var rep = CreateLocal(SagaTransactionType.ReputationChanged, _avatarA,
            new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = "faction-idem",
                [TransactionDataKeys.Amount] = "10"
            });
        await _repo.AddAndCommitTransactionsAsync(instance.InstanceId, [rep]);

        var rejection = new TransactionRejection(rep.TransactionId, "Validation failed");
        var first = await _repo.RejectAndCompensateTransactionsAsync(instance.InstanceId, [rejection]);
        var second = await _repo.RejectAndCompensateTransactionsAsync(instance.InstanceId, [rejection]);

        Assert.Single(first);
        Assert.Empty(second);

        // Unprojected exactly once (0, not -10)
        Assert.Equal(0, _progress.GetFactionReputation(_avatarA, "faction-idem"));

        var all = await _repo.GetTransactionsAsync(instance.InstanceId);
        Assert.Single(all, t => t.Type == SagaTransactionType.TransactionReversed);
    }

    // ===== F3: >= pull filter → re-imports must be idempotent =====

    [Fact]
    public async Task Reimport_SamePullPayload_IsIdempotent()
    {
        var instance = await _repo.GetOrCreateInstanceAsync(_avatarA, "arc-f3");

        var defeat = CreateImported(SagaTransactionType.CharacterDefeated, _avatarA, 1,
            new Dictionary<string, string> { [TransactionDataKeys.CharacterRef] = "boss-f3" });

        var imported1 = await _repo.ImportTransactionsAsync(_avatarA, instance.InstanceId, [defeat]);
        var imported2 = await _repo.ImportTransactionsAsync(_avatarA, instance.InstanceId, [defeat]);

        Assert.Equal(1, imported1);
        Assert.Equal(0, imported2);

        // Non-idempotent projection (boss defeat count) applied exactly once
        Assert.Equal(1, _progress.GetBossDefeatedCount(_avatarA, "boss-f3"));
        Assert.Single(await _repo.GetTransactionsAsync(instance.InstanceId));
    }

    // ===== Push acknowledgement =====

    [Fact]
    public async Task MarkTransactionsSynced_OnlyMovesLocalUnsynced_AndStampsServerTimestamp()
    {
        var instance = await _repo.GetOrRegisterMultiplayerInstanceAsync("shared-arc-ack");

        var local = CreateLocal(SagaTransactionType.EntityInteracted, _avatarA,
            new Dictionary<string, string> { [TransactionDataKeys.FeatureRef] = "door" });
        await _repo.AddAndCommitTransactionsAsync(instance.InstanceId, [local]);

        var foreign = CreateImported(SagaTransactionType.EntityInteracted, _avatarB, 9);
        var foreignServerTs = foreign.ServerTimestamp;
        await _repo.ImportTransactionsAsync(_avatarA, instance.InstanceId, [foreign]);

        var batchTimestamp = DateTime.UtcNow;
        await _repo.MarkTransactionsSyncedAsync(
            instance.InstanceId,
            new[] { local.TransactionId, foreign.TransactionId },
            batchTimestamp);

        var all = await _repo.GetTransactionsAsync(instance.InstanceId);
        var storedLocal = all.Single(t => t.TransactionId == local.TransactionId);
        var storedForeign = all.Single(t => t.TransactionId == foreign.TransactionId);

        Assert.Equal(TransactionSyncState.Synced, storedLocal.SyncState);
        Assert.NotNull(storedLocal.ServerTimestamp);

        // Imported record untouched by the ack (LiteDB round-trips DateTime as
        // local kind — normalize to UTC before comparing)
        Assert.Equal(TransactionSyncState.Imported, storedForeign.SyncState);
        Assert.Equal(
            foreignServerTs!.Value.ToUniversalTime(),
            storedForeign.ServerTimestamp!.Value.ToUniversalTime(),
            TimeSpan.FromSeconds(1));

        Assert.Empty(SelectForPush(all));
    }
}
