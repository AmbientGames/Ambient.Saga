using Ambient.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Reputation;

/// <summary>
/// Manages faction reputation calculations, level conversions, and spillover mechanics.
/// Implements WoW-style reputation system with 8 levels and relationship spillover.
/// </summary>
public static class ReputationManager
{
    /// <summary>
    /// Reputation level thresholds based on WoW system.
    /// </summary>
    private static readonly Dictionary<ReputationLevel, (int Min, int Max)> ReputationThresholds = new()
    {
        [ReputationLevel.Hated] = (-42000, -21000),
        [ReputationLevel.Hostile] = (-21000, -6000),
        [ReputationLevel.Unfriendly] = (-6000, -3000),
        [ReputationLevel.Neutral] = (-3000, 3000),
        [ReputationLevel.Friendly] = (3000, 9000),
        [ReputationLevel.Honored] = (9000, 21000),
        [ReputationLevel.Revered] = (21000, 42000),
        [ReputationLevel.Exalted] = (42000, int.MaxValue)
    };

    /// <summary>
    /// Converts numeric reputation value to reputation level.
    /// </summary>
    /// <param name="reputationValue">Numeric reputation (-42000 to +infinity)</param>
    /// <returns>Reputation level enum</returns>
    public static ReputationLevel GetReputationLevel(int reputationValue)
    {
        foreach (var (level, (min, max)) in ReputationThresholds)
        {
            if (reputationValue >= min && reputationValue < max)
                return level;
        }

        // If value is above all thresholds, return Exalted
        return ReputationLevel.Exalted;
    }

    /// <summary>
    /// Gets the numeric range for a given reputation level.
    /// </summary>
    /// <param name="level">Reputation level</param>
    /// <returns>Tuple of (MinValue, MaxValue) for the level</returns>
    public static (int Min, int Max) GetReputationRange(ReputationLevel level)
    {
        return ReputationThresholds[level];
    }

    /// <summary>
    /// Calculates spillover amount for a related faction.
    /// Allied factions get positive spillover, enemies get negative, rivals get none.
    /// </summary>
    /// <param name="sourceFaction">Faction gaining reputation</param>
    /// <param name="targetFactionRef">Faction to check for spillover</param>
    /// <param name="reputationGain">Amount of reputation gained with source faction</param>
    /// <returns>Spillover amount (positive for allies, negative for enemies, 0 for rivals/unrelated)</returns>
    public static int CalculateSpillover(Faction sourceFaction, string targetFactionRef, int reputationGain)
    {
        if (sourceFaction.Relationships == null || sourceFaction.Relationships.Length == 0)
            return 0;

        var relationship = sourceFaction.Relationships
            .FirstOrDefault(r => r.FactionRef == targetFactionRef);

        if (relationship == null)
            return 0;

        var spilloverPercent = relationship.SpilloverPercent;

        return relationship.RelationshipType switch
        {
            FactionRelationshipRelationshipType.Allied => (int)(reputationGain * spilloverPercent),
            FactionRelationshipRelationshipType.Enemy => (int)(-reputationGain * spilloverPercent),
            FactionRelationshipRelationshipType.Rival => 0,  // Rivals don't affect each other
            _ => 0
        };
    }

    /// <summary>
    /// Applies reputation change to a faction and calculates all spillover effects.
    /// </summary>
    /// <param name="currentReputation">Current reputation values (modified in-place)</param>
    /// <param name="allFactions">All factions in the world</param>
    /// <param name="targetFactionRef">Faction gaining reputation</param>
    /// <param name="reputationGain">Amount of reputation to gain</param>
    /// <returns>Dictionary of all reputation changes (including spillover)</returns>
    public static Dictionary<string, int> ApplyReputationChange(
        Dictionary<string, int> currentReputation,
        Dictionary<string, Faction> allFactions,
        string targetFactionRef,
        int reputationGain)
    {
        var changes = new Dictionary<string, int>();

        // Apply direct reputation gain
        if (!currentReputation.ContainsKey(targetFactionRef))
            currentReputation[targetFactionRef] = 0;

        currentReputation[targetFactionRef] += reputationGain;
        changes[targetFactionRef] = reputationGain;

        // Apply spillover to related factions
        if (allFactions.TryGetValue(targetFactionRef, out var sourceFaction))
        {
            foreach (var (factionRef, faction) in allFactions)
            {
                if (factionRef == targetFactionRef)
                    continue;  // Skip the source faction

                var spillover = CalculateSpillover(sourceFaction, factionRef, reputationGain);

                if (spillover != 0)
                {
                    if (!currentReputation.ContainsKey(factionRef))
                        currentReputation[factionRef] = 0;

                    currentReputation[factionRef] += spillover;
                    changes[factionRef] = spillover;
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// Gets the starting reputation value for a faction.
    /// </summary>
    /// <param name="faction">Faction to check</param>
    /// <returns>Starting reputation value (defaults to 0 / Neutral)</returns>
    public static int GetStartingReputation(Faction faction)
    {
        return faction.StartingReputation;
    }

    /// <summary>
    /// Gets all reputation rewards available at the current reputation level.
    /// </summary>
    /// <param name="faction">Faction to check</param>
    /// <param name="currentReputation">Current reputation value</param>
    /// <returns>Array of unlocked rewards</returns>
    public static ReputationReward[] GetAvailableRewards(Faction faction, int currentReputation)
    {
        if (faction.ReputationRewards == null || faction.ReputationRewards.Length == 0)
            return Array.Empty<ReputationReward>();

        var currentLevel = GetReputationLevel(currentReputation);
        var currentLevelValue = (int)currentLevel;

        return faction.ReputationRewards
            .Where(r => (int)r.RequiredLevel <= currentLevelValue)
            .ToArray();
    }

    /// <summary>
    /// Checks if a player has the required reputation level with a faction.
    /// Used for quest prerequisites, dialogue branching, etc.
    /// </summary>
    /// <param name="currentReputation">Current reputation value</param>
    /// <param name="requiredLevel">Required reputation level</param>
    /// <returns>True if reputation meets or exceeds required level</returns>
    public static bool MeetsReputationRequirement(int currentReputation, ReputationLevel requiredLevel)
    {
        var currentLevel = GetReputationLevel(currentReputation);
        return (int)currentLevel >= (int)requiredLevel;
    }
}
