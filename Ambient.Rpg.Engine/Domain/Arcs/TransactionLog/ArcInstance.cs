namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// An instance of an arc with its transaction log.
/// Instance = Template (immutable XML) + Transaction Log (append-only) → Current State (derived)
/// </summary>
public class ArcInstance
{
    /// <summary>
    /// Unique identifier for this arc instance.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the arc template in the world definition.
    /// This is the immutable template - all state changes are in the transaction log.
    /// </summary>
    public string ArcRef { get; set; } = string.Empty;

    /// <summary>
    /// Type of instance (LocalOnly, SinglePlayer, Multiplayer).
    /// Determines sync and merge behavior.
    /// </summary>
    public ArcInstanceType InstanceType { get; set; } = ArcInstanceType.SinglePlayer;

    /// <summary>
    /// For SinglePlayer instances: the avatar that owns this instance.
    /// Null for LocalOnly and Multiplayer instances.
    /// </summary>
    public Guid? OwnerAvatarId { get; set; }

    /// <summary>
    /// Composite key for unique constraint on (OwnerAvatarId, ArcRef).
    /// Used by LiteDB to enforce database-level uniqueness.
    /// Format: "OwnerAvatarId|ArcRef" or "NULL|ArcRef" for shared instances.
    /// This is a stored field, not computed, because LiteDB can't index computed properties.
    /// Must be set whenever OwnerAvatarId or ArcRef changes.
    /// </summary>
    public string CompositeKey { get; set; } = string.Empty;

    /// <summary>
    /// For Multiplayer instances: all avatars participating in this instance.
    /// Empty for LocalOnly and SinglePlayer instances.
    /// </summary>
    public List<Guid> ParticipantAvatarIds { get; set; } = new();

    /// <summary>
    /// The transaction log - append-only list of all state changes.
    /// This is the source of truth.
    /// </summary>
    public List<ArcTransaction> Transactions { get; set; } = new();

    /// <summary>
    /// When this instance was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this instance was last modified (last transaction added).
    /// </summary>
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this instance was last synced to the server.
    /// Null if never synced or LocalOnly.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Server version/timestamp of last sync.
    /// Used for conflict detection during merge.
    /// </summary>
    public DateTime? ServerVersion { get; set; }

    /// <summary>
    /// The highest sequence number confirmed by the server.
    /// Transactions with SequenceNumber > this value have not been synced yet.
    /// 0 means nothing has been synced.
    /// </summary>
    public long LastSyncedSequenceNumber { get; set; }

    /// <summary>
    /// Max server-clock timestamp of transactions imported via pull.
    /// Sent back as the pull watermark — server timestamps are globally ordered,
    /// unlike client-local sequence numbers which collide across avatars on
    /// shared arcs. Null if nothing has been pulled yet.
    /// </summary>
    public DateTime? LastPulledServerTimestamp { get; set; }

    /// <summary>
    /// Whether this instance has pending (uncommitted) transactions.
    /// True if any transactions have Status = Pending.
    /// </summary>
    public bool HasPendingTransactions => Transactions.Any(t => t.Status == TransactionStatus.Pending);

    /// <summary>
    /// Gets the next sequence number for a new transaction.
    /// </summary>
    public long GetNextSequenceNumber() => Transactions.Any() ? Transactions.Max(t => t.SequenceNumber) + 1 : 1;

    /// <summary>
    /// Cached replay state. Invalidated when transactions are added.
    /// </summary>
    [LiteDB.BsonIgnore]
    public ArcState? CachedState { get; set; }

    /// <summary>
    /// The highest committed sequence number that CachedState was derived from.
    /// Used to detect staleness when reloading transactions from the database.
    /// -1 means no replay has been performed yet.
    /// </summary>
    [LiteDB.BsonIgnore]
    public long CachedAtSequenceNumber { get; set; } = -1;

    /// <summary>
    /// True when the transaction log has changed since the last replay.
    /// </summary>
    [LiteDB.BsonIgnore]
    public bool IsDirty { get; set; } = true;

    /// <summary>
    /// Guards mutations of <see cref="Transactions"/>. Cached instances are shared
    /// across concurrent command handlers; unsynchronized List.Add from two racing
    /// commands corrupts the log (audit H4 aggravator). Mutations are copy-on-write:
    /// readers that captured the list reference always enumerate a stable snapshot.
    /// (Private field — never serialized by LiteDB, which maps public properties only.)
    /// </summary>
    private readonly object _transactionsLock = new();

    /// <summary>
    /// Adds a new transaction to the log. Thread-safe (copy-on-write): concurrent
    /// readers of <see cref="Transactions"/> keep enumerating their own snapshot.
    /// </summary>
    public void AddTransaction(ArcTransaction transaction)
    {
        lock (_transactionsLock)
        {
            if (transaction.SequenceNumber == 0)
            {
                transaction.SequenceNumber = GetNextSequenceNumber();
            }

            var next = new List<ArcTransaction>(Transactions) { transaction };
            Transactions = next;
            LastModifiedAt = DateTime.UtcNow;
            IsDirty = true;
        }
    }

    /// <summary>
    /// Removes transactions from the in-memory log (copy-on-write, thread-safe).
    /// Used by the repository to un-stage transactions whose durable commit FAILED —
    /// leaving them in the shared cached instance created phantom entries that
    /// suppressed first-visit rewards and polluted stock replay (audit H5).
    /// </summary>
    public void RemoveTransactions(IEnumerable<Guid> transactionIds)
    {
        var ids = transactionIds as HashSet<Guid> ?? new HashSet<Guid>(transactionIds);
        if (ids.Count == 0)
            return;

        lock (_transactionsLock)
        {
            var next = Transactions.Where(t => !ids.Contains(t.TransactionId)).ToList();
            Transactions = next;
            LastModifiedAt = DateTime.UtcNow;
            IsDirty = true;
        }
    }

    /// <summary>
    /// Gets all committed transactions in order.
    /// </summary>
    public List<ArcTransaction> GetCommittedTransactions()
    {
        return Transactions
            .Where(t => t.Status == TransactionStatus.Committed)
            .OrderBy(t => t.SequenceNumber)
            .ToList();
    }
}
