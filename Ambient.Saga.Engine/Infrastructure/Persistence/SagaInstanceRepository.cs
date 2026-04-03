using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using LiteDB;
using System.Collections.Concurrent;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// LiteDB implementation of ISagaInstanceRepository.
/// Stores Saga instances and their transaction logs in LiteDB collections.
///
/// Schema:
/// - SagaInstances: { InstanceId, SagaRef, OwnerAvatarId, CreatedAt }
/// - SagaTransactions: { _id (auto), InstanceId (manual), TransactionId, Type, AvatarId, Status, LocalTimestamp, ServerTimestamp, SequenceNumber, Data }
///
/// Note: We store transactions in a separate collection with manual InstanceId linking,
/// since the domain SagaTransaction model doesn't have InstanceId as a property.
/// </summary>
public class SagaInstanceRepository : ISagaInstanceRepository
{
    private readonly ILiteDatabase _database;
    private readonly ILiteCollection<SagaInstance> _instances;
    private readonly ILiteCollection<SagaTransactionRecord> _transactions;

    // Per-instance locks to avoid global blocking - different sagas don't block each other
    private readonly ConcurrentDictionary<string, object> _instanceLocks = new();

    // In-memory instance cache — preserves CachedState/IsDirty across calls
    private readonly ConcurrentDictionary<string, SagaInstance> _instanceCache = new();

    public SagaInstanceRepository(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));

        // Configure fluent mapping for SagaInstance
        var mapper = database.Mapper;
        mapper.Entity<SagaInstance>()
            .Id(x => x.InstanceId, autoId: false); // We generate our own GUIDs

        _instances = _database.GetCollection<SagaInstance>("SagaInstances");
        _transactions = _database.GetCollection<SagaTransactionRecord>("SagaTransactions");

        // Create indexes for fast lookups
        _instances.EnsureIndex(x => x.InstanceId, unique: true);
        _instances.EnsureIndex(x => x.SagaRef);
        _instances.EnsureIndex(x => x.OwnerAvatarId);

        // CRITICAL: Composite unique index on computed CompositeKey property
        // Format: "OwnerAvatarId|SagaRef" - prevents duplicate instances for same avatar+saga combination
        _instances.EnsureIndex(x => x.CompositeKey, unique: true);

        _transactions.EnsureIndex(x => x.TransactionId, unique: true);
        _transactions.EnsureIndex(x => x.InstanceId);
        _transactions.EnsureIndex(x => x.SequenceNumber);
    }

    public Task<SagaInstance> GetOrCreateInstanceAsync(Guid avatarId, string sagaRef, CancellationToken ct = default)
    {
        // Use per-instance lock to prevent race condition without blocking other sagas
        var lockKey = $"{avatarId}|{sagaRef}";
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            // Return cached instance if available and clean (no new transactions to load)
            if (_instanceCache.TryGetValue(lockKey, out var cachedInstance) && !cachedInstance.IsDirty)
                return Task.FromResult(cachedInstance);

            // Try to find existing instance in DB
            var instance = _instances
                .Find(x => x.OwnerAvatarId == avatarId && x.SagaRef == sagaRef)
                .FirstOrDefault();

            if (instance != null)
            {
                // Load transactions for this instance
                var transactionRecords = _transactions
                    .Find(x => x.InstanceId == instance.InstanceId)
                    .OrderBy(x => x.SequenceNumber)
                    .ToList();

                instance.Transactions = transactionRecords.Select(r => r.ToTransaction()).ToList();

                // Check if our cached replay is still valid by comparing the highest
                // committed sequence number against what the cache was built from.
                if (_instanceCache.TryGetValue(lockKey, out var existing)
                    && existing.InstanceId == instance.InstanceId
                    && existing.CachedState != null)
                {
                    var maxCommittedSeq = instance.GetCommittedTransactions()
                        .Select(t => t.SequenceNumber)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (existing.CachedAtSequenceNumber == maxCommittedSeq)
                    {
                        // No new committed transactions since last replay — cache still valid
                        instance.CachedState = existing.CachedState;
                        instance.CachedAtSequenceNumber = existing.CachedAtSequenceNumber;
                        instance.IsDirty = false;
                    }
                    // else: new committed transactions exist — IsDirty defaults to true
                }
                // else: no cached version — IsDirty defaults to true

                _instanceCache[lockKey] = instance;

                return Task.FromResult(instance);
            }

            // Create new instance
            instance = new SagaInstance
            {
                InstanceId = Guid.NewGuid(),
                SagaRef = sagaRef,
                OwnerAvatarId = avatarId,
                InstanceType = SagaInstanceType.SinglePlayer,
                CreatedAt = DateTime.UtcNow,
                CompositeKey = $"{avatarId}|{sagaRef}" // Set composite key for unique index
            };

            // Insert will fail if unique constraint violated (shouldn't happen with lock, but defensive)
            try
            {
                _instances.Insert(instance);
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
            {
                // Rare edge case: another process created instance between check and insert
                // Re-query to get the existing instance
                instance = _instances
                    .Find(x => x.OwnerAvatarId == avatarId && x.SagaRef == sagaRef)
                    .FirstOrDefault();

                if (instance == null)
                    throw; // Should never happen, rethrow original exception

                // Load transactions for re-queried instance
                var transactionRecords = _transactions
                    .Find(x => x.InstanceId == instance.InstanceId)
                    .OrderBy(x => x.SequenceNumber)
                    .ToList();

                // Thread-safe: Replace list instead of Clear+AddRange
                instance.Transactions = transactionRecords.Select(r => r.ToTransaction()).ToList();
            }

            return Task.FromResult(instance);
        }
    }

    public Task<SagaInstance?> GetInstanceByIdAsync(Guid instanceId, CancellationToken ct = default)
    {
        var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();

        if (instance != null)
        {
            // Load transactions
            var transactionRecords = _transactions
                .Find(x => x.InstanceId == instanceId)
                .OrderBy(x => x.SequenceNumber)
                .ToList();

            // Thread-safe: Replace list instead of Clear+AddRange
            instance.Transactions = transactionRecords.Select(r => r.ToTransaction()).ToList();
        }

        return Task.FromResult(instance);
    }

    public Task<List<long>> AddTransactionsAsync(Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default)
    {
        // Use per-instance lock to prevent sequence number collisions without blocking other instances
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();
            if (instance == null)
                throw new InvalidOperationException($"Saga instance {instanceId} not found");

            // Get current max sequence number
            var maxSequence = _transactions
                .Find(x => x.InstanceId == instanceId)
                .Select(x => x.SequenceNumber)
                .DefaultIfEmpty(0)
                .Max();

            var sequenceNumbers = new List<long>();

            foreach (var transaction in transactions)
            {
                // Assign sequence number
                transaction.SequenceNumber = ++maxSequence;
                transaction.Status = TransactionStatus.Pending; // Will be committed separately

                // Create record with InstanceId linkage
                var record = SagaTransactionRecord.FromTransaction(transaction, instanceId);

                // Insert transaction
                _transactions.Insert(record);
                sequenceNumbers.Add(transaction.SequenceNumber);
            }

            return Task.FromResult(sequenceNumbers);
        }
    }

    public Task<(List<long> SequenceNumbers, bool Committed)> AddAndCommitTransactionsAsync(Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();
            if (instance == null)
                throw new InvalidOperationException($"Saga instance {instanceId} not found");

            try
            {
                _database.BeginTrans();

                try
                {
                    var maxSequence = _transactions
                        .Find(x => x.InstanceId == instanceId)
                        .Select(x => x.SequenceNumber)
                        .DefaultIfEmpty(0)
                        .Max();

                    var sequenceNumbers = new List<long>();

                    foreach (var transaction in transactions)
                    {
                        transaction.SequenceNumber = ++maxSequence;
                        transaction.Status = TransactionStatus.Committed;
                        transaction.ServerTimestamp = DateTime.UtcNow;

                        var record = SagaTransactionRecord.FromTransaction(transaction, instanceId);
                        _transactions.Insert(record);
                        sequenceNumbers.Add(transaction.SequenceNumber);
                    }

                    _database.Commit();
                    return Task.FromResult((sequenceNumbers, true));
                }
                catch
                {
                    _database.Rollback();
                    throw;
                }
            }
            catch
            {
                return Task.FromResult((new List<long>(), false));
            }
        }
    }

    public Task<int> ImportTransactionsAsync(Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var imported = 0;

            foreach (var transaction in transactions)
            {
                // Skip if already exists locally (idempotent)
                var existing = _transactions.FindOne(x => x.TransactionId == transaction.TransactionId);
                if (existing != null)
                    continue;

                // Preserve sequence number and status from server — do not reassign
                var record = SagaTransactionRecord.FromTransaction(transaction, instanceId);
                _transactions.Insert(record);
                imported++;
            }

            // Mark any cached instance as dirty so the next read triggers a reload
            if (imported > 0)
            {
                InvalidateInstanceCache(instanceId);
            }

            return Task.FromResult(imported);
        }
    }

    public Task<List<SagaTransaction>> GetTransactionsAsync(Guid instanceId, CancellationToken ct = default)
    {
        var transactionRecords = _transactions
            .Find(x => x.InstanceId == instanceId)
            .OrderBy(x => x.SequenceNumber)
            .ToList();

        return Task.FromResult(transactionRecords.Select(r => r.ToTransaction()).ToList());
    }

    public Task<List<SagaTransaction>> GetTransactionsAfterSequenceAsync(Guid instanceId, long afterSequence, CancellationToken ct = default)
    {
        var transactionRecords = _transactions
            .Find(x => x.InstanceId == instanceId && x.SequenceNumber > afterSequence)
            .OrderBy(x => x.SequenceNumber)
            .ToList();

        return Task.FromResult(transactionRecords.Select(r => r.ToTransaction()).ToList());
    }

    public Task<bool> CommitTransactionsAsync(Guid instanceId, List<Guid> transactionIds, CancellationToken ct = default)
    {
        try
        {
            // CRITICAL FIX: Use database transaction for atomicity
            // All commits succeed or all fail - no partial commits
            _database.BeginTrans();

            try
            {
                foreach (var transactionId in transactionIds)
                {
                    var record = _transactions.FindOne(x => x.TransactionId == transactionId);
                    if (record == null)
                    {
                        _database.Rollback();
                        return Task.FromResult(false); // Transaction not found
                    }

                    if (record.InstanceId != instanceId)
                    {
                        _database.Rollback();
                        return Task.FromResult(false); // Transaction belongs to different instance
                    }

                    if (record.Status != TransactionStatus.Pending)
                    {
                        _database.Rollback();
                        return Task.FromResult(false); // Already committed or rolled back
                    }

                    // Mark as committed
                    record.Status = TransactionStatus.Committed;
                    record.ServerTimestamp = DateTime.UtcNow;
                    _transactions.Update(record);
                }

                _database.Commit();
                return Task.FromResult(true);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task RollbackTransactionsAsync(Guid instanceId, List<Guid> transactionIds, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            _database.BeginTrans();

            try
            {
                foreach (var transactionId in transactionIds)
                {
                    var record = _transactions.FindOne(x => x.TransactionId == transactionId);
                    if (record != null && record.InstanceId == instanceId)
                    {
                        record.Status = TransactionStatus.Rejected;
                        _transactions.Update(record);
                    }
                }

                _database.Commit();
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task<List<SagaInstance>> GetAllInstancesForAvatarAsync(Guid avatarId, CancellationToken ct = default)
    {
        var instances = _instances
            .Find(x => x.OwnerAvatarId == avatarId)
            .ToList();

        // Load transactions for each instance
        foreach (var instance in instances)
        {
            var transactionRecords = _transactions
                .Find(x => x.InstanceId == instance.InstanceId)
                .OrderBy(x => x.SequenceNumber)
                .ToList();

            // Thread-safe: Replace list instead of Clear+AddRange
            instance.Transactions = transactionRecords.Select(r => r.ToTransaction()).ToList();
        }

        return Task.FromResult(instances);
    }

    /// <summary>
    /// Marks any cached instance with the given ID as dirty so the next
    /// GetOrCreateInstanceAsync call reloads from DB instead of returning stale state.
    /// Used after external writes (sync import) that bypass AddTransaction.
    /// </summary>
    private void InvalidateInstanceCache(Guid instanceId)
    {
        foreach (var kvp in _instanceCache)
        {
            if (kvp.Value.InstanceId == instanceId)
            {
                kvp.Value.IsDirty = true;
                break;
            }
        }
    }
}

/// <summary>
/// Wrapper for SagaTransaction that includes InstanceId for LiteDB storage.
/// The domain model doesn't have InstanceId on transactions, so we add it here for persistence.
/// </summary>
internal class SagaTransactionRecord
{
    public int Id { get; set; } // LiteDB auto-increment primary key
    public Guid InstanceId { get; set; } // Manual linkage to SagaInstance
    public Guid TransactionId { get; set; }
    public long SequenceNumber { get; set; }
    public string? AvatarId { get; set; }
    public DateTime LocalTimestamp { get; set; }
    public DateTime? ServerTimestamp { get; set; }
    public TransactionStatus Status { get; set; }
    public SagaTransactionType Type { get; set; }
    public string? ExtensionTypeName { get; set; }
    public Dictionary<string, string> Data { get; set; } = new();
    public Guid? ReversesTransactionId { get; set; }
    public string? ReversalReason { get; set; }

    public static SagaTransactionRecord FromTransaction(SagaTransaction transaction, Guid instanceId)
    {
        return new SagaTransactionRecord
        {
            InstanceId = instanceId,
            TransactionId = transaction.TransactionId,
            SequenceNumber = transaction.SequenceNumber,
            AvatarId = transaction.AvatarId,
            LocalTimestamp = transaction.LocalTimestamp,
            ServerTimestamp = transaction.ServerTimestamp,
            Status = transaction.Status,
            Type = transaction.Type,
            ExtensionTypeName = transaction.ExtensionTypeName,
            Data = new Dictionary<string, string>(transaction.Data),
            ReversesTransactionId = transaction.ReversesTransactionId,
            ReversalReason = transaction.ReversalReason
        };
    }

    public SagaTransaction ToTransaction()
    {
        return new SagaTransaction
        {
            TransactionId = TransactionId,
            SequenceNumber = SequenceNumber,
            AvatarId = AvatarId,
            LocalTimestamp = LocalTimestamp,
            ServerTimestamp = ServerTimestamp,
            Status = Status,
            Type = Type,
            ExtensionTypeName = ExtensionTypeName,
            Data = new Dictionary<string, string>(Data),
            ReversesTransactionId = ReversesTransactionId,
            ReversalReason = ReversalReason
        };
    }
}
