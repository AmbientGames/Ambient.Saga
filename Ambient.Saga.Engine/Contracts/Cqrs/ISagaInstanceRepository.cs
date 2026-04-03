using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Contracts.Cqrs;

/// <summary>
/// Repository for Saga instances (write-side event sourcing).
/// Handles transaction log persistence and retrieval.
/// </summary>
public interface ISagaInstanceRepository
{
    /// <summary>
    /// Get Saga instance for avatar (creates if doesn't exist).
    /// </summary>
    Task<SagaInstance> GetOrCreateInstanceAsync(Guid avatarId, string sagaRef, CancellationToken ct = default);

    /// <summary>
    /// Get Saga instance by ID.
    /// </summary>
    Task<SagaInstance?> GetInstanceByIdAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Add transactions to Saga instance.
    /// Returns the new sequence numbers assigned.
    /// </summary>
    Task<List<long>> AddTransactionsAsync(Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Get all transactions for a Saga instance.
    /// </summary>
    Task<List<SagaTransaction>> GetTransactionsAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Get transactions after a specific sequence number (for incremental updates).
    /// </summary>
    Task<List<SagaTransaction>> GetTransactionsAfterSequenceAsync(Guid instanceId, long afterSequence, CancellationToken ct = default);

    /// <summary>
    /// Atomically add and commit transactions in a single locked operation.
    /// Prevents race conditions between add and commit that could allow
    /// interleaved writes from concurrent handlers on the same instance.
    /// Returns the assigned sequence numbers and whether the commit succeeded.
    /// </summary>
    Task<(List<long> SequenceNumbers, bool Committed)> AddAndCommitTransactionsAsync(Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Mark transactions as committed (optimistic concurrency).
    /// Returns true if all transactions were committed, false if conflict detected.
    /// Prefer AddAndCommitTransactionsAsync for new code to avoid race conditions.
    /// </summary>
    Task<bool> CommitTransactionsAsync(Guid instanceId, List<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>
    /// Mark transactions as rolled back (conflict resolution).
    /// </summary>
    Task RollbackTransactionsAsync(Guid instanceId, List<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>
    /// Import transactions from the server, preserving their sequence numbers and status.
    /// Used during pull/recovery — does not reassign sequences or override status.
    /// Skips transactions that already exist locally (by TransactionId).
    /// </summary>
    Task<int> ImportTransactionsAsync(Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Get all Saga instances for an avatar.
    /// </summary>
    Task<List<SagaInstance>> GetAllInstancesForAvatarAsync(Guid avatarId, CancellationToken ct = default);
}
