using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Contracts.Cqrs;

/// <summary>
/// A server rejection of a locally-pushed transaction.
/// </summary>
public record TransactionRejection(Guid TransactionId, string? Reason);

/// <summary>
/// Repository for Arc instances (write-side event sourcing).
/// Handles transaction log persistence and retrieval.
/// </summary>
public interface IArcInstanceRepository
{
    /// <summary>
    /// Get Arc instance for avatar (creates if doesn't exist).
    /// </summary>
    Task<ArcInstance> GetOrCreateInstanceAsync(Guid avatarId, string arcRef, CancellationToken ct = default);

    /// <summary>
    /// Get the shared multiplayer Arc instance for a ref (creates if doesn't exist).
    /// Used for server-sourced arcs (shopkeepers, geocaches, remnant Loot) where the instance is
    /// not owned by any one avatar and transactions from all players are merged.
    /// OwnerAvatarId is null; CompositeKey is "NULL|{arcRef}".
    /// </summary>
    Task<ArcInstance> GetOrRegisterMultiplayerInstanceAsync(string arcRef, CancellationToken ct = default);

    /// <summary>
    /// Add transactions to Arc instance.
    /// Returns the new sequence numbers assigned.
    /// </summary>
    Task<List<long>> AddTransactionsAsync(Guid instanceId, List<ArcTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Get all transactions for an arc instance.
    /// </summary>
    Task<List<ArcTransaction>> GetTransactionsAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Get transactions after a specific sequence number (for incremental updates).
    /// </summary>
    Task<List<ArcTransaction>> GetTransactionsAfterSequenceAsync(Guid instanceId, long afterSequence, CancellationToken ct = default);

    /// <summary>
    /// Atomically add and commit transactions in a single locked operation.
    /// Prevents race conditions between add and commit that could allow
    /// interleaved writes from concurrent handlers on the same instance.
    /// Returns the assigned sequence numbers and whether the commit succeeded.
    /// </summary>
    Task<(List<long> SequenceNumbers, bool Committed)> AddAndCommitTransactionsAsync(Guid instanceId, List<ArcTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Mark transactions as committed (optimistic concurrency).
    /// Returns true if all transactions were committed, false if conflict detected.
    /// Prefer AddAndCommitTransactionsAsync for new code to avoid race conditions.
    /// </summary>
    Task<bool> CommitTransactionsAsync(Guid instanceId, List<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>
    /// Mark transactions as rolled back (conflict resolution).
    /// Imported transactions (SyncState = Imported) are never touched — they are
    /// server-confirmed peer history, not local optimism.
    /// </summary>
    Task RollbackTransactionsAsync(Guid instanceId, List<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>
    /// Mark locally-created transactions as acknowledged by the server
    /// (SyncState = Synced) so they are never pushed again. Stamps the
    /// server timestamp when the record doesn't have one yet.
    /// Only LocalUnsynced records are affected.
    /// </summary>
    Task MarkTransactionsSyncedAsync(Guid instanceId, IReadOnlyCollection<Guid> transactionIds, DateTime? serverTimestamp = null, CancellationToken ct = default);

    /// <summary>
    /// Full server-rejection handling for locally-created transactions, atomically:
    /// marks each Rejected (excluding it from replay), writes a committed
    /// client-only TransactionReversed compensation referencing it (audit trail +
    /// reward-ledger consumers), and reverses its cross-arc projections
    /// (IAvatarProgressRepository). Imported and already-rejected transactions are
    /// skipped — rejection handling applies only to LocalUnsynced pushes.
    /// Avatar-entity side effects (items/credits already applied by handlers) are
    /// NOT reversed here — see FEATURE-STATUS "online path".
    /// Returns the compensating transactions written.
    /// </summary>
    Task<List<ArcTransaction>> RejectAndCompensateTransactionsAsync(Guid instanceId, IReadOnlyList<TransactionRejection> rejections, CancellationToken ct = default);

    /// <summary>
    /// Import transactions from the server, preserving their sequence numbers and status.
    /// Used during pull/recovery — does not reassign sequences or override status.
    /// Skips transactions that already exist locally (by TransactionId).
    /// Newly-inserted transactions are projected to the avatar progress tables in the
    /// same LiteDB transaction, so the log and projections can't drift.
    /// </summary>
    Task<int> ImportTransactionsAsync(Guid avatarId, Guid instanceId, List<ArcTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Get all arc instances for an avatar.
    /// </summary>
    Task<List<ArcInstance>> GetAllInstancesForAvatarAsync(Guid avatarId, CancellationToken ct = default);

    /// <summary>
    /// Persist the sync watermark fields (LastSyncedSequenceNumber, LastSyncedAt, ServerVersion)
    /// on the stored instance. Called after a successful push to avoid re-sending confirmed transactions.
    /// </summary>
    Task UpdateSyncWatermarkAsync(Guid instanceId, long lastSyncedSequenceNumber, DateTime lastSyncedAt, DateTime? serverVersion, CancellationToken ct = default);

    /// <summary>
    /// Persists the max server-clock timestamp of transactions imported via pull.
    /// Sent back as the pull watermark on subsequent pulls (server timestamps are
    /// globally ordered, unlike client-local sequence numbers, which collide
    /// across avatars on shared arcs).
    /// </summary>
    Task UpdatePullWatermarkAsync(Guid instanceId, DateTime lastPulledServerTimestamp, CancellationToken ct = default);
}
