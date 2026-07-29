namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// A single transaction in an arc instance's event log.
/// Represents an atomic state change that can be replayed.
/// Inspired by banking transaction logs - immutable, auditable, replayable.
/// </summary>
public class ArcTransaction
{
    // ===== Identity =====

    /// <summary>
    /// Unique identifier for this transaction.
    /// </summary>
    public Guid TransactionId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Sequence number within this arc instance's log.
    /// Monotonically increasing - used for ordering.
    /// </summary>
    public long SequenceNumber { get; set; }

    // ===== Source =====

    /// <summary>
    /// Avatar ID that caused this transaction (if avatar-initiated).
    /// Null for system-initiated transactions (spawns, timeouts).
    /// </summary>
    public string? AvatarId { get; set; }

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

    /// <summary>
    /// Where this transaction stands relative to the server (see
    /// <see cref="TransactionSyncState"/>). Push selection is driven by this
    /// per-transaction state, never by per-instance sequence watermarks —
    /// sequence numbers collide across avatars on shared arcs.
    /// </summary>
    public TransactionSyncState SyncState { get; set; } = TransactionSyncState.LocalUnsynced;

    // ===== Content =====

    /// <summary>
    /// Type of transaction.
    /// </summary>
    public ArcTransactionType Type { get; set; }

    /// <summary>
    /// Extension type name when Type = Extension.
    /// Allows domain-specific packages (e.g., the host game) to define custom transaction types
    /// without modifying the base ArcTransactionType enum.
    /// Examples: "LocationClaimed", "MiningSessionClaimed", "ProcessingCycleCompleted"
    /// </summary>
    public string? ExtensionTypeName { get; set; }

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

        // Try to convert (invariant culture: transaction data is written invariant by
        // SetData — current-culture parsing corrupted floats when a save crossed locales)
        try
        {
            return (T)Convert.ChangeType(NormalizeNumeric<T>(value), typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Normalizes legacy culture-formatted numerics before invariant parsing. Values
    /// written before the invariant-culture fix used the machine culture ("35,5" on
    /// comma-decimal locales); SetData never writes group separators, so a lone comma
    /// is always a decimal separator.
    /// </summary>
    private static string NormalizeNumeric<T>(string value)
    {
        if ((typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(decimal)) &&
            value.Contains(',') && !value.Contains('.'))
        {
            return value.Replace(',', '.');
        }
        return value;
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

        // Try to convert (invariant culture, legacy comma-decimals normalized — see GetData)
        try
        {
            value = (T)Convert.ChangeType(NormalizeNumeric<T>(stringValue), typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a value in the Data dictionary (invariant culture — the value must
    /// round-trip through GetData regardless of the machine's locale).
    /// </summary>
    public void SetData<T>(string key, T value)
    {
        if (value != null)
        {
            Data[key] = value is IFormattable formattable
                ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Returns the canonical timestamp (server if available, local otherwise).
    /// </summary>
    public DateTime GetCanonicalTimestamp() => ServerTimestamp ?? LocalTimestamp;
}
