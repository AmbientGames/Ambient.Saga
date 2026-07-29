namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// Overall status of an arc instance.
/// </summary>
public enum ArcStatus
{
    /// <summary>
    /// Arc has not been discovered yet.
    /// No transactions have occurred.
    /// </summary>
    Undiscovered,

    /// <summary>
    /// Arc has been discovered and is active.
    /// Avatar can interact with triggers and entities.
    /// </summary>
    Active,

    /// <summary>
    /// All objectives completed, but Arc can still be visited.
    /// Example: Boss defeated but chest can still be looted
    /// </summary>
    Completed,

    /// <summary>
    /// Arc is exhausted and cannot be interacted with.
    /// Example: One-time chest already looted
    /// </summary>
    Exhausted
}
