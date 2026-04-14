using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Checks if triggers are available based on the awarded quest tokens in a replayed SagaState.
/// </summary>
public static class TriggerAvailabilityChecker
{
    /// <summary>
    /// State-based availability check. Reads tokens from the replayed SagaState rather than the avatar,
    /// so availability derives from the saga's transaction log (the source of truth).
    /// </summary>
    public static bool CanActivate(SagaTrigger sagaTrigger, SagaState state)
    {
        if (sagaTrigger == null)
            throw new ArgumentNullException(nameof(sagaTrigger));

        if (state == null)
            throw new ArgumentNullException(nameof(state));

        if (sagaTrigger.RequiresQuestTokenRef == null || sagaTrigger.RequiresQuestTokenRef.Length == 0)
            return true;

        foreach (var requiredTokenRef in sagaTrigger.RequiresQuestTokenRef)
        {
            if (!state.AwardedQuestTokens.Contains(requiredTokenRef))
                return false;
        }

        return true;
    }
}
