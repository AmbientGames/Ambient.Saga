namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// An instance of a Saga with its transaction log.
/// Instance = Template (immutable XML) + Transaction Log (append-only) → Current State (derived)
/// </summary>
public class SagaInstance
{
    /// <summary>
    /// Unique identifier for this Saga instance.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the Saga template in the world definition.
    /// This is the immutable template - all state changes are in the transaction log.
    /// </summary>
    public string SagaRef { get; set; } = string.Empty;

    /// <summary>
    /// Type of instance (LocalOnly, SinglePlayer, Multiplayer).
    /// Determines sync and merge behavior.
    /// </summary>
    public SagaInstanceType InstanceType { get; set; } = SagaInstanceType.SinglePlayer;

    /// <summary>
    /// For SinglePlayer instances: the avatar that owns this instance.
    /// Null for LocalOnly and Multiplayer instances.
    /// </summary>
    public Guid? OwnerAvatarId { get; set; }

    /// <summary>
    /// Composite key for unique constraint on (OwnerAvatarId, SagaRef).
    /// Used by LiteDB to enforce database-level uniqueness.
    /// Format: "OwnerAvatarId|SagaRef" or "NULL|SagaRef" for shared instances.
    /// This is a stored field, not computed, because LiteDB can't index computed properties.
    /// Must be set whenever OwnerAvatarId or SagaRef changes.
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
    public List<SagaTransaction> Transactions { get; set; } = new();

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
    /// Whether this instance has pending (uncommitted) transactions.
    /// True if any transactions have Status = Pending.
    /// </summary>
    public bool HasPendingTransactions => Transactions.Any(t => t.Status == TransactionStatus.Pending);

    /// <summary>
    /// Gets the next sequence number for a new transaction.
    /// </summary>
    public long GetNextSequenceNumber() => Transactions.Any() ? Transactions.Max(t => t.SequenceNumber) + 1 : 1;

    /// <summary>
    /// Adds a new transaction to the log.
    /// </summary>
    public void AddTransaction(SagaTransaction transaction)
    {
        if (transaction.SequenceNumber == 0)
        {
            transaction.SequenceNumber = GetNextSequenceNumber();
        }

        Transactions.Add(transaction);
        LastModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets all committed transactions in order.
    /// </summary>
    public List<SagaTransaction> GetCommittedTransactions()
    {
        return Transactions
            .Where(t => t.Status == TransactionStatus.Committed)
            .OrderBy(t => t.SequenceNumber)
            .ToList();
    }
}
