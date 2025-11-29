namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// A single transaction in a Saga instance's event log.
/// Represents an atomic state change that can be replayed.
/// Inspired by banking transaction logs - immutable, auditable, replayable.
/// </summary>
public class SagaTransaction
{
    // ===== Identity =====

    /// <summary>
    /// Unique identifier for this transaction.
    /// </summary>
    public Guid TransactionId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Sequence number within this Saga instance's log.
    /// Monotonically increasing - used for ordering.
    /// </summary>
    public long SequenceNumber { get; set; }

    // ===== Source =====

    /// <summary>
    /// Avatar ID that caused this transaction (if player-initiated).
    /// Null for system-initiated transactions (spawns, timeouts).
    /// </summary>
    public string? AvatarId { get; set; }

    /// <summary>
    /// Client/device ID that created this transaction.
    /// Used for conflict resolution and debugging.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    // ===== Timing =====

    /// <summary>
    /// When this transaction was created on the client.
    /// Uses client's clock - may not match server time.
    /// </summary>
    public DateTime LocalTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this transaction was confirmed by the server.
    /// Null for local-only or pending transactions.
    /// This is the canonical timestamp for ordering.
    /// </summary>
    public DateTime? ServerTimestamp { get; set; }

    // ===== State =====

    /// <summary>
    /// Current status of this transaction.
    /// </summary>
    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

    // ===== Content =====

    /// <summary>
    /// Type of transaction.
    /// </summary>
    public SagaTransactionType Type { get; set; }

    /// <summary>
    /// Transaction-specific data (JSON-serialized).
    /// Structure depends on Type.
    /// Examples:
    /// - CharacterSpawned: { CharacterRef, TriggerRef, CharacterInstanceId }
    /// - CharacterDamaged: { CharacterInstanceId, Damage, AvatarId }
    /// - TriggerActivated: { TriggerRef, AvatarId }
    /// </summary>
    public Dictionary<string, string> Data { get; set; } = new();

    // ===== Reconciliation =====

    /// <summary>
    /// If this is a compensating transaction (reversal), reference to the original.
    /// Used for rollback and conflict resolution.
    /// </summary>
    public Guid? ReversesTransactionId { get; set; }

    /// <summary>
    /// Reason for rejection or reversal.
    /// Example: "Server rejected: boss already dead" or "Conflict with concurrent action"
    /// </summary>
    public string? ReversalReason { get; set; }

    /// <summary>
    /// How conflicts were resolved during merge.
    /// Example: "Server wins", "Timestamp ordering", "Replayed after server"
    /// </summary>
    public string? MergeStrategy { get; set; }

    // ===== Helper Methods =====

    /// <summary>
    /// Gets a value from the Data dictionary with type conversion.
    /// </summary>
    public T? GetData<T>(string key)
    {
        if (!Data.TryGetValue(key, out var value))
            return default;

        if (value is T typedValue)
            return typedValue;

        // Special handling for Guid (LiteDB stores as string)
        if (typeof(T) == typeof(Guid) && value is string guidString)
        {
            if (Guid.TryParse(guidString, out var guid))
                return (T)(object)guid;
            return default;
        }

        // Try to convert
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Tries to get a value from the Data dictionary with type conversion.
    /// Returns true if key exists and can be converted, false otherwise.
    /// </summary>
    public bool TryGetData<T>(string key, out T? value)
    {
        value = default;

        if (!Data.TryGetValue(key, out var stringValue))
            return false;

        if (stringValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        // Special handling for Guid (LiteDB stores as string)
        if (typeof(T) == typeof(Guid) && stringValue is string guidString)
        {
            if (Guid.TryParse(guidString, out var guid))
            {
                value = (T)(object)guid;
                return true;
            }
            return false;
        }

        // Try to convert
        try
        {
            value = (T)Convert.ChangeType(stringValue, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a value in the Data dictionary.
    /// </summary>
    public void SetData<T>(string key, T value)
    {
        if (value != null)
        {
            Data[key] = value.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Returns the canonical timestamp (server if available, local otherwise).
    /// </summary>
    public DateTime GetCanonicalTimestamp() => ServerTimestamp ?? LocalTimestamp;
}
