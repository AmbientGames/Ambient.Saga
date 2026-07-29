using Ambient.Domain;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Domain.Arcs;

/// <summary>
/// Checks if triggers are available based on the avatar's awarded quest tokens.
/// </summary>
public static class TriggerAvailabilityChecker
{
    /// <summary>
    /// Checks trigger availability against the avatar progress table.
    /// </summary>
    public static bool CanActivate(ArcTrigger arcTrigger, IAvatarProgressRepository progressRepo, Guid avatarId)
    {
        if (arcTrigger == null)
            throw new ArgumentNullException(nameof(arcTrigger));

        if (arcTrigger.RequiresQuestTokenRef == null || arcTrigger.RequiresQuestTokenRef.Length == 0)
            return true;

        foreach (var requiredTokenRef in arcTrigger.RequiresQuestTokenRef)
        {
            if (!progressRepo.HasQuestToken(avatarId, requiredTokenRef))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Legacy overload that reads from ArcState. Kept for backward compatibility with tests.
    /// </summary>
    public static bool CanActivate(ArcTrigger arcTrigger, ArcState state)
    {
        if (arcTrigger == null)
            throw new ArgumentNullException(nameof(arcTrigger));

        if (state == null)
            throw new ArgumentNullException(nameof(state));

        if (arcTrigger.RequiresQuestTokenRef == null || arcTrigger.RequiresQuestTokenRef.Length == 0)
            return true;

        foreach (var requiredTokenRef in arcTrigger.RequiresQuestTokenRef)
        {
            if (!state.AwardedQuestTokens.Contains(requiredTokenRef))
                return false;
        }

        return true;
    }
}
