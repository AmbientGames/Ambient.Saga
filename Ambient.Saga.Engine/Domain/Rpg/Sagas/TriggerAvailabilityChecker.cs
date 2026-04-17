using Ambient.Domain;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Checks if triggers are available based on the avatar's awarded quest tokens.
/// </summary>
public static class TriggerAvailabilityChecker
{
    /// <summary>
    /// Checks trigger availability against the avatar progress table.
    /// </summary>
    public static bool CanActivate(SagaTrigger sagaTrigger, IAvatarProgressRepository progressRepo, Guid avatarId)
    {
        if (sagaTrigger == null)
            throw new ArgumentNullException(nameof(sagaTrigger));

        if (sagaTrigger.RequiresQuestTokenRef == null || sagaTrigger.RequiresQuestTokenRef.Length == 0)
            return true;

        foreach (var requiredTokenRef in sagaTrigger.RequiresQuestTokenRef)
        {
            if (!progressRepo.HasQuestToken(avatarId, requiredTokenRef))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Legacy overload that reads from SagaState. Kept for backward compatibility with tests.
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
