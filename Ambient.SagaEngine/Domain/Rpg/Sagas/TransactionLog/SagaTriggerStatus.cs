namespace Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Status of a trigger within a Saga instance.
/// </summary>
public enum SagaTriggerStatus
{
    /// <summary>
    /// Trigger exists but has not been activated yet.
    /// Waiting for player to enter radius.
    /// </summary>
    Inactive,

    /// <summary>
    /// Trigger is currently active.
    /// Characters may be spawned, effects applied.
    /// </summary>
    Active,

    /// <summary>
    /// Trigger is on cooldown after being activated.
    /// Cannot be triggered again until cooldown expires.
    /// </summary>
    OnCooldown,

    /// <summary>
    /// Trigger has been completed and cannot activate again.
    /// Used for one-time triggers or progression-gated sequences.
    /// </summary>
    Completed
}
