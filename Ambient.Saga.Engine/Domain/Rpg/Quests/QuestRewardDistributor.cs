using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Domain.Extensions;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;

namespace Ambient.Saga.Engine.Domain.Rpg.Quests;

/// <summary>
/// Handles distribution of quest rewards to avatar inventory and stats.
/// Supports all reward types: Currency, Equipment, Consumable, QuestToken, Experience, Reputation.
/// </summary>
public static class QuestRewardDistributor
{
    /// <summary>
    /// Distribute a single quest reward to the avatar.
    /// Updates avatar's inventory, stats, and capabilities based on reward type.
    /// </summary>
    public static void DistributeReward(QuestReward reward, AvatarEntity avatar, IWorld world)
    {
        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Initialize Stats if needed
        if (avatar.Stats == null)
        {
            avatar.Stats = new CharacterStats();
        }

        // Award Currency
        if (reward.Currency != null)
        {
            avatar.Stats.Credits += reward.Currency.Amount;
        }

        // Award Experience
        if (reward.Experience != null)
        {
            avatar.Stats.Experience += reward.Experience.Amount;
        }

        // Award Equipment
        if (reward.Equipment != null && reward.Equipment.Length > 0)
        {
            foreach (var equipmentReward in reward.Equipment)
            {
                var equipment = avatar.Capabilities.GetOrAddEquipment(equipmentReward.EquipmentRef);
                // New equipment starts at full condition
                equipment.Condition = 1.0f;

                // NOTE: Quantity is ignored for equipment (can't have "2 swords" of same type)
                // Each equipment reward adds the item if not present, or does nothing if already owned
            }
        }

        // Award Consumables
        if (reward.Consumable != null && reward.Consumable.Length > 0)
        {
            foreach (var consumableReward in reward.Consumable)
            {
                var consumable = avatar.Capabilities.GetOrAddConsumable(consumableReward.ConsumableRef);
                consumable.Quantity += consumableReward.Quantity;
            }
        }

        // Award Quest Tokens
        if (reward.QuestToken != null && reward.QuestToken.Length > 0)
        {
            foreach (var questTokenReward in reward.QuestToken)
            {
                // Add quest token multiple times if quantity > 1
                for (var i = 0; i < questTokenReward.Quantity; i++)
                {
                    avatar.Capabilities.AddQuestToken(questTokenReward.QuestTokenRef);
                }
            }
        }

        // Reputation rewards - TODO: Implement faction system
        // For now, log that reputation rewards are not yet supported
        if (reward.Reputation != null && reward.Reputation.Length > 0)
        {
            // NOTE: Reputation system not yet implemented
            // Will need FactionReputation tracking in avatar capabilities
        }

        // Achievement rewards - TODO: Achievement unlocking
        // Achievements are auto-unlocked via transaction log, no manual award needed
        if (reward.Achievement != null && reward.Achievement.Length > 0)
        {
            // NOTE: Achievements are event-sourced from transaction log
            // No direct avatar state change needed here
        }
    }

    /// <summary>
    /// Distribute all rewards that match the given condition.
    /// </summary>
    /// <param name="rewards">Array of quest rewards to filter and distribute</param>
    /// <param name="condition">The condition to match (OnSuccess, OnFailure, OnBranch, OnObjective)</param>
    /// <param name="avatar">The avatar to receive rewards</param>
    /// <param name="world">World containing catalog definitions</param>
    /// <param name="branchRef">Optional branch reference for OnBranch rewards</param>
    /// <param name="objectiveRef">Optional objective reference for OnObjective rewards</param>
    public static void DistributeRewards(
        QuestReward[]? rewards,
        QuestRewardCondition condition,
        AvatarEntity avatar,
        IWorld world,
        string? branchRef = null,
        string? objectiveRef = null)
    {
        if (rewards == null || rewards.Length == 0)
            return;

        foreach (var reward in rewards)
        {
            // Check if reward matches condition
            if (reward.Condition != condition)
                continue;

            // For OnBranch rewards, check BranchRef matches
            if (condition == QuestRewardCondition.OnBranch)
            {
                if (string.IsNullOrEmpty(branchRef) || reward.BranchRef != branchRef)
                    continue;
            }

            // For OnObjective rewards, check ObjectiveRef matches
            if (condition == QuestRewardCondition.OnObjective)
            {
                if (string.IsNullOrEmpty(objectiveRef) || reward.ObjectiveRef != objectiveRef)
                    continue;
            }

            // Distribute the reward
            DistributeReward(reward, avatar, world);
        }
    }

    /// <summary>
    /// Check if an avatar meets all quest prerequisites.
    /// Returns (canAccept, reason).
    /// </summary>
    /// <param name="quest">Quest to check prerequisites for</param>
    /// <param name="avatar">Avatar attempting to accept the quest</param>
    /// <param name="world">World containing factions and other definitions</param>
    /// <param name="completedQuests">Set of completed quest refs</param>
    /// <param name="factionReputation">Dictionary of faction reputation values (optional)</param>
    /// <param name="unlockedAchievements">Set of unlocked achievement refs (optional)</param>
    public static (bool canAccept, string? reason) CheckPrerequisites(
        Quest quest,
        AvatarEntity avatar,
        IWorld world,
        HashSet<string> completedQuests,
        Dictionary<string, int>? factionReputation = null,
        HashSet<string>? unlockedAchievements = null)
    {
        if (quest.Prerequisites == null || quest.Prerequisites.Length == 0)
            return (true, null);

        foreach (var prereq in quest.Prerequisites)
        {
            // Check previous quest completion
            if (!string.IsNullOrEmpty(prereq.QuestRef))
            {
                if (!completedQuests.Contains(prereq.QuestRef))
                {
                    var requiredQuest = world.TryGetQuestByRefName(prereq.QuestRef);
                    var questName = requiredQuest?.DisplayName ?? prereq.QuestRef;
                    return (false, $"Must complete quest '{questName}' first");
                }
            }

            // Check minimum level (use MinimumLevelSpecified for proper XSD alignment)
            if (prereq.MinimumLevelSpecified && prereq.MinimumLevel > 0)
            {
                var avatarLevel = avatar.Stats?.Level ?? 1;
                if (avatarLevel < prereq.MinimumLevel)
                {
                    return (false, $"Must be level {prereq.MinimumLevel} or higher");
                }
            }

            // Check required item
            if (!string.IsNullOrEmpty(prereq.RequiredItemRef))
            {
                var hasItem = false;

                // Check in equipment
                if (avatar.Capabilities?.Equipment != null)
                {
                    hasItem = avatar.Capabilities.Equipment.Any(e => e.EquipmentRef == prereq.RequiredItemRef);
                }

                // Check in consumables
                if (!hasItem && avatar.Capabilities?.Consumables != null)
                {
                    hasItem = avatar.Capabilities.Consumables.Any(c => c.ConsumableRef == prereq.RequiredItemRef && c.Quantity > 0);
                }

                // Check in quest tokens
                if (!hasItem && avatar.Capabilities?.QuestTokens != null)
                {
                    hasItem = avatar.Capabilities.QuestTokens.Any(q => q.QuestTokenRef == prereq.RequiredItemRef);
                }

                if (!hasItem)
                {
                    return (false, $"Must have item '{prereq.RequiredItemRef}'");
                }
            }

            // Check achievement prerequisite
            if (!string.IsNullOrEmpty(prereq.RequiredAchievementRef))
            {
                var hasAchievement = unlockedAchievements?.Contains(prereq.RequiredAchievementRef) ?? false;
                if (!hasAchievement)
                {
                    // Try to get display name from world
                    var achievementName = prereq.RequiredAchievementRef;
                    if (world.AchievementsLookup.TryGetValue(prereq.RequiredAchievementRef, out var achievement))
                    {
                        achievementName = achievement.DisplayName ?? prereq.RequiredAchievementRef;
                    }
                    return (false, $"Must unlock achievement '{achievementName}'");
                }
            }

            // Check faction reputation prerequisite
            if (!string.IsNullOrEmpty(prereq.FactionRef) && !string.IsNullOrEmpty(prereq.RequiredReputationLevel))
            {
                // Parse the required reputation level
                if (!Enum.TryParse<ReputationLevel>(prereq.RequiredReputationLevel, ignoreCase: true, out var requiredLevel))
                {
                    // Invalid reputation level specified - log and skip check
                    continue;
                }

                // Get current reputation with faction (default to 0 / Neutral)
                var currentReputation = factionReputation?.GetValueOrDefault(prereq.FactionRef, 0) ?? 0;

                if (!ReputationManager.MeetsReputationRequirement(currentReputation, requiredLevel))
                {
                    // Try to get faction display name
                    var factionName = prereq.FactionRef;
                    if (world.FactionsLookup.TryGetValue(prereq.FactionRef, out var faction))
                    {
                        factionName = faction.DisplayName ?? prereq.FactionRef;
                    }
                    return (false, $"Must have {requiredLevel} reputation with {factionName}");
                }
            }
        }

        return (true, null);
    }
}
