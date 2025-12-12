using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Achievements;

/// <summary>
/// Service for evaluating achievement progress from event-sourced Saga transaction logs.
/// Achievements track player milestones by querying immutable transaction history.
/// Progress is computed on-demand, not stored incrementally.
/// Server and client use this same logic to compute achievement progress.
/// </summary>
public static class AchievementProgressEvaluator
{
    /// <summary>
    /// Evaluates progress for a single achievement by querying Saga transactions.
    /// Returns value between 0.0 and 1.0 (percentage toward threshold).
    /// </summary>
    /// <param name="achievement">Achievement template with criteria</param>
    /// <param name="allSagaInstances">All Saga instances to query</param>
    /// <param name="world">World for looking up character/Saga templates</param>
    /// <param name="avatarId">Avatar ID to filter transactions</param>
    /// <returns>Progress percentage (0.0 - 1.0)</returns>
    public static float EvaluateProgress(
        Achievement achievement,
        IEnumerable<SagaInstance> allSagaInstances,
        IWorld world,
        string avatarId)
    {
        if (achievement.Criteria == null)
            return 0.0f;

        var criteria = achievement.Criteria;
        var currentValue = GetCurrentValue(criteria, allSagaInstances, world, avatarId);
        var progress = currentValue / criteria.Threshold;

        return Math.Clamp(progress, 0.0f, 1.0f);
    }

    /// <summary>
    /// Gets the current metric value for an achievement criteria.
    /// Queries transaction logs based on criteria type and filters.
    /// </summary>
    private static float GetCurrentValue(
        AchievementCriteria criteria,
        IEnumerable<SagaInstance> allSagaInstances,
        IWorld world,
        string avatarId)
    {
        // Flatten all transactions from all Saga instances for this avatar
        var allTransactions = allSagaInstances
            .SelectMany(saga => saga.Transactions)
            .Where(t => t.AvatarId == avatarId || string.IsNullOrEmpty(avatarId)) // Filter by avatar
            .Where(t => t.Status == TransactionStatus.Committed) // Only count committed transactions
            .ToList();

        return criteria.Type switch
        {
            // Combat achievements
            AchievementCriteriaType.CharactersDefeated => CountCharacterDefeats(allTransactions),
            AchievementCriteriaType.CharactersDefeatedByType => CountCharacterDefeatsByType(allTransactions, criteria.CharacterType, world),
            AchievementCriteriaType.CharactersDefeatedByTag => CountCharacterDefeatsByTag(allTransactions, criteria.CharacterTag, world),
            AchievementCriteriaType.CharactersDefeatedByRef => CountCharacterDefeatsByRef(allTransactions, criteria.CharacterRef),

            // Discovery achievements
            AchievementCriteriaType.SagaArcsDiscovered => CountUniqueSagasDiscovered(allTransactions),
            AchievementCriteriaType.SagaArcsCompleted => CountSagasCompleted(allTransactions),
            AchievementCriteriaType.LandmarksDiscovered => CountLandmarksDiscovered(allTransactions),
            AchievementCriteriaType.SagaTriggersActivated => CountSagaTriggersActivated(allTransactions),

            // Social achievements
            AchievementCriteriaType.DialogueTreesCompleted => CountDialogueTreesCompleted(allTransactions),
            AchievementCriteriaType.DialogueNodesVisited => CountDialogueNodesVisited(allTransactions),
            AchievementCriteriaType.UniqueCharactersMet => CountUniqueCharactersMet(allTransactions),

            // Relationship achievements
            AchievementCriteriaType.TraitsAssigned => CountTraitsAssigned(allTransactions),
            AchievementCriteriaType.TraitsAssignedByType => CountTraitsAssignedByType(allTransactions, criteria.TraitType),
            AchievementCriteriaType.TraitsAssignedToCharacterType => CountTraitsAssignedToCharacterType(allTransactions, criteria.CharacterType, world),

            // Economy achievements
            AchievementCriteriaType.ItemsTraded => CountItemsTraded(allTransactions),
            AchievementCriteriaType.LootAwarded => CountLootAwarded(allTransactions),
            AchievementCriteriaType.QuestTokensEarned => CountQuestTokensEarned(allTransactions, criteria.QuestTokenRef),

            // Quest achievements
            AchievementCriteriaType.QuestsCompleted => CountQuestsCompleted(allTransactions),
            AchievementCriteriaType.QuestsCompletedByRef => CountQuestsCompletedByRef(allTransactions, criteria.QuestRef),

            // Reputation achievements
            AchievementCriteriaType.ReputationReached => CheckReputationReached(allTransactions, criteria.FactionRef, criteria.ReputationLevel),
            AchievementCriteriaType.FactionsAtReputationLevel => CountFactionsAtReputationLevel(allTransactions, criteria.ReputationLevel),

            // Battle achievements
            AchievementCriteriaType.StatusEffectsApplied => CountStatusEffectsApplied(allTransactions, criteria.StatusEffectType),
            AchievementCriteriaType.CriticalHitsDealt => CountCriticalHitsDealt(allTransactions),
            AchievementCriteriaType.CombosExecuted => CountCombosExecuted(allTransactions),

            // Traditional voxel metrics (not event-sourced yet)
            AchievementCriteriaType.BlocksPlaced => 0, // TODO: Implement if needed
            AchievementCriteriaType.BlocksDestroyed => 0, // TODO: Implement if needed
            AchievementCriteriaType.DistanceTraveled => 0, // TODO: Implement if needed
            AchievementCriteriaType.PlayTimeHours => 0, // TODO: Implement if needed

            _ => 0
        };
    }

    #region Combat Metrics

    private static float CountCharacterDefeats(List<SagaTransaction> transactions)
    {
        return transactions.Count(t => t.Type == SagaTransactionType.CharacterDefeated);
    }

    private static float CountCharacterDefeatsByType(List<SagaTransaction> transactions, string? characterType, IWorld world)
    {
        if (string.IsNullOrEmpty(characterType))
            return CountCharacterDefeats(transactions);

        // Filter by character type (matching against RefName patterns like "Boss", "Merchant", "Encounter", "Quest")
        return transactions
            .Where(t => t.Type == SagaTransactionType.CharacterDefeated)
            .Count(t =>
            {
                var characterRef = t.GetData<string>("CharacterRef");
                if (string.IsNullOrEmpty(characterRef))
                    return false;

                // Check if character exists in world catalog
                if (!world.CharactersLookup.TryGetValue(characterRef, out var character))
                    return false;

                // Match against RefName (case-insensitive) - e.g., "GenericBoss" contains "Boss"
                return character.RefName?.Contains(characterType, StringComparison.OrdinalIgnoreCase) == true;
            });
    }

    private static float CountCharacterDefeatsByTag(List<SagaTransaction> transactions, string? tag, IWorld world)
    {
        if (string.IsNullOrEmpty(tag))
            return CountCharacterDefeats(transactions);

        // Filter by tag (matching against RefName or Description keywords like "dragon", "bandit", "undead")
        return transactions
            .Where(t => t.Type == SagaTransactionType.CharacterDefeated)
            .Count(t =>
            {
                var characterRef = t.GetData<string>("CharacterRef");
                if (string.IsNullOrEmpty(characterRef))
                    return false;

                // Check if character exists in world catalog
                if (!world.CharactersLookup.TryGetValue(characterRef, out var character))
                    return false;

                // Match against RefName or Description (case-insensitive)
                return character.RefName?.Contains(tag, StringComparison.OrdinalIgnoreCase) == true ||
                       character.Description?.Contains(tag, StringComparison.OrdinalIgnoreCase) == true;
            });
    }

    private static float CountCharacterDefeatsByRef(List<SagaTransaction> transactions, string? characterRef)
    {
        if (string.IsNullOrEmpty(characterRef))
            return CountCharacterDefeats(transactions);

        return transactions
            .Where(t => t.Type == SagaTransactionType.CharacterDefeated)
            .Count(t => t.GetData<string>("CharacterRef") == characterRef);
    }

    #endregion

    #region Discovery Metrics

    private static float CountUniqueSagasDiscovered(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.SagaDiscovered)
            .Select(t => t.GetData<string>("SagaArcRef"))
            .Distinct()
            .Count();
    }

    private static float CountSagasCompleted(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.SagaCompleted)
            .Select(t => t.GetData<string>("SagaArcRef"))
            .Distinct()
            .Count();
    }

    private static float CountLandmarksDiscovered(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.LandmarkDiscovered)
            .Select(t => t.GetData<string>("LandmarkRef"))
            .Distinct()
            .Count();
    }

    private static float CountSagaTriggersActivated(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.TriggerActivated)
            .Count();
    }

    #endregion

    #region Social Metrics

    private static float CountDialogueTreesCompleted(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.DialogueCompleted)
            .Select(t => t.GetData<string>("DialogueTreeRef"))
            .Distinct()
            .Count();
    }

    private static float CountDialogueNodesVisited(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.DialogueNodeVisited)
            .Count();
    }

    private static float CountUniqueCharactersMet(List<SagaTransaction> transactions)
    {
        // Characters met = either dialogue started or dialogue completed
        var dialogueChars = transactions
            .Where(t => t.Type == SagaTransactionType.DialogueStarted || t.Type == SagaTransactionType.DialogueCompleted)
            .Select(t => t.GetData<string>("CharacterRef"))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct();

        return dialogueChars.Count();
    }

    #endregion

    #region Relationship Metrics

    private static float CountTraitsAssigned(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.TraitAssigned)
            .Count();
    }

    private static float CountTraitsAssignedByType(List<SagaTransaction> transactions, string? traitType)
    {
        if (string.IsNullOrEmpty(traitType))
            return CountTraitsAssigned(transactions);

        return transactions
            .Where(t => t.Type == SagaTransactionType.TraitAssigned)
            .Count(t => t.GetData<string>("TraitType") == traitType);
    }

    private static float CountTraitsAssignedToCharacterType(List<SagaTransaction> transactions, string? characterType, IWorld world)
    {
        if (string.IsNullOrEmpty(characterType))
            return CountTraitsAssigned(transactions);

        // Filter by character type (matching against RefName patterns like "Boss", "Merchant", "Encounter", "Quest")
        return transactions
            .Where(t => t.Type == SagaTransactionType.TraitAssigned)
            .Count(t =>
            {
                var characterRef = t.GetData<string>("CharacterRef");
                if (string.IsNullOrEmpty(characterRef))
                    return false;

                // Check if character exists in world catalog
                if (!world.CharactersLookup.TryGetValue(characterRef, out var character))
                    return false;

                // Match against RefName (case-insensitive) - e.g., "GenericMerchant" contains "Merchant"
                return character.RefName?.Contains(characterType, StringComparison.OrdinalIgnoreCase) == true;
            });
    }

    #endregion

    #region Economy Metrics

    private static float CountItemsTraded(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.ItemTraded)
            .Count();
    }

    private static float CountLootAwarded(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.LootAwarded)
            .Count();
    }

    private static float CountQuestTokensEarned(List<SagaTransaction> transactions, string? questTokenRef)
    {
        var query = transactions.Where(t => t.Type == SagaTransactionType.QuestTokenAwarded);

        if (!string.IsNullOrEmpty(questTokenRef))
        {
            query = query.Where(t => t.GetData<string>("QuestTokenRef") == questTokenRef);
        }

        return query.Count();
    }

    #endregion

    #region Quest Metrics

    private static float CountQuestsCompleted(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.QuestCompleted)
            .Select(t => t.GetData<string>("QuestRef"))
            .Distinct()
            .Count();
    }

    private static float CountQuestsCompletedByRef(List<SagaTransaction> transactions, string? questRef)
    {
        if (string.IsNullOrEmpty(questRef))
            return CountQuestsCompleted(transactions);

        return transactions
            .Where(t => t.Type == SagaTransactionType.QuestCompleted)
            .Any(t => t.GetData<string>("QuestRef") == questRef) ? 1 : 0;
    }

    #endregion

    #region Reputation Metrics

    private static float CheckReputationReached(List<SagaTransaction> transactions, string? factionRef, string? reputationLevel)
    {
        if (string.IsNullOrEmpty(factionRef))
            return 0;

        // Calculate total reputation changes for the faction
        var totalReputation = transactions
            .Where(t => t.Type == SagaTransactionType.ReputationChanged &&
                       t.GetData<string>("FactionRef") == factionRef)
            .Sum(t => t.TryGetData<int>("Amount", out var amount) ? amount : 0);

        // Check if the reputation level is reached
        // Reputation levels: Hated < -6000, Hostile < -3000, Unfriendly < 0, Neutral < 3000,
        //                    Friendly < 6000, Honored < 12000, Revered < 21000, Exalted >= 21000
        var targetThreshold = GetReputationThreshold(reputationLevel);

        return totalReputation >= targetThreshold ? 1 : 0;
    }

    private static float CountFactionsAtReputationLevel(List<SagaTransaction> transactions, string? reputationLevel)
    {
        if (string.IsNullOrEmpty(reputationLevel))
            return 0;

        // Group reputation changes by faction and sum them
        var factionReputations = transactions
            .Where(t => t.Type == SagaTransactionType.ReputationChanged)
            .GroupBy(t => t.GetData<string>("FactionRef"))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(
                g => g.Key!,
                g => g.Sum(t => t.TryGetData<int>("Amount", out var amount) ? amount : 0)
            );

        var targetThreshold = GetReputationThreshold(reputationLevel);

        return factionReputations.Values.Count(rep => rep >= targetThreshold);
    }

    private static int GetReputationThreshold(string? reputationLevel)
    {
        return reputationLevel?.ToLowerInvariant() switch
        {
            "hated" => -6000,
            "hostile" => -3000,
            "unfriendly" => 0,
            "neutral" => 0,
            "friendly" => 3000,
            "honored" => 6000,
            "revered" => 12000,
            "exalted" => 21000,
            _ => 0
        };
    }

    #endregion

    #region Battle Metrics

    private static float CountStatusEffectsApplied(List<SagaTransaction> transactions, string? statusEffectType)
    {
        var query = transactions.Where(t => t.Type == SagaTransactionType.StatusEffectApplied);

        if (!string.IsNullOrEmpty(statusEffectType))
        {
            query = query.Where(t =>
                t.GetData<string>("StatusEffectRef")?.Contains(statusEffectType, StringComparison.OrdinalIgnoreCase) == true ||
                t.GetData<string>("StatusEffectType") == statusEffectType);
        }

        return query.Count();
    }

    private static float CountCriticalHitsDealt(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.CriticalHitDealt)
            .Count();
    }

    private static float CountCombosExecuted(List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.ComboExecuted)
            .Count();
    }

    #endregion

    /// <summary>
    /// Evaluates all achievements for an avatar and returns updated instances with progress.
    /// Use this for batch evaluation (e.g., periodic achievement check).
    /// </summary>
    public static List<AchievementInstance> EvaluateAllAchievements(
        IEnumerable<Achievement> allAchievements,
        IEnumerable<SagaInstance> allSagaInstances,
        IWorld world,
        string avatarId)
    {
        var results = new List<AchievementInstance>();

        foreach (var achievement in allAchievements)
        {
            var progress = EvaluateProgress(achievement, allSagaInstances, world, avatarId);
            var isUnlocked = progress >= 1.0f;

            var instance = new AchievementInstance
            {
                TemplateRef = achievement.RefName,
                InstanceId = Guid.NewGuid().ToString(),
                AvatarId = avatarId,
                CurrentProgress = (int)(progress * 100), // Store as percentage
                IsUnlocked = isUnlocked
            };

            results.Add(instance);
        }

        return results;
    }

    /// <summary>
    /// Checks if any achievements were just unlocked (progress crossed 100% threshold).
    /// Returns newly unlocked achievements.
    /// </summary>
    public static List<Achievement> GetNewlyUnlockedAchievements(
        IEnumerable<Achievement> allAchievements,
        IEnumerable<AchievementInstance> previousInstances,
        IEnumerable<SagaInstance> allSagaInstances,
        IWorld world,
        string avatarId)
    {
        var newlyUnlocked = new List<Achievement>();
        var previousDict = previousInstances.ToDictionary(i => i.TemplateRef, i => i.IsUnlocked);

        foreach (var achievement in allAchievements)
        {
            var wasUnlocked = previousDict.TryGetValue(achievement.RefName, out var wasUnlockedBool) && wasUnlockedBool;
            if (wasUnlocked)
                continue; // Already unlocked, skip

            var progress = EvaluateProgress(achievement, allSagaInstances, world, avatarId);
            var isNowUnlocked = progress >= 1.0f;

            if (isNowUnlocked)
            {
                newlyUnlocked.Add(achievement);
            }
        }

        return newlyUnlocked;
    }
}
