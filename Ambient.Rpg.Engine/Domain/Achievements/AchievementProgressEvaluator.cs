using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Domain.Achievements;

/// <summary>
/// Service for evaluating achievement progress from event-sourced Arc transaction logs.
/// Achievements track avatar milestones by querying immutable transaction history.
/// Progress is computed on-demand, not stored incrementally.
/// Server and client use this same logic to compute achievement progress.
/// </summary>
public static class AchievementProgressEvaluator
{
    /// <summary>
    /// Evaluates progress for a single achievement by querying Arc transactions.
    /// Returns value between 0.0 and 1.0 (percentage toward threshold).
    /// </summary>
    /// <param name="achievement">Achievement template with criteria</param>
    /// <param name="allArcInstances">All Arc instances to query</param>
    /// <param name="world">World for looking up character/Arc templates</param>
    /// <param name="avatarId">Avatar ID to filter transactions</param>
    /// <returns>Progress percentage (0.0 - 1.0)</returns>
    public static float EvaluateProgress(
        Achievement achievement,
        IEnumerable<ArcInstance> allArcInstances,
        IWorld world,
        string avatarId)
    {
        if (achievement.Criteria == null)
            return 0.0f;

        var criteria = achievement.Criteria;
        var currentValue = GetCurrentValue(criteria, allArcInstances, world, avatarId);
        var progress = currentValue / criteria.Threshold;

        return Math.Clamp(progress, 0.0f, 1.0f);
    }

    /// <summary>
    /// Gets the current metric value for an achievement criteria.
    /// Queries transaction logs based on criteria type and filters.
    /// </summary>
    private static float GetCurrentValue(
        AchievementCriteria criteria,
        IEnumerable<ArcInstance> allArcInstances,
        IWorld world,
        string avatarId)
    {
        // Flatten all transactions from all arc instances for this avatar
        var allTransactions = allArcInstances
            .SelectMany(arc => arc.Transactions)
            .Where(t => t.AvatarId == avatarId || string.IsNullOrEmpty(avatarId)) // Filter by avatar
            .Where(t => t.Status == TransactionStatus.Committed) // Only count committed transactions
            .ToList();

        return criteria.Type switch
        {
            // Combat achievements
            AchievementCriteriaType.CharactersDefeated => CountCharacterDefeats(allTransactions),
            AchievementCriteriaType.CharactersDefeatedByTrait => CountCharacterDefeatsByTrait(allTransactions, criteria, world),
            AchievementCriteriaType.CharactersDefeatedByRef => CountCharacterDefeatsByRef(allTransactions, criteria.CharacterRef),

            // Discovery achievements
            AchievementCriteriaType.ArcsDiscovered => CountUniqueArcsDiscovered(allTransactions),
            AchievementCriteriaType.ArcsCompleted => CountArcsCompleted(allTransactions),
            AchievementCriteriaType.ArcTriggersActivated => CountArcTriggersActivated(allTransactions),

            // Social achievements
            AchievementCriteriaType.DialogueTreesCompleted => CountDialogueTreesCompleted(allTransactions),
            AchievementCriteriaType.DialogueNodesVisited => CountDialogueNodesVisited(allTransactions),
            AchievementCriteriaType.UniqueCharactersMet => CountUniqueCharactersMet(allTransactions),

            // Relationship achievements
            AchievementCriteriaType.TraitsAssigned => CountTraitsAssigned(allTransactions),
            AchievementCriteriaType.TraitsAssignedByType => CountTraitsAssignedByType(allTransactions, criteria.TraitSpecified ? criteria.Trait.ToString() : null),
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

            // Traditional voxel metrics — NOT event-sourced. The Arc transaction
            // log carries no block placement/destruction transactions (those
            // counters live on AvatarBase.BlocksPlaced/BlocksDestroyed, maintained
            // by the voxel engine outside this log), so these criteria cannot be
            // evaluated here and always report zero progress. No shipped world
            // authors them; do not use them for Arc achievements.
            AchievementCriteriaType.BlocksPlaced => 0,
            AchievementCriteriaType.BlocksDestroyed => 0,

            _ => 0
        };
    }

    #region Combat Metrics

    private static float CountCharacterDefeats(List<ArcTransaction> transactions)
    {
        return transactions.Count(t => t.Type == ArcTransactionType.CharacterDefeated);
    }

    private static float CountCharacterDefeatsByTrait(List<ArcTransaction> transactions, AchievementCriteria criteria, IWorld world)
    {
        // Fail closed like the quest evaluator: a ByTrait criteria without a Trait
        // counts nothing (content validation requires the attribute), instead of
        // silently degrading to "count every defeat"
        if (!criteria.TraitSpecified)
            return 0;

        // Resolve the defeated character's template and check the trait
        return transactions
            .Where(t => t.Type == ArcTransactionType.CharacterDefeated)
            .Count(t =>
            {
                var characterRef = t.GetData<string>(TransactionDataKeys.CharacterRef);
                return !string.IsNullOrEmpty(characterRef) &&
                       world.CharactersLookup.TryGetValue(characterRef, out var character) &&
                       character.CarriesTrait(criteria.Trait);
            });
    }

    private static float CountCharacterDefeatsByRef(List<ArcTransaction> transactions, string? characterRef)
    {
        if (string.IsNullOrEmpty(characterRef))
            return CountCharacterDefeats(transactions);

        return transactions
            .Where(t => t.Type == ArcTransactionType.CharacterDefeated)
            .Count(t => t.GetData<string>(TransactionDataKeys.CharacterRef) == characterRef);
    }

    #endregion

    #region Discovery Metrics

    private static float CountUniqueArcsDiscovered(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.ArcDiscovered)
            .Select(t => t.GetData<string>(TransactionDataKeys.ArcRef))
            .Distinct()
            .Count();
    }

    private static float CountArcsCompleted(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.ArcCompleted)
            .Select(t => t.GetData<string>(TransactionDataKeys.ArcRef))
            .Distinct()
            .Count();
    }

    private static float CountArcTriggersActivated(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.TriggerActivated)
            .Count();
    }

    #endregion

    #region Social Metrics

    private static float CountDialogueTreesCompleted(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.DialogueCompleted)
            .Select(t => t.GetData<string>(TransactionDataKeys.DialogueTreeRef))
            .Distinct()
            .Count();
    }

    private static float CountDialogueNodesVisited(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.DialogueNodeVisited)
            .Count();
    }

    private static float CountUniqueCharactersMet(List<ArcTransaction> transactions)
    {
        // Characters met = either dialogue started or dialogue completed
        var dialogueChars = transactions
            .Where(t => t.Type == ArcTransactionType.DialogueStarted || t.Type == ArcTransactionType.DialogueCompleted)
            .Select(t => t.GetData<string>(TransactionDataKeys.CharacterRef))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct();

        return dialogueChars.Count();
    }

    #endregion

    #region Relationship Metrics

    private static float CountTraitsAssigned(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.TraitAssigned)
            .Count();
    }

    private static float CountTraitsAssignedByType(List<ArcTransaction> transactions, string? traitType)
    {
        if (string.IsNullOrEmpty(traitType))
            return CountTraitsAssigned(transactions);

        return transactions
            .Where(t => t.Type == ArcTransactionType.TraitAssigned)
            .Count(t => t.GetData<string>(TransactionDataKeys.TraitType) == traitType);
    }

    private static float CountTraitsAssignedToCharacterType(List<ArcTransaction> transactions, string? characterType, IWorld world)
    {
        if (string.IsNullOrEmpty(characterType))
            return CountTraitsAssigned(transactions);

        // Filter by character type (matching against RefName patterns like "Boss", "Merchant", "Encounter", "Quest")
        return transactions
            .Where(t => t.Type == ArcTransactionType.TraitAssigned)
            .Count(t =>
            {
                var characterRef = t.GetData<string>(TransactionDataKeys.CharacterRef);
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

    private static float CountItemsTraded(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.ItemTraded)
            .Count();
    }

    private static float CountLootAwarded(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.LootAwarded)
            .Count();
    }

    private static float CountQuestTokensEarned(List<ArcTransaction> transactions, string? questTokenRef)
    {
        var query = transactions.Where(t => t.Type == ArcTransactionType.QuestTokenAwarded);

        if (!string.IsNullOrEmpty(questTokenRef))
        {
            query = query.Where(t => t.GetData<string>(TransactionDataKeys.QuestTokenRef) == questTokenRef);
        }

        return query.Count();
    }

    #endregion

    #region Quest Metrics

    private static float CountQuestsCompleted(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.QuestCompleted)
            .Select(t => t.GetData<string>(TransactionDataKeys.QuestRef))
            .Distinct()
            .Count();
    }

    private static float CountQuestsCompletedByRef(List<ArcTransaction> transactions, string? questRef)
    {
        if (string.IsNullOrEmpty(questRef))
            return CountQuestsCompleted(transactions);

        return transactions
            .Where(t => t.Type == ArcTransactionType.QuestCompleted)
            .Any(t => t.GetData<string>(TransactionDataKeys.QuestRef) == questRef) ? 1 : 0;
    }

    #endregion

    #region Reputation Metrics

    private static float CheckReputationReached(List<ArcTransaction> transactions, string? factionRef, string? reputationLevel)
    {
        if (string.IsNullOrEmpty(factionRef))
            return 0;

        // Calculate total reputation changes for the faction
        var totalReputation = transactions
            .Where(t => t.Type == ArcTransactionType.ReputationChanged &&
                       t.GetData<string>(TransactionDataKeys.FactionRef) == factionRef)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Amount, out var amount) ? amount : 0);

        // Check if the reputation level is reached
        // Reputation levels: Hated < -6000, Hostile < -3000, Unfriendly < 0, Neutral < 3000,
        //                    Friendly < 6000, Honored < 12000, Revered < 21000, Exalted >= 21000
        var targetThreshold = GetReputationThreshold(reputationLevel);

        return totalReputation >= targetThreshold ? 1 : 0;
    }

    private static float CountFactionsAtReputationLevel(List<ArcTransaction> transactions, string? reputationLevel)
    {
        if (string.IsNullOrEmpty(reputationLevel))
            return 0;

        // Group reputation changes by faction and sum them
        var factionReputations = transactions
            .Where(t => t.Type == ArcTransactionType.ReputationChanged)
            .GroupBy(t => t.GetData<string>(TransactionDataKeys.FactionRef))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(
                g => g.Key!,
                g => g.Sum(t => t.TryGetData<int>(TransactionDataKeys.Amount, out var amount) ? amount : 0)
            );

        var targetThreshold = GetReputationThreshold(reputationLevel);

        return factionReputations.Values.Count(rep => rep >= targetThreshold);
    }

    private static int GetReputationThreshold(string? reputationLevel)
    {
        // Delegate to the reputation system's own thresholds — this table used to
        // disagree (Honored 6000 vs 9000, Exalted 21000 vs 42000), so "Reach Exalted"
        // achievements unlocked at what the faction system considers Revered
        if (Enum.TryParse<ReputationLevel>(reputationLevel, ignoreCase: true, out var level))
        {
            return Ambient.Rpg.Engine.Domain.Reputation.ReputationManager.GetReputationRange(level).Min;
        }

        return 0;
    }

    #endregion

    #region Battle Metrics

    private static float CountStatusEffectsApplied(List<ArcTransaction> transactions, string? statusEffectType)
    {
        var query = transactions.Where(t => t.Type == ArcTransactionType.StatusEffectApplied);

        if (!string.IsNullOrEmpty(statusEffectType))
        {
            query = query.Where(t =>
                t.GetData<string>(TransactionDataKeys.StatusEffectRef)?.Contains(statusEffectType, StringComparison.OrdinalIgnoreCase) == true ||
                t.GetData<string>(TransactionDataKeys.StatusEffectType) == statusEffectType);
        }

        return query.Count();
    }

    private static float CountCriticalHitsDealt(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.CriticalHitDealt)
            .Count();
    }

    private static float CountCombosExecuted(List<ArcTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == ArcTransactionType.ComboExecuted)
            .Count();
    }

    #endregion

    /// <summary>
    /// Evaluates all achievements for an avatar and returns updated instances with progress.
    /// Use this for batch evaluation (e.g., periodic achievement check).
    /// </summary>
    public static List<AchievementInstance> EvaluateAllAchievements(
        IEnumerable<Achievement> allAchievements,
        IEnumerable<ArcInstance> allArcInstances,
        IWorld world,
        string avatarId)
    {
        var results = new List<AchievementInstance>();

        foreach (var achievement in allAchievements)
        {
            var progress = EvaluateProgress(achievement, allArcInstances, world, avatarId);
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
        IEnumerable<ArcInstance> allArcInstances,
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

            var progress = EvaluateProgress(achievement, allArcInstances, world, avatarId);
            var isNowUnlocked = progress >= 1.0f;

            if (isNowUnlocked)
            {
                newlyUnlocked.Add(achievement);
            }
        }

        return newlyUnlocked;
    }
}
