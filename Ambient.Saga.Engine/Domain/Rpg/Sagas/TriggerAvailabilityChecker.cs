using Ambient.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Checks if triggers are available to avatars based on quest token requirements.
/// Server and client use this same logic to determine trigger availability.
///
/// Usage:
/// - Check if trigger meets requirements: TriggerAvailabilityChecker.CanActivate(trigger, avatar)
/// - Get missing tokens for UI: TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, avatar)
/// </summary>
public static class TriggerAvailabilityChecker
{
    /// <summary>
    /// Checks if a trigger can activate for a given avatar based on quest token requirements.
    /// Returns true if the avatar has ALL required quest tokens, or if there are no requirements.
    /// </summary>
    /// <param name="sagaTrigger">The trigger to check</param>
    /// <param name="avatar">The avatar attempting to activate the trigger</param>
    /// <returns>True if the trigger is available, false if locked by missing quest tokens</returns>
    public static bool CanActivate(SagaTrigger sagaTrigger, AvatarBase avatar)
    {
        if (sagaTrigger == null)
            throw new ArgumentNullException(nameof(sagaTrigger));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        // No requirements = always available
        if (sagaTrigger.RequiresQuestTokenRef == null || sagaTrigger.RequiresQuestTokenRef.Length == 0)
            return true;

        // Avatar must have ALL required quest tokens
        if (avatar.Capabilities?.QuestTokens == null)
            return false; // No inventory = can't meet requirements

        // Check if avatar has each required token
        foreach (var requiredTokenRef in sagaTrigger.RequiresQuestTokenRef)
        {
            var hasToken = Array.Exists(avatar.Capabilities.QuestTokens,
                qt => qt.QuestTokenRef == requiredTokenRef);

            if (!hasToken)
                return false; // Missing at least one required token
        }

        return true; // Has all required tokens
    }

    /// <summary>
    /// Gets the list of missing quest tokens required to activate this trigger.
    /// Useful for UI to display what the player needs.
    /// </summary>
    /// <param name="sagaTrigger">The trigger to check</param>
    /// <param name="avatar">The avatar attempting to activate the trigger</param>
    /// <returns>Array of missing quest token RefNames, or empty array if none missing</returns>
    public static string[] GetMissingQuestTokens(SagaTrigger sagaTrigger, AvatarBase avatar)
    {
        if (sagaTrigger == null)
            throw new ArgumentNullException(nameof(sagaTrigger));

        if (avatar == null)
            throw new ArgumentNullException(nameof(avatar));

        // No requirements = nothing missing
        if (sagaTrigger.RequiresQuestTokenRef == null || sagaTrigger.RequiresQuestTokenRef.Length == 0)
            return Array.Empty<string>();

        var missing = new List<string>();
        var playerTokens = avatar.Capabilities?.QuestTokens;

        foreach (var requiredTokenRef in sagaTrigger.RequiresQuestTokenRef)
        {
            var hasToken = playerTokens != null &&
                Array.Exists(playerTokens, qt => qt.QuestTokenRef == requiredTokenRef);

            if (!hasToken)
                missing.Add(requiredTokenRef);
        }

        return missing.ToArray();
    }
}
