namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Determines how a Saga instance is managed and synchronized.
/// </summary>
public enum SagaInstanceType
{
    /// <summary>
    /// Player's private instance, never syncs to server.
    /// Pure offline mode - state lives only on this device.
    /// Example: Solo exploration in offline mode
    /// </summary>
    LocalOnly,

    /// <summary>
    /// Player's instance that syncs progress to server but is not shared.
    /// Other players see their own separate instances of the same Saga.
    /// Example: Personal quest progression, solo dungeon runs
    /// </summary>
    SinglePlayer,

    /// <summary>
    /// Shared instance where multiple players interact together.
    /// Transactions from all players are merged and synchronized.
    /// Example: Guild boss fight, shared world events
    /// </summary>
    Multiplayer
}
