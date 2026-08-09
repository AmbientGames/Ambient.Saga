namespace Ambient.Rpg.Engine.Contracts.Services;

/// <summary>
/// Raised by <see cref="IAvatarUpdateService"/> whenever an arc transaction mutates
/// <c>avatar.Stats.Credits</c>. Intended for hosts that need to forward the change
/// to an authoritative server so the server-side balance stays in sync with Arc's
/// local view.
/// </summary>
/// <param name="AvatarId">ID of the avatar whose credits changed.</param>
/// <param name="Delta">Signed credit delta: positive = earned, negative = spent.</param>
/// <param name="Reason">Transaction type that triggered the change — <c>"Trade"</c>,
/// <c>"Loot"</c>, or <c>"Effect"</c>. Identifies the source for auditing.</param>
/// <param name="TransactionId">Arc TransactionId. Host should pass this to the
/// server as an idempotency key so double-applies are impossible if the event
/// fires more than once or the sync path later carries the same transaction.</param>
public record CreditChangeNotification(
    Guid AvatarId,
    int Delta,
    string Reason,
    Guid TransactionId);
