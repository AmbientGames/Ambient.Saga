using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Quests;

/// <summary>
/// Evaluates quest progress by querying the transaction log.
/// This is the core of the event-sourced quest system - all quest state is derived from transactions.
/// </summary>
public static class QuestProgressEvaluator
{
    /// <summary>
    /// Evaluate objective progress by querying transactions.
    /// Returns current value toward threshold (e.g., 3 of 5 dragons defeated).
    /// </summary>
    public static int EvaluateObjectiveProgress(
        Quest quest,
        QuestStage stage,
        QuestObjective objective,
        List<SagaTransaction> transactions,
        IWorld world)
    {
        var relevantTransactions = transactions
            .Where(t => t.Status == TransactionStatus.Committed)
            .ToList();

        return objective.Type switch
        {
            QuestObjectiveType.CharacterDefeated => CountCharacterDefeated(objective, relevantTransactions),
            QuestObjectiveType.CharactersDefeatedByTag => CountCharacterDefeatedByTag(objective, relevantTransactions),
            QuestObjectiveType.CharactersDefeatedByType => CountCharacterDefeatedByType(objective, relevantTransactions),

            QuestObjectiveType.DialogueCompleted => CountDialogueCompleted(objective, relevantTransactions),
            QuestObjectiveType.DialogueChoiceSelected => CountDialogueChoiceSelected(objective, relevantTransactions),
            QuestObjectiveType.DialogueNodeVisited => CountDialogueNodeVisited(objective, relevantTransactions),

            QuestObjectiveType.ItemCollected => CountItemCollected(objective, relevantTransactions),
            QuestObjectiveType.ItemDelivered => CountItemDelivered(objective, relevantTransactions),
            QuestObjectiveType.ItemTraded => CountItemTraded(objective, relevantTransactions),

            QuestObjectiveType.QuestTokenCollected => CountQuestTokenCollected(objective, relevantTransactions),

            QuestObjectiveType.SagaDiscovered => CountSagaDiscovered(objective, relevantTransactions),
            QuestObjectiveType.LocationReached => CountLocationReached(objective, relevantTransactions),
            QuestObjectiveType.TriggerActivated => CountTriggerActivated(objective, relevantTransactions),

            QuestObjectiveType.ItemCrafted => CountItemCrafted(objective, relevantTransactions),
            QuestObjectiveType.CurrencyCollected => CountCurrencyCollected(objective, relevantTransactions),

            // Custom objectives are evaluated externally - return threshold if a CustomObjectiveCompleted transaction exists
            QuestObjectiveType.Custom => CountCustomObjective(objective, relevantTransactions),

            _ => 0
        };
    }

    /// <summary>
    /// Check if an objective is complete (current value >= threshold).
    /// </summary>
    public static bool IsObjectiveComplete(
        Quest quest,
        QuestStage stage,
        QuestObjective objective,
        List<SagaTransaction> transactions,
        IWorld world)
    {
        var currentValue = EvaluateObjectiveProgress(quest, stage, objective, transactions, world);
        return currentValue >= objective.Threshold;
    }

    /// <summary>
    /// Check if all required objectives in a stage are complete.
    /// </summary>
    public static bool IsStageComplete(
        Quest quest,
        QuestStage stage,
        List<SagaTransaction> transactions,
        IWorld world)
    {
        // If stage has branches, check if a branch was chosen
        if (stage.Branches != null)
        {
            return transactions.Any(t =>
                t.Type == SagaTransactionType.QuestBranchChosen &&
                t.GetData<string>("QuestRef") == quest.RefName &&
                t.GetData<string>("StageRef") == stage.RefName);
        }

        // Otherwise check objectives based on logical operator
        if (stage.Objectives == null || stage.Objectives.Objective == null)
            return false;

        var requiredObjectives = stage.Objectives.Objective.Where(o => !o.Optional).ToList();
        if (!requiredObjectives.Any())
            return true;

        var useOrLogic = stage.Objectives.LogicalOperator == ConditionLogic.OR;

        if (useOrLogic)
        {
            // OR logic: at least one required objective must be complete
            return requiredObjectives.Any(objective =>
                IsObjectiveComplete(quest, stage, objective, transactions, world));
        }
        else
        {
            // AND logic (default): all required objectives must be complete
            return requiredObjectives.All(objective =>
                IsObjectiveComplete(quest, stage, objective, transactions, world));
        }
    }

    /// <summary>
    /// Get the next stage RefName after completing current stage.
    /// Returns null if quest is complete.
    /// </summary>
    public static string? GetNextStage(
        Quest quest,
        QuestStage currentStage,
        List<SagaTransaction> transactions)
    {
        // If stage has branches, check which branch was chosen and use its NextStage
        if (currentStage.Branches != null)
        {
            var branchTransaction = transactions
                .Where(t => t.Type == SagaTransactionType.QuestBranchChosen &&
                           t.GetData<string>("QuestRef") == quest.RefName &&
                           t.GetData<string>("StageRef") == currentStage.RefName)
                .OrderByDescending(t => t.SequenceNumber)
                .FirstOrDefault();

            if (branchTransaction != null)
            {
                var chosenBranchRef = branchTransaction.GetData<string>("BranchRef");
                var chosenBranch = currentStage.Branches.Branch.FirstOrDefault(b => b.RefName == chosenBranchRef);
                return chosenBranch?.NextStage;
            }
        }

        // Otherwise use stage's NextStage
        return currentStage.NextStage;
    }

    /// <summary>
    /// Check if quest has failed due to fail conditions.
    /// </summary>
    /// <param name="quest">The quest definition to check</param>
    /// <param name="transactions">Committed transactions to evaluate</param>
    /// <param name="currentTime">Current time for time-based fail conditions (optional)</param>
    /// <param name="currentLocationRef">Player's current location for location-based fail conditions (optional)</param>
    public static (bool failed, string? reason) CheckFailConditions(
        Quest quest,
        List<SagaTransaction> transactions,
        DateTime? currentTime = null,
        string? currentLocationRef = null)
    {
        if (quest.FailConditions == null)
            return (false, null);

        foreach (var failCondition in quest.FailConditions)
        {
            switch (failCondition.Type)
            {
                case QuestFailConditionType.CharacterDied:
                    if (IsCharacterDead(failCondition.CharacterRef, transactions))
                        return (true, $"Quest failed: {failCondition.CharacterRef} died");
                    break;

                case QuestFailConditionType.WrongChoiceMade:
                    if (WasWrongChoiceMade(failCondition, transactions))
                        return (true, "Quest failed: Wrong choice made");
                    break;

                case QuestFailConditionType.TimeExpired:
                    if (HasTimeExpired(quest.RefName, failCondition, transactions, currentTime))
                        return (true, "Quest failed: Time limit expired");
                    break;

                case QuestFailConditionType.ItemLost:
                    if (WasItemLost(failCondition, transactions))
                        return (true, $"Quest failed: Required item {failCondition.ItemRef} was lost");
                    break;

                case QuestFailConditionType.LocationLeft:
                    if (HasLeftLocation(failCondition, transactions, currentLocationRef))
                        return (true, $"Quest failed: Left required location {failCondition.LocationRef}");
                    break;
            }
        }

        return (false, null);
    }

    // ===== Private Helper Methods =====

    private static int CountCharacterDefeated(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            (string.IsNullOrEmpty(objective.CharacterRef) || t.GetData<string>("CharacterRef") == objective.CharacterRef));
    }

    private static int CountCharacterDefeatedByTag(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            t.TryGetData<string>("CharacterTag", out var tags) &&
            tags.Split(',').Contains(objective.CharacterTag));
    }

    private static int CountCharacterDefeatedByType(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            t.TryGetData<string>("CharacterType", out var type) &&
            type == objective.CharacterType);
    }

    private static int CountDialogueCompleted(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.DialogueCompleted &&
            (string.IsNullOrEmpty(objective.DialogueRef) || t.GetData<string>("DialogueRef") == objective.DialogueRef));
    }

    private static int CountDialogueChoiceSelected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.GetData<string>("DialogueRef") == objective.DialogueRef &&
            t.TryGetData<string>("ChoiceRef", out var choice) &&
            choice == objective.ChoiceRef);
    }

    private static int CountDialogueNodeVisited(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.GetData<string>("DialogueRef") == objective.DialogueRef &&
            t.GetData<string>("NodeRef") == objective.NodeRef);
    }

    private static int CountItemCollected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Count items gained from LootAwarded transactions
        return transactions
            .Where(t => t.Type == SagaTransactionType.LootAwarded)
            .Sum(t =>
            {
                if (t.TryGetData<string>("ItemRef", out var itemRef) && itemRef == objective.ItemRef)
                    return t.TryGetData<int>("Quantity", out var qty) ? qty : 1;
                return 0;
            });
    }

    private static int CountItemDelivered(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Count items traded away (sold/given)
        return transactions
            .Where(t => t.Type == SagaTransactionType.ItemTraded &&
                       t.GetData<string>("ItemRef") == objective.ItemRef &&
                       t.GetData<string>("Direction") == "Sell")
            .Sum(t => t.TryGetData<int>("Quantity", out var qty) ? qty : 1);
    }

    private static int CountItemTraded(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.ItemTraded &&
            (string.IsNullOrEmpty(objective.ItemRef) || t.GetData<string>("ItemRef") == objective.ItemRef));
    }

    private static int CountQuestTokenCollected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions
            .Where(t => t.Type == SagaTransactionType.QuestTokenAwarded &&
                       t.GetData<string>("QuestTokenRef") == objective.QuestTokenRef)
            .Sum(t => t.TryGetData<int>("Amount", out var amt) ? amt : 1);
    }

    private static int CountSagaDiscovered(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.SagaDiscovered &&
            (string.IsNullOrEmpty(objective.SagaArcRef) || t.GetData<string>("SagaArcRef") == objective.SagaArcRef));
    }

    private static int CountLocationReached(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Any(t =>
            t.Type == SagaTransactionType.TriggerActivated &&
            t.GetData<string>("TriggerRef") == objective.LocationRef) ? 1 : 0;
    }

    private static int CountTriggerActivated(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.TriggerActivated &&
            t.GetData<string>("TriggerRef") == objective.TriggerRef);
    }

    private static int CountItemCrafted(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Count items crafted from ItemCrafted transactions
        return transactions
            .Where(t => t.Type == SagaTransactionType.ItemCrafted &&
                       (string.IsNullOrEmpty(objective.ItemRef) || t.GetData<string>("ItemRef") == objective.ItemRef))
            .Sum(t => t.TryGetData<int>("Quantity", out var qty) ? qty : 1);
    }

    private static int CountCurrencyCollected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Sum all currency gained (from trades, loot, dialogue rewards)
        // Only count positive amounts (gains, not losses)
        return transactions
            .Where(t => t.Type == SagaTransactionType.CurrencyChanged &&
                       t.TryGetData<int>("Amount", out var amt) && amt > 0)
            .Sum(t => t.TryGetData<int>("Amount", out var amt) ? amt : 0);
    }

    private static int CountCustomObjective(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Custom objectives are marked complete via CustomObjectiveCompleted transaction
        // with matching ObjectiveRef
        return transactions.Any(t =>
            t.Type == SagaTransactionType.CustomObjectiveCompleted &&
            t.GetData<string>("ObjectiveRef") == objective.RefName) ? objective.Threshold : 0;
    }

    private static bool IsCharacterDead(string? characterRef, List<SagaTransaction> transactions)
    {
        if (string.IsNullOrEmpty(characterRef))
            return false;

        return transactions.Any(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            t.GetData<string>("CharacterRef") == characterRef);
    }

    private static bool WasWrongChoiceMade(QuestFailCondition failCondition, List<SagaTransaction> transactions)
    {
        if (string.IsNullOrEmpty(failCondition.DialogueRef) || string.IsNullOrEmpty(failCondition.ChoiceRef))
            return false;

        return transactions.Any(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.GetData<string>("DialogueRef") == failCondition.DialogueRef &&
            t.TryGetData<string>("ChoiceRef", out var choice) &&
            choice == failCondition.ChoiceRef);
    }

    private static bool HasTimeExpired(
        string questRef,
        QuestFailCondition failCondition,
        List<SagaTransaction> transactions,
        DateTime? currentTime)
    {
        // Time limit must be specified
        if (!failCondition.TimeLimitSpecified || failCondition.TimeLimit <= 0)
            return false;

        // Need current time to check
        if (!currentTime.HasValue)
            return false;

        // Find when the quest was accepted
        var questAcceptedTransaction = transactions
            .Where(t => t.Type == SagaTransactionType.QuestAccepted &&
                       t.GetData<string>("QuestRef") == questRef)
            .OrderBy(t => t.LocalTimestamp)
            .FirstOrDefault();

        if (questAcceptedTransaction == null)
            return false; // Quest not started yet

        // Calculate elapsed time since quest was accepted
        var questStartTime = questAcceptedTransaction.LocalTimestamp;
        var elapsed = currentTime.Value - questStartTime;

        // TimeLimit is in seconds
        return elapsed.TotalSeconds > failCondition.TimeLimit;
    }

    private static bool WasItemLost(QuestFailCondition failCondition, List<SagaTransaction> transactions)
    {
        if (string.IsNullOrEmpty(failCondition.ItemRef))
            return false;

        // Check if player had the item at some point (via LootAwarded or QuestTokenAwarded or ItemTraded Buy)
        var hadItem = transactions.Any(t =>
            (t.Type == SagaTransactionType.LootAwarded && t.GetData<string>("ItemRef") == failCondition.ItemRef) ||
            (t.Type == SagaTransactionType.QuestTokenAwarded && t.GetData<string>("QuestTokenRef") == failCondition.ItemRef) ||
            (t.Type == SagaTransactionType.ItemTraded && t.GetData<string>("ItemRef") == failCondition.ItemRef && t.GetData<string>("Direction") == "Buy"));

        if (!hadItem)
            return false;

        // Check if the item was subsequently lost (sold, traded away, or explicitly removed)
        var lostItem = transactions.Any(t =>
            t.Type == SagaTransactionType.ItemTraded &&
            t.GetData<string>("ItemRef") == failCondition.ItemRef &&
            t.GetData<string>("Direction") == "Sell");

        return lostItem;
    }

    private static bool HasLeftLocation(
        QuestFailCondition failCondition,
        List<SagaTransaction> transactions,
        string? currentLocationRef)
    {
        if (string.IsNullOrEmpty(failCondition.LocationRef))
            return false;

        // If we have a current location, directly check if player is no longer at required location
        if (!string.IsNullOrEmpty(currentLocationRef))
        {
            // Simple string comparison - player is not at the required location
            return currentLocationRef != failCondition.LocationRef;
        }

        // Fall back to checking via LocationClaimed transactions
        // Find the most recent location claim
        var lastLocationClaim = transactions
            .Where(t => t.Type == SagaTransactionType.LocationClaimed)
            .OrderByDescending(t => t.LocalTimestamp)
            .FirstOrDefault();

        if (lastLocationClaim == null)
            return false; // No location data available

        // Check if the location claim indicates leaving the required area
        var locationRef = lastLocationClaim.GetData<string>("LocationRef");
        return !string.IsNullOrEmpty(locationRef) && locationRef != failCondition.LocationRef;
    }
}
