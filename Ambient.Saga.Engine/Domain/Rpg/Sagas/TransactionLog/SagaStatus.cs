namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Overall status of a Saga instance.
/// </summary>
public enum SagaStatus
{
    /// <summary>
    /// Saga has not been discovered yet.
    /// No transactions have occurred.
    /// </summary>
    Undiscovered,

    /// <summary>
    /// Saga has been discovered and is active.
    /// Player can interact with triggers and entities.
    /// </summary>
    Active,

    /// <summary>
    /// All objectives completed, but Saga can still be visited.
    /// Example: Boss defeated but chest can still be looted
    /// </summary>
    Completed,

    /// <summary>
    /// Saga is exhausted and cannot be interacted with.
    /// Example: One-time chest already looted
    /// </summary>
    Exhausted
}
