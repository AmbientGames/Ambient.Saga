using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Domain.Rpg.Quests;

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
        World world)
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
        World world)
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
        World world)
    {
        // If stage has branches, check if a branch was chosen
        if (stage.Branches != null)
        {
            return transactions.Any(t =>
                t.Type == SagaTransactionType.QuestBranchChosen &&
                t.GetData<string>("QuestRef") == quest.RefName &&
                t.GetData<string>("StageRef") == stage.RefName);
        }

        // Otherwise check if all required objectives are complete
        if (stage.Objectives == null || stage.Objectives.Objective == null)
            return false;

        foreach (var objective in stage.Objectives.Objective)
        {
            // Skip optional objectives
            if (objective.Optional)
                continue;

            if (!IsObjectiveComplete(quest, stage, objective, transactions, world))
                return false;
        }

        return true;
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
    public static (bool failed, string? reason) CheckFailConditions(
        Quest quest,
        List<SagaTransaction> transactions)
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
            t.TryGetData<string>("CharacterTag", out var tag) &&
            tag == objective.CharacterTag);
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
}
