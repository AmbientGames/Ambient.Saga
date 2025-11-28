namespace Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Status of a transaction in the log.
/// Used for optimistic concurrency and conflict resolution.
/// </summary>
public enum TransactionStatus
{
    /// <summary>
    /// Created locally, not yet confirmed by server (optimistic).
    /// May be rolled back if server rejects.
    /// </summary>
    Pending,

    /// <summary>
    /// Confirmed by server (or local-only committed).
    /// This is canonical truth for the player's instance.
    /// </summary>
    Committed,

    /// <summary>
    /// Server rejected this transaction.
    /// Client should discard predicted state.
    /// </summary>
    Rejected,

    /// <summary>
    /// Compensating transaction created to undo this transaction.
    /// Used for conflict resolution and rollback.
    /// </summary>
    Reversed
}
