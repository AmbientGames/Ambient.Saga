namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// Where a transaction stands relative to the server, tracked PER TRANSACTION.
///
/// Push selection used to be a per-instance sequence watermark
/// (LastSyncedSequenceNumber), but on shared multiplayer arcs pulled
/// transactions carry OTHER avatars' client-local sequence numbers, which
/// collide with ours: a pull could raise the watermark past a local
/// not-yet-pushed transaction (it then never pushed — lost owner revenue),
/// and the inverse race re-sent pulled foreign transactions, which the
/// server rejected and the client then wrongly marked Rejected locally.
/// Per-transaction state makes both races impossible.
/// </summary>
public enum TransactionSyncState
{
    /// <summary>
    /// Created locally and not yet acknowledged by the server.
    /// This is the default (enum value 0) so records persisted before this
    /// field existed deserialize as unsynced and simply re-push — the server
    /// dedups by TransactionId, so a re-push is idempotent.
    /// </summary>
    LocalUnsynced = 0,

    /// <summary>
    /// Pushed to the server and acknowledged (in CommittedTransactionIds).
    /// Never pushed again.
    /// </summary>
    Synced = 1,

    /// <summary>
    /// Arrived via pull — the server already has it (possibly authored by
    /// another avatar on a shared arc). Never pushed, and never marked
    /// Rejected by push-rejection handling.
    /// </summary>
    Imported = 2,

    /// <summary>
    /// Client-side bookkeeping the server must never see — e.g. the
    /// compensating TransactionReversed written when the server rejects a
    /// local transaction (the original never landed server-side, so the
    /// compensation pair stays client-only). Never pushed.
    /// </summary>
    LocalOnly = 3
}
