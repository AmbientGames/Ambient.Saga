using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Contracts.Persistence;
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

    private IAvatarProgressRepository? _avatarProgressRepository;
    private ISagaReadModelRepository? _readModelRepository;

    public void SetAvatarProgressRepository(IAvatarProgressRepository repository)
        => _avatarProgressRepository = repository;

    public void SetReadModelRepository(ISagaReadModelRepository repository)
        => _readModelRepository = repository;

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
        // If a multiplayer instance exists for this sagaRef, every avatar shares it — prefer it
        // over creating a new single-player instance. This lets commands written for SinglePlayer
        // sagas transparently target Multiplayer ones (e.g. server-sourced arcs).
        var multiplayerKey = $"NULL|{sagaRef}";
        var multiplayer = _instances.Find(x => x.CompositeKey == multiplayerKey).FirstOrDefault();
        if (multiplayer != null)
            return GetOrRegisterMultiplayerInstanceAsync(sagaRef, ct);

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

                if (instance.Transactions.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SagaInstanceRepository] READ empty instance: SagaRef='{sagaRef}' AvatarId={avatarId} InstanceId={instance.InstanceId}");
                }

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

    public Task<SagaInstance> GetOrRegisterMultiplayerInstanceAsync(string sagaRef, CancellationToken ct = default)
    {
        var lockKey = $"NULL|{sagaRef}";
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            if (_instanceCache.TryGetValue(lockKey, out var cached) && !cached.IsDirty)
                return Task.FromResult(cached);

            var instance = _instances
                .Find(x => x.CompositeKey == lockKey)
                .FirstOrDefault();

            if (instance != null)
            {
                var records = _transactions
                    .Find(x => x.InstanceId == instance.InstanceId)
                    .OrderBy(x => x.SequenceNumber)
                    .ToList();
                instance.Transactions = records.Select(r => r.ToTransaction()).ToList();

                _instanceCache[lockKey] = instance;
                return Task.FromResult(instance);
            }

            instance = new SagaInstance
            {
                InstanceId = Guid.NewGuid(),
                SagaRef = sagaRef,
                OwnerAvatarId = null,
                InstanceType = SagaInstanceType.Multiplayer,
                CreatedAt = DateTime.UtcNow,
                CompositeKey = lockKey,
            };

            try
            {
                _instances.Insert(instance);
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
            {
                // Another caller registered it between check and insert — re-query.
                instance = _instances.Find(x => x.CompositeKey == lockKey).FirstOrDefault();
                if (instance == null)
                    throw;
            }

            _instanceCache[lockKey] = instance;
            return Task.FromResult(instance);
        }
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

                    // Project cross-arc state before commit so it's in the same transaction.
                    // Shared multiplayer instances have no owner — project per transaction
                    // author instead (tokens/reputation earned on shared arcs used to never
                    // reach AvatarQuestTokens, soft-locking cross-arc gating).
                    if (_avatarProgressRepository != null)
                    {
                        if (instance.OwnerAvatarId.HasValue)
                        {
                            _avatarProgressRepository.ProjectTransactions(
                                instance.OwnerAvatarId.Value,
                                instance.SagaRef,
                                transactions);
                        }
                        else
                        {
                            foreach (var authorGroup in transactions
                                         .Where(t => Guid.TryParse(t.AvatarId, out var authorId) && authorId != Guid.Empty)
                                         .GroupBy(t => Guid.Parse(t.AvatarId)))
                            {
                                _avatarProgressRepository.ProjectTransactions(
                                    authorGroup.Key,
                                    instance.SagaRef,
                                    authorGroup.ToList());
                            }
                        }
                    }

                    _database.Commit();
                    System.Diagnostics.Debug.WriteLine($"[SagaInstanceRepository] AddAndCommit OK: InstanceId={instanceId} wrote {transactions.Count} txs");

                    InvalidateInstanceCache(instanceId);
                    if (instance.OwnerAvatarId.HasValue)
                    {
                        InvalidateReadModelCache(instance.OwnerAvatarId.Value, instance.SagaRef);
                    }

                    return Task.FromResult((sequenceNumbers, true));
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SagaInstanceRepository] AddAndCommit INNER FAIL: InstanceId={instanceId} ex={innerEx.GetType().Name}: {innerEx.Message}");
                    _database.Rollback();
                    throw;
                }
            }
            catch (Exception outerEx)
            {
                System.Diagnostics.Debug.WriteLine($"[SagaInstanceRepository] AddAndCommit OUTER FAIL: InstanceId={instanceId} ex={outerEx.GetType().Name}: {outerEx.Message}");
                return Task.FromResult((new List<long>(), false));
            }
        }
    }

    public Task<int> ImportTransactionsAsync(Guid avatarId, Guid instanceId, List<SagaTransaction> transactions, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();
            if (instance == null)
                throw new InvalidOperationException($"Saga instance {instanceId} not found");

            _database.BeginTrans();

            try
            {
                var newlyInserted = new List<SagaTransaction>();

                foreach (var transaction in transactions)
                {
                    // Skip if already exists locally (idempotent — keeps non-idempotent
                    // projections like BossDefeats/FactionReputation from double-counting
                    // on overlapping pulls)
                    var existing = _transactions.FindOne(x => x.TransactionId == transaction.TransactionId);
                    if (existing != null)
                        continue;

                    // Preserve sequence number and status from server — do not reassign.
                    // Imported transactions are already server-side (often authored by
                    // OTHER avatars on shared arcs): they must never be pushed back, and
                    // push-rejection handling must never mark them Rejected.
                    transaction.SyncState = TransactionSyncState.Imported;
                    var record = SagaTransactionRecord.FromTransaction(transaction, instanceId);
                    _transactions.Insert(record);
                    newlyInserted.Add(transaction);
                }

                // Project only the newly-inserted transactions — matches the
                // AddAndCommitTransactionsAsync invariant so sync clients don't
                // have to remember to call ProjectTransactions themselves.
                // Attribution mirrors the write side: owner instances project to
                // the owner; shared multiplayer instances project per transaction
                // AUTHOR — never to the pulling avatar (peer rows pulled on shared
                // arcs used to be credited to the puller's cross-arc tables).
                if (newlyInserted.Count > 0 && _avatarProgressRepository != null)
                {
                    if (instance.OwnerAvatarId.HasValue)
                    {
                        _avatarProgressRepository.ProjectTransactions(
                            instance.OwnerAvatarId.Value,
                            instance.SagaRef,
                            newlyInserted);
                    }
                    else
                    {
                        foreach (var authorGroup in newlyInserted
                                     .Where(t => Guid.TryParse(t.AvatarId, out var authorId) && authorId != Guid.Empty)
                                     .GroupBy(t => Guid.Parse(t.AvatarId!)))
                        {
                            _avatarProgressRepository.ProjectTransactions(
                                authorGroup.Key,
                                instance.SagaRef,
                                authorGroup.ToList());
                        }
                    }
                }

                _database.Commit();

                if (newlyInserted.Count > 0)
                {
                    InvalidateInstanceCache(instanceId);
                    if (instance.OwnerAvatarId.HasValue)
                    {
                        InvalidateReadModelCache(instance.OwnerAvatarId.Value, instance.SagaRef);
                    }
                    else
                    {
                        foreach (var authorId in newlyInserted
                                     .Select(t => Guid.TryParse(t.AvatarId, out var id) ? id : Guid.Empty)
                                     .Where(id => id != Guid.Empty)
                                     .Distinct())
                        {
                            InvalidateReadModelCache(authorId, instance.SagaRef);
                        }
                        // The pulling avatar's replay of this shared arc changed too,
                        // even though nothing was projected to it.
                        if (avatarId != Guid.Empty)
                        {
                            InvalidateReadModelCache(avatarId, instance.SagaRef);
                        }
                    }
                }

                return Task.FromResult(newlyInserted.Count);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
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

    // NOTE: unlike AddAndCommitTransactionsAsync, the two-phase Add/Commit path
    // does NOT project cross-arc state. Its callers only write audit-trail claim
    // types that ProjectTransactions ignores; any type the projection handles
    // (quests, tokens, defeats, reputation, traits) must go through
    // AddAndCommitTransactionsAsync or its cross-arc effects are silently lost.
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

                // Pending→Committed changes what replay produces. Invalidate any cached
                // SagaInstance so the next GetOrCreate reloads from disk instead of
                // returning state derived before these transactions were committed.
                InvalidateInstanceCache(instanceId);

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
                    if (record != null && record.InstanceId == instanceId
                        && record.SyncState != TransactionSyncState.Imported)
                    {
                        // Imported transactions are server-confirmed peer history — a
                        // rejection can only ever apply to our own local optimism
                        record.Status = TransactionStatus.Rejected;
                        _transactions.Update(record);
                    }
                }

                _database.Commit();

                // Keep the in-memory view aligned with the DB even though replay output
                // is unchanged — consumers that read instance.Transactions directly would
                // otherwise see stale Pending entries.
                InvalidateInstanceCache(instanceId);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkTransactionsSyncedAsync(Guid instanceId, IReadOnlyCollection<Guid> transactionIds, DateTime? serverTimestamp = null, CancellationToken ct = default)
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
                    if (record == null || record.InstanceId != instanceId)
                        continue;

                    // Only local unsynced records move to Synced — Imported/LocalOnly
                    // records were never pushed and must stay as they are
                    if (record.SyncState != TransactionSyncState.LocalUnsynced)
                        continue;

                    record.SyncState = TransactionSyncState.Synced;
                    if (serverTimestamp.HasValue && record.ServerTimestamp == null)
                    {
                        // The sync endpoint stamps one serverNow per batch, so the batch
                        // timestamp IS the stored server timestamp for these records
                        record.ServerTimestamp = serverTimestamp.Value;
                    }
                    _transactions.Update(record);
                }

                _database.Commit();

                // SyncState doesn't change replay output, but cached instance.Transactions
                // must not keep serving stale LocalUnsynced entries to the next push
                InvalidateInstanceCache(instanceId);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task<List<SagaTransaction>> RejectAndCompensateTransactionsAsync(Guid instanceId, IReadOnlyList<TransactionRejection> rejections, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();
            if (instance == null)
                throw new InvalidOperationException($"Saga instance {instanceId} not found");

            var reversals = new List<SagaTransaction>();
            var reversedOriginals = new List<SagaTransaction>();

            _database.BeginTrans();

            try
            {
                var maxSequence = _transactions
                    .Find(x => x.InstanceId == instanceId)
                    .Select(x => x.SequenceNumber)
                    .DefaultIfEmpty(0)
                    .Max();

                foreach (var rejection in rejections)
                {
                    var record = _transactions.FindOne(x => x.TransactionId == rejection.TransactionId);
                    if (record == null || record.InstanceId != instanceId)
                        continue;

                    // Rejection handling applies ONLY to local optimism. Imported
                    // records are server-confirmed peer history (the inverse
                    // watermark race used to erase them from replay this way);
                    // already-rejected records make this call idempotent.
                    if (record.SyncState == TransactionSyncState.Imported ||
                        record.Status == TransactionStatus.Rejected)
                        continue;

                    record.Status = TransactionStatus.Rejected;
                    record.ReversalReason = rejection.Reason;
                    _transactions.Update(record);

                    var original = record.ToTransaction();
                    reversedOriginals.Add(original);

                    // Committed client-only compensation. In replay the Rejected original
                    // is already excluded (GetCommittedTransactions), so the reversal's
                    // fold finds no referenced transaction and skips quietly — it exists
                    // as the audit record and for reward-ledger consumers that count
                    // reversals directly (see DialogueActionExecutor).
                    var reversal = new SagaTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = SagaTransactionType.TransactionReversed,
                        AvatarId = original.AvatarId,
                        Status = TransactionStatus.Committed,
                        SyncState = TransactionSyncState.LocalOnly,
                        LocalTimestamp = DateTime.UtcNow,
                        SequenceNumber = ++maxSequence,
                        ReversesTransactionId = original.TransactionId,
                        ReversalReason = rejection.Reason,
                        Data = new Dictionary<string, string>
                        {
                            [TransactionDataKeys.ReversedTransactionId] = original.TransactionId.ToString(),
                            [TransactionDataKeys.Reason] = rejection.Reason ?? "Server rejected",
                            [TransactionDataKeys.OriginalType] = original.Type.ToString()
                        }
                    };

                    _transactions.Insert(SagaTransactionRecord.FromTransaction(reversal, instanceId));
                    reversals.Add(reversal);
                }

                // Undo cross-arc projections inside the same LiteDB transaction —
                // mirrors the per-author grouping AddAndCommitTransactionsAsync uses
                // for shared multiplayer instances
                if (reversedOriginals.Count > 0 && _avatarProgressRepository != null)
                {
                    if (instance.OwnerAvatarId.HasValue)
                    {
                        _avatarProgressRepository.ReverseTransactions(
                            instance.OwnerAvatarId.Value,
                            instance.SagaRef,
                            reversedOriginals);
                    }
                    else
                    {
                        foreach (var authorGroup in reversedOriginals
                                     .Where(t => Guid.TryParse(t.AvatarId, out var authorId) && authorId != Guid.Empty)
                                     .GroupBy(t => Guid.Parse(t.AvatarId!)))
                        {
                            _avatarProgressRepository.ReverseTransactions(
                                authorGroup.Key,
                                instance.SagaRef,
                                authorGroup.ToList());
                        }
                    }
                }

                _database.Commit();

                if (reversedOriginals.Count > 0)
                {
                    InvalidateInstanceCache(instanceId);
                    if (instance.OwnerAvatarId.HasValue)
                    {
                        InvalidateReadModelCache(instance.OwnerAvatarId.Value, instance.SagaRef);
                    }
                    else
                    {
                        foreach (var authorId in reversedOriginals
                                     .Select(t => Guid.TryParse(t.AvatarId, out var id) ? id : Guid.Empty)
                                     .Where(id => id != Guid.Empty)
                                     .Distinct())
                        {
                            InvalidateReadModelCache(authorId, instance.SagaRef);
                        }
                    }
                }

                return Task.FromResult(reversals);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }
    }

    public Task<List<SagaInstance>> GetAllInstancesForAvatarAsync(Guid avatarId, CancellationToken ct = default)
    {
        // Include shared multiplayer instances so push ships their transactions too.
        var instances = _instances
            .Find(x => x.OwnerAvatarId == avatarId || x.OwnerAvatarId == null)
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

    public Task UpdateSyncWatermarkAsync(Guid instanceId, long lastSyncedSequenceNumber, DateTime lastSyncedAt, DateTime? serverVersion, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();
            if (instance == null)
                return Task.CompletedTask;

            instance.LastSyncedSequenceNumber = lastSyncedSequenceNumber;
            instance.LastSyncedAt = lastSyncedAt;
            instance.ServerVersion = serverVersion;

            _instances.Update(instance);

            // Keep any cached copy in sync so the next GetOrCreate doesn't overwrite with
            // stale values. Use CompositeKey — hand-building "{OwnerAvatarId}|{SagaRef}"
            // rendered "|ref" for multiplayer (null owner) instead of the cached "NULL|ref",
            // so shared instances never received watermark updates and re-synced endlessly.
            if (_instanceCache.TryGetValue(instance.CompositeKey, out var cached))
            {
                cached.LastSyncedSequenceNumber = lastSyncedSequenceNumber;
                cached.LastSyncedAt = lastSyncedAt;
                cached.ServerVersion = serverVersion;
            }

            return Task.CompletedTask;
        }
    }

    public Task UpdatePullWatermarkAsync(Guid instanceId, DateTime lastPulledServerTimestamp, CancellationToken ct = default)
    {
        var lockKey = instanceId.ToString();
        var instanceLock = _instanceLocks.GetOrAdd(lockKey, _ => new object());

        lock (instanceLock)
        {
            var instance = _instances.Find(x => x.InstanceId == instanceId).FirstOrDefault();
            if (instance == null)
                return Task.CompletedTask;

            // Never move the watermark backwards
            if (instance.LastPulledServerTimestamp == null || lastPulledServerTimestamp > instance.LastPulledServerTimestamp)
            {
                instance.LastPulledServerTimestamp = lastPulledServerTimestamp;
                _instances.Update(instance);

                if (_instanceCache.TryGetValue(instance.CompositeKey, out var cached))
                {
                    cached.LastPulledServerTimestamp = lastPulledServerTimestamp;
                }
            }

            return Task.CompletedTask;
        }
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

    /// <summary>
    /// Drops the cached SagaState projection (if any) for this (avatarId, sagaRef).
    /// Handlers already invalidate after their own writes, but this covers paths like
    /// pull-sync imports that otherwise leave the read model serving pre-import state.
    /// Fire-and-forget — we're inside a sync method and the cache is in-process.
    /// </summary>
    private void InvalidateReadModelCache(Guid avatarId, string sagaRef)
    {
        _readModelRepository?.InvalidateCacheAsync(avatarId, sagaRef);
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
    public TransactionSyncState SyncState { get; set; } = TransactionSyncState.LocalUnsynced;
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
            SyncState = transaction.SyncState,
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
            SyncState = SyncState,
            Type = Type,
            ExtensionTypeName = ExtensionTypeName,
            Data = new Dictionary<string, string>(Data),
            ReversesTransactionId = ReversesTransactionId,
            ReversalReason = ReversalReason
        };
    }
}
