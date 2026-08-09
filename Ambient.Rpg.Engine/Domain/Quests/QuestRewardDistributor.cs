using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.Extensions;
using Ambient.Rpg.Engine.Domain.Dialogue;
using Ambient.Rpg.Engine.Domain.Reputation;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Domain.Quests;

/// <summary>
/// Handles distribution of quest rewards.
/// Avatar-state rewards (Currency, Experience, Equipment, Consumable, Achievement) are
/// applied directly to the avatar via <see cref="DistributeRewards"/>. Reputation rewards
/// are transaction-driven (replayed arc state + cross-arc projection, exactly like the
/// dialogue ChangeReputation action): callers collect them via
/// <see cref="CollectRewardTransactions"/> and commit them atomically with the
/// transaction that caused the reward (QuestCompleted / QuestStageAdvanced /
/// QuestObjectiveCompleted).
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

        // Award Experience (levels derive from accumulated XP; never demote)
        if (reward.Experience != null)
        {
            avatar.Stats.Experience += reward.Experience.Amount;
            avatar.Stats.Level = Math.Max(avatar.Stats.Level,
                Progression.LevelCurve.GetLevelForExperience(avatar.Stats.Experience));
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

        // Reputation rewards carry no avatar state — reputation only exists as
        // ReputationChanged transactions (replayed arc state + cross-arc projection).
        // They are handled by CollectRewardTransactions; the quest command handlers
        // commit those transactions together with the reward's cause.

        // Award Achievements — the avatar's Achievements list is the single unlock
        // ledger; mirrors the dialogue UnlockAchievement action path exactly
        if (reward.Achievement != null && reward.Achievement.Length > 0)
        {
            foreach (var achievementReward in reward.Achievement)
            {
                if (string.IsNullOrEmpty(achievementReward.AchievementRef))
                    continue;

                avatar.Achievements ??= Array.Empty<AchievementEntry>();
                if (avatar.Achievements.Any(a => a.AchievementRef == achievementReward.AchievementRef))
                    continue; // already unlocked — idempotent

                var list = avatar.Achievements.ToList();
                list.Add(new AchievementEntry { AchievementRef = achievementReward.AchievementRef });
                avatar.Achievements = list.ToArray();
            }
        }
    }

    /// <summary>
    /// Distribute all rewards that match the given condition.
    /// </summary>
    /// <param name="rewards">Array of quest rewards to filter and distribute</param>
    /// <param name="condition">The condition to match (OnSuccess, OnBranch, OnObjective)</param>
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
            if (!MatchesCondition(reward, condition, branchRef, objectiveRef))
                continue;

            // Distribute the reward
            DistributeReward(reward, avatar, world);
        }
    }

    /// <summary>
    /// Collects the arc transactions produced by rewards matching the given condition.
    /// Currently this is Reputation rewards: each yields a ReputationChanged transaction
    /// (plus one per spillover-affected faction), the same shape the dialogue
    /// ChangeReputation action writes. Callers commit these atomically with the
    /// transaction that caused the reward, so replay and the cross-arc progress
    /// projection both see them.
    /// </summary>
    /// <param name="rewards">Array of quest rewards to filter</param>
    /// <param name="condition">The condition to match (OnSuccess, OnBranch, OnObjective)</param>
    /// <param name="avatarId">Avatar receiving the rewards</param>
    /// <param name="arcInstanceId">Arc instance whose log records the reward</param>
    /// <param name="world">World containing faction definitions (for spillover)</param>
    /// <param name="branchRef">Optional branch reference for OnBranch rewards</param>
    /// <param name="objectiveRef">Optional objective reference for OnObjective rewards</param>
    public static List<ArcTransaction> CollectRewardTransactions(
        QuestReward[]? rewards,
        QuestRewardCondition condition,
        string avatarId,
        Guid arcInstanceId,
        IWorld world,
        string? branchRef = null,
        string? objectiveRef = null)
    {
        var transactions = new List<ArcTransaction>();
        if (rewards == null || rewards.Length == 0)
            return transactions;

        foreach (var reward in rewards)
        {
            if (!MatchesCondition(reward, condition, branchRef, objectiveRef))
                continue;

            if (reward.Reputation == null || reward.Reputation.Length == 0)
                continue;

            foreach (var reputationReward in reward.Reputation)
            {
                if (string.IsNullOrEmpty(reputationReward.FactionRef) || reputationReward.Amount == 0)
                    continue;

                transactions.Add(DialogueTransactionHelper.CreateReputationChangedTransaction(
                    avatarId,
                    reputationReward.FactionRef,
                    reputationReward.Amount,
                    arcInstanceId));

                // Spillover to related factions (allies gain, enemies lose) — each gets
                // its own ReputationChanged transaction, mirroring the dialogue path
                foreach (var (spillFactionRef, spillAmount) in ReputationManager.CalculateSpilloverForAll(
                    world.FactionsLookup, reputationReward.FactionRef, reputationReward.Amount))
                {
                    transactions.Add(DialogueTransactionHelper.CreateReputationChangedTransaction(
                        avatarId,
                        spillFactionRef,
                        spillAmount,
                        arcInstanceId));
                }
            }
        }

        return transactions;
    }

    /// <summary>
    /// Checks whether a reward applies for the given condition (and branch/objective scope).
    /// </summary>
    private static bool MatchesCondition(
        QuestReward reward,
        QuestRewardCondition condition,
        string? branchRef,
        string? objectiveRef)
    {
        if (reward.Condition != condition)
            return false;

        // For OnBranch rewards, check BranchRef matches
        if (condition == QuestRewardCondition.OnBranch)
        {
            if (string.IsNullOrEmpty(branchRef) || reward.BranchRef != branchRef)
                return false;
        }

        // For OnObjective rewards, check ObjectiveRef matches
        if (condition == QuestRewardCondition.OnObjective)
        {
            if (string.IsNullOrEmpty(objectiveRef) || reward.ObjectiveRef != objectiveRef)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Snapshot of the avatar state that <see cref="DistributeReward"/> can mutate.
    /// Captured before distributing and restored if the subsequent avatar persistence
    /// fails, so the in-memory avatar never diverges from its durable state (the
    /// reverse-on-persist-failure compensation pattern; audit M1).
    /// </summary>
    public sealed class AvatarRewardSnapshot
    {
        public float Credits { get; init; }
        public float Experience { get; init; }
        public int Level { get; init; }
        public Dictionary<string, float> EquipmentConditions { get; init; } = new();
        public Dictionary<string, int> ConsumableQuantities { get; init; } = new();
        public HashSet<string> Achievements { get; init; } = new();
    }

    /// <summary>
    /// Captures the reward-affected avatar state (credits, experience/level, equipment
    /// refs + conditions, consumable quantities, achievement refs). Cheap and exact:
    /// only what <see cref="DistributeReward"/> touches.
    /// </summary>
    public static AvatarRewardSnapshot CaptureRewardSnapshot(AvatarEntity avatar)
    {
        var snapshot = new AvatarRewardSnapshot
        {
            Credits = avatar.Stats?.Credits ?? 0,
            Experience = avatar.Stats?.Experience ?? 0f,
            Level = avatar.Stats?.Level ?? 1
        };

        if (avatar.Capabilities?.Equipment != null)
        {
            foreach (var entry in avatar.Capabilities.Equipment)
            {
                if (!string.IsNullOrEmpty(entry.EquipmentRef))
                    snapshot.EquipmentConditions[entry.EquipmentRef] = entry.Condition;
            }
        }

        if (avatar.Capabilities?.Consumables != null)
        {
            foreach (var entry in avatar.Capabilities.Consumables)
            {
                if (!string.IsNullOrEmpty(entry.ConsumableRef))
                    snapshot.ConsumableQuantities[entry.ConsumableRef] = entry.Quantity;
            }
        }

        if (avatar.Achievements != null)
        {
            foreach (var entry in avatar.Achievements)
            {
                if (!string.IsNullOrEmpty(entry.AchievementRef))
                    snapshot.Achievements.Add(entry.AchievementRef);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Restores the avatar's reward-affected state from a snapshot taken just before
    /// <see cref="DistributeRewards"/>. Only valid when nothing besides reward
    /// distribution ran in between (the quest handlers' commit→distribute→persist
    /// window). Newly-added equipment/consumables/achievements are removed; mutated
    /// conditions and quantities are restored.
    /// </summary>
    public static void RestoreRewardSnapshot(AvatarEntity avatar, AvatarRewardSnapshot snapshot)
    {
        avatar.Stats ??= new CharacterStats();
        avatar.Stats.Credits = snapshot.Credits;
        avatar.Stats.Experience = snapshot.Experience;
        avatar.Stats.Level = snapshot.Level;

        if (avatar.Capabilities?.Equipment != null)
        {
            var equipment = avatar.Capabilities.Equipment
                .Where(e => string.IsNullOrEmpty(e.EquipmentRef) ||
                            snapshot.EquipmentConditions.ContainsKey(e.EquipmentRef))
                .ToArray();
            foreach (var entry in equipment)
            {
                if (!string.IsNullOrEmpty(entry.EquipmentRef) &&
                    snapshot.EquipmentConditions.TryGetValue(entry.EquipmentRef, out var condition))
                {
                    entry.Condition = condition;
                }
            }
            avatar.Capabilities.Equipment = equipment;
        }

        if (avatar.Capabilities?.Consumables != null)
        {
            var consumables = avatar.Capabilities.Consumables
                .Where(c => string.IsNullOrEmpty(c.ConsumableRef) ||
                            snapshot.ConsumableQuantities.ContainsKey(c.ConsumableRef))
                .ToArray();
            foreach (var entry in consumables)
            {
                if (!string.IsNullOrEmpty(entry.ConsumableRef) &&
                    snapshot.ConsumableQuantities.TryGetValue(entry.ConsumableRef, out var quantity))
                {
                    entry.Quantity = quantity;
                }
            }
            avatar.Capabilities.Consumables = consumables;
        }

        if (avatar.Achievements != null)
        {
            avatar.Achievements = avatar.Achievements
                .Where(a => string.IsNullOrEmpty(a.AchievementRef) ||
                            snapshot.Achievements.Contains(a.AchievementRef))
                .ToArray();
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
        HashSet<string>? unlockedAchievements = null,
        HashSet<string>? awardedQuestTokens = null)
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

                // Check in quest tokens (from arc state, not avatar)
                if (!hasItem && awardedQuestTokens != null)
                {
                    hasItem = awardedQuestTokens.Contains(prereq.RequiredItemRef);
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
