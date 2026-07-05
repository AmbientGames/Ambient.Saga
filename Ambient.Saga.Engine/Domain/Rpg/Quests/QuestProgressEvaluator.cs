using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain;

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
        var relevantTransactions = ScopeToCurrentAcceptance(quest, transactions
            .Where(t => t.Status == TransactionStatus.Committed)
            .ToList());

        return objective.Type switch
        {
            QuestObjectiveType.CharacterDefeated => CountCharacterDefeated(objective, relevantTransactions),
            QuestObjectiveType.CharactersDefeatedByTag => CountCharacterDefeatedByTag(objective, relevantTransactions),
            QuestObjectiveType.CharactersDefeatedByType => CountCharacterDefeatedByType(objective, relevantTransactions, world),

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

            QuestObjectiveType.CurrencyCollected => CountCurrencyCollected(objective, relevantTransactions),

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
        // If stage has branches, check if a branch was chosen — scoped to the
        // current acceptance so a branch chosen before an abandon + re-accept
        // doesn't mark the new run's stage as already complete
        if (stage.Branches != null)
        {
            return ScopeToCurrentAcceptance(quest, transactions).Any(t =>
                t.Type == SagaTransactionType.QuestBranchChosen &&
                t.GetData<string>(TransactionDataKeys.QuestRef) == quest.RefName &&
                t.GetData<string>(TransactionDataKeys.StageRef) == stage.RefName);
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
        // If stage has branches, check which branch was chosen and use its NextStage —
        // scoped to the current acceptance so a previous run's choice doesn't route
        // this one (see ScopeToCurrentAcceptance)
        if (currentStage.Branches != null)
        {
            var branchTransaction = ScopeToCurrentAcceptance(quest, transactions)
                .Where(t => t.Type == SagaTransactionType.QuestBranchChosen &&
                           t.GetData<string>(TransactionDataKeys.QuestRef) == quest.RefName &&
                           t.GetData<string>(TransactionDataKeys.StageRef) == currentStage.RefName)
                .OrderByDescending(t => t.SequenceNumber)
                .FirstOrDefault();

            if (branchTransaction != null)
            {
                var chosenBranchRef = branchTransaction.GetData<string>(TransactionDataKeys.BranchRef);
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
    /// <param name="currentLocationRef">Avatar's current location for location-based fail conditions (optional)</param>
    public static (bool failed, string? reason) CheckFailConditions(
        Quest quest,
        List<SagaTransaction> transactions,
        DateTime? currentTime = null,
        string? currentLocationRef = null)
    {
        if (quest.FailConditions == null)
            return (false, null);

        // Fail conditions only consider the current acceptance — a character death or
        // item loss before a re-accept must not fail the new run (and the time limit
        // starts from the latest acceptance, not the first-ever one)
        transactions = ScopeToCurrentAcceptance(quest, transactions);

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

    /// <summary>
    /// Restricts evaluation to transactions from the quest's CURRENT acceptance.
    /// Without this, abandon-then-re-accept inherited all prior progress
    /// (objectives instantly complete, stage rewards granted again, old branch
    /// choices auto-"chosen"). Public so command handlers can scope their own
    /// log queries (e.g. branch exclusivity) with the same rule.
    ///
    /// The scope starts at the beginning of the dialogue session the acceptance
    /// came from, not at the QuestAccepted transaction itself: the dialogue
    /// handlers stage node-action transactions (QuestTokenAwarded, ...) before
    /// they dispatch the nested AcceptQuestCommand, so a token granted by the
    /// very node that accepts the quest ends up with a LOWER sequence number
    /// than QuestAccepted. GiveQuestToken is first-visit-only, so scoping
    /// strictly from the acceptance excluded that token forever and soft-locked
    /// stage-1 QuestTokenCollected objectives. Acceptances that did not come
    /// from a dialogue session keep the strict from-QuestAccepted scope.
    /// </summary>
    public static List<SagaTransaction> ScopeToCurrentAcceptance(Quest quest, List<SagaTransaction> transactions)
    {
        var latestAccept = transactions
            .Where(t => t.Type == SagaTransactionType.QuestAccepted &&
                       t.GetData<string>(TransactionDataKeys.QuestRef) == quest.RefName)
            .OrderByDescending(t => t.SequenceNumber)
            .FirstOrDefault();

        if (latestAccept == null)
            return transactions;

        var scopeStart = FindAcceptingDialogueSessionStart(quest.RefName, latestAccept, transactions)
                         ?? latestAccept.SequenceNumber;

        return transactions.Where(t => t.SequenceNumber >= scopeStart).ToList();
    }

    /// <summary>
    /// Finds the sequence number of the DialogueStarted transaction that begins
    /// the dialogue session the given acceptance was made in, or null when the
    /// acceptance did not come from a (still attributable) dialogue session.
    ///
    /// Rule: take the nearest preceding DialogueStarted for the quest giver
    /// (QuestAccepted always records QuestGiverRef; for dialogue-driven
    /// acceptances it is the conversation partner — see DialogueActionExecutor).
    /// That session contains the acceptance when it is still open at the
    /// acceptance, or when it was sealed by the very command that dispatched the
    /// acceptance: on a terminal accept node EndDialogue writes DialogueCompleted
    /// just before the quest events run, so only quest-lifecycle transactions
    /// from that same dispatch can sit between the completion and the
    /// acceptance. Anything else in that gap — or an abandon of this very quest,
    /// which proves the acceptance is a fresh run — means the session already
    /// ended for good, and its transactions must not leak into the new scope
    /// (tokens from a previous conversation, or from before an abandon +
    /// re-accept, never count).
    /// </summary>
    private static long? FindAcceptingDialogueSessionStart(
        string questRef,
        SagaTransaction latestAccept,
        List<SagaTransaction> transactions)
    {
        var giverRef = latestAccept.GetData<string>(TransactionDataKeys.QuestGiverRef);

        var prior = transactions
            .Where(t => t.SequenceNumber < latestAccept.SequenceNumber)
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        var sessionStart = prior.LastOrDefault(t =>
            t.Type == SagaTransactionType.DialogueStarted &&
            (string.IsNullOrEmpty(giverRef) ||
             t.GetData<string>(TransactionDataKeys.CharacterRef) == giverRef));

        if (sessionStart == null)
            return null;

        var sessionCharacterRef = sessionStart.GetData<string>(TransactionDataKeys.CharacterRef);

        var sessionCompletion = prior.LastOrDefault(t =>
            t.Type == SagaTransactionType.DialogueCompleted &&
            t.SequenceNumber > sessionStart.SequenceNumber &&
            t.GetData<string>(TransactionDataKeys.CharacterRef) == sessionCharacterRef);

        // Session still open at the acceptance — the acceptance came from it
        if (sessionCompletion == null)
            return sessionStart.SequenceNumber;

        // Session sealed first: only the terminal-accept-node dispatch order may
        // bridge the gap (see summary above)
        var acceptanceBelongsToSealedSession = prior
            .Where(t => t.SequenceNumber > sessionCompletion.SequenceNumber)
            .All(t => QuestLifecycleTransactionTypes.Contains(t.Type) &&
                      !(t.Type == SagaTransactionType.QuestAbandoned &&
                        t.GetData<string>(TransactionDataKeys.QuestRef) == questRef));

        return acceptanceBelongsToSealedSession ? sessionStart.SequenceNumber : null;
    }

    /// <summary>
    /// Transaction types the dialogue handlers' quest-event dispatch (and the
    /// quest pipeline behaviors it triggers) can emit between a terminal node's
    /// DialogueCompleted and a QuestAccepted from that same node.
    /// </summary>
    private static readonly HashSet<SagaTransactionType> QuestLifecycleTransactionTypes = new()
    {
        SagaTransactionType.QuestAccepted,
        SagaTransactionType.QuestCompleted,
        SagaTransactionType.QuestAbandoned,
        SagaTransactionType.QuestStageAdvanced,
        SagaTransactionType.QuestObjectiveCompleted,
        SagaTransactionType.QuestBranchChosen,
        SagaTransactionType.ReputationChanged
    };

    // ===== Private Helper Methods =====

    private static int CountCharacterDefeated(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            (string.IsNullOrEmpty(objective.CharacterRef) || t.GetData<string>(TransactionDataKeys.CharacterRef) == objective.CharacterRef));
    }

    private static int CountCharacterDefeatedByTag(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // CharacterTag carries the defeated character's Tags plus its boolean trait names
        // (content authors objectives against both vocabularies, e.g. "BanditScout"/"hostile")
        return transactions.Count(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            t.TryGetData<string>(TransactionDataKeys.CharacterTag, out var tags) &&
            tags!.Split(',').Contains(objective.CharacterTag, StringComparer.OrdinalIgnoreCase));
    }

    private static int CountCharacterDefeatedByType(QuestObjective objective, List<SagaTransaction> transactions, IWorld world)
    {
        // No emitter writes a CharacterType data key, so resolve the defeated
        // character's template and match RefName/trait names against the type
        // (same convention as AchievementProgressEvaluator.CountCharacterDefeatsByType)
        return transactions.Count(t =>
        {
            if (t.Type != SagaTransactionType.CharacterDefeated)
                return false;

            if (t.TryGetData<string>(TransactionDataKeys.CharacterType, out var type))
                return type == objective.CharacterType;

            var characterRef = t.GetData<string>(TransactionDataKeys.CharacterRef);
            if (string.IsNullOrEmpty(characterRef) ||
                !world.CharactersLookup.TryGetValue(characterRef, out var character))
                return false;

            if (character.RefName?.Contains(objective.CharacterType, StringComparison.OrdinalIgnoreCase) == true)
                return true;

            return character.Traits?.Any(tr =>
                tr.Name.ToString().Contains(objective.CharacterType, StringComparison.OrdinalIgnoreCase)) == true;
        });
    }

    private static int CountDialogueCompleted(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Content filters by DialogueRef or by CharacterRef ("speak with X"); honor both
        return transactions.Count(t =>
            t.Type == SagaTransactionType.DialogueCompleted &&
            (string.IsNullOrEmpty(objective.DialogueRef) || t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == objective.DialogueRef) &&
            (string.IsNullOrEmpty(objective.CharacterRef) || t.GetData<string>(TransactionDataKeys.CharacterRef) == objective.CharacterRef));
    }

    private static int CountDialogueChoiceSelected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Choices have no identity of their own — selecting one is recorded as a visit
        // to its target node, so ChoiceRef refers to the chosen node's id
        return transactions.Count(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == objective.DialogueRef &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == objective.ChoiceRef);
    }

    private static int CountDialogueNodeVisited(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == objective.DialogueRef &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == objective.NodeRef);
    }

    private static int CountItemCollected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // LootAwarded is retired (corpse looting removed 2026-07-04; no producer remains)
        // but historical transactions are still counted. It packs per-family lists:
        // "Ref:Condition" for degradables (one entry per item), "Ref:Quantity" for stackables.
        var looted = transactions
            .Where(t => t.Type == SagaTransactionType.LootAwarded)
            .Sum(t =>
            {
                var count = 0;
                foreach (var key in LootDegradableKeys)
                    count += ParseLootEntries(t, key).Count(e => e.Ref == objective.ItemRef);
                foreach (var key in LootStackableKeys)
                    count += ParseLootEntries(t, key)
                        .Where(e => e.Ref == objective.ItemRef)
                        .Sum(e => Math.Max(1, (int)e.Value));
                return count;
            });

        // "Collected" means acquired, not specifically looted — merchant purchases
        // count too. Several gather objectives name materials (Screws, Mortar, ...)
        // that exist only in trade, so loot-only counting left them at 0 forever.
        var bought = transactions
            .Where(t => t.Type == SagaTransactionType.ItemTraded &&
                       t.GetData<string>(TransactionDataKeys.ItemRef) == objective.ItemRef &&
                       t.TryGetData<bool>(TransactionDataKeys.IsBuying, out var isBuying) && isBuying)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Quantity, out var qty) ? qty : 1);

        return looted + bought;
    }

    private static readonly string[] LootDegradableKeys =
        { TransactionDataKeys.Equipment, TransactionDataKeys.Spells, TransactionDataKeys.Tools };

    private static readonly string[] LootStackableKeys =
        { TransactionDataKeys.Consumables, TransactionDataKeys.Blocks, TransactionDataKeys.BuildingMaterials };

    private static IEnumerable<(string Ref, float Value)> ParseLootEntries(SagaTransaction t, string dataKey)
    {
        if (!t.TryGetData<string>(dataKey, out var packed) || string.IsNullOrEmpty(packed))
            yield break;

        foreach (var entry in packed.Split(','))
        {
            var sep = entry.LastIndexOf(':');
            if (sep <= 0) continue;
            float.TryParse(entry[(sep + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value);
            yield return (entry[..sep], value);
        }
    }

    private static int CountItemDelivered(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Count items traded away (IsBuying == false means the avatar sold/gave the item)
        return transactions
            .Where(t => t.Type == SagaTransactionType.ItemTraded &&
                       t.GetData<string>(TransactionDataKeys.ItemRef) == objective.ItemRef &&
                       t.TryGetData<bool>(TransactionDataKeys.IsBuying, out var isBuying) && !isBuying)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Quantity, out var qty) ? qty : 1);
    }

    private static int CountItemTraded(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.ItemTraded &&
            (string.IsNullOrEmpty(objective.ItemRef) || t.GetData<string>(TransactionDataKeys.ItemRef) == objective.ItemRef));
    }

    private static int CountQuestTokenCollected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Shipped quest templates author the token under ItemRef instead of
        // QuestTokenRef (e.g. MAIN_QUEST_05/COLLECT_TOKENS) — honor both, else
        // those objectives sit at 0 forever
        var tokenRef = !string.IsNullOrEmpty(objective.QuestTokenRef) ? objective.QuestTokenRef : objective.ItemRef;
        if (string.IsNullOrEmpty(tokenRef))
            return 0;

        return transactions
            .Where(t => t.Type == SagaTransactionType.QuestTokenAwarded &&
                       t.GetData<string>(TransactionDataKeys.QuestTokenRef) == tokenRef)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Amount, out var amt) ? amt : 1);
    }

    private static int CountSagaDiscovered(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.SagaDiscovered &&
            (string.IsNullOrEmpty(objective.SagaArcRef) || t.GetData<string>(TransactionDataKeys.SagaArcRef) == objective.SagaArcRef));
    }

    private static int CountLocationReached(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Shipped LocationReached objectives carry no LocationRef — within the quest's
        // arc-scoped log, "reach the location" means any trigger activated in this arc
        return transactions.Any(t =>
            t.Type == SagaTransactionType.TriggerActivated &&
            (string.IsNullOrEmpty(objective.LocationRef) || t.GetData<string>(TransactionDataKeys.SagaTriggerRef) == objective.LocationRef)) ? 1 : 0;
    }

    private static int CountTriggerActivated(QuestObjective objective, List<SagaTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == SagaTransactionType.TriggerActivated &&
            (string.IsNullOrEmpty(objective.TriggerRef) || t.GetData<string>(TransactionDataKeys.SagaTriggerRef) == objective.TriggerRef));
    }

    private static int CountCurrencyCollected(QuestObjective objective, List<SagaTransaction> transactions)
    {
        // Sum all currency gained (from trades, loot, dialogue rewards)
        // Only count positive amounts (gains, not losses)
        return transactions
            .Where(t => t.Type == SagaTransactionType.CurrencyChanged &&
                       t.TryGetData<int>(TransactionDataKeys.Amount, out var amt) && amt > 0)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Amount, out var amt) ? amt : 0);
    }

    private static bool IsCharacterDead(string? characterRef, List<SagaTransaction> transactions)
    {
        if (string.IsNullOrEmpty(characterRef))
            return false;

        return transactions.Any(t =>
            t.Type == SagaTransactionType.CharacterDefeated &&
            t.GetData<string>(TransactionDataKeys.CharacterRef) == characterRef);
    }

    private static bool WasWrongChoiceMade(QuestFailCondition failCondition, List<SagaTransaction> transactions)
    {
        if (string.IsNullOrEmpty(failCondition.DialogueRef) || string.IsNullOrEmpty(failCondition.ChoiceRef))
            return false;

        // ChoiceRef refers to the chosen node's id (see CountDialogueChoiceSelected)
        return transactions.Any(t =>
            t.Type == SagaTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == failCondition.DialogueRef &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == failCondition.ChoiceRef);
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
                       t.GetData<string>(TransactionDataKeys.QuestRef) == questRef)
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

        // Check if avatar had the item at some point (via LootAwarded or QuestTokenAwarded or ItemTraded Buy)
        var hadItem = transactions.Any(t =>
            (t.Type == SagaTransactionType.LootAwarded &&
                LootDegradableKeys.Concat(LootStackableKeys).Any(key =>
                    ParseLootEntries(t, key).Any(e => e.Ref == failCondition.ItemRef))) ||
            (t.Type == SagaTransactionType.QuestTokenAwarded && t.GetData<string>(TransactionDataKeys.QuestTokenRef) == failCondition.ItemRef) ||
            (t.Type == SagaTransactionType.ItemTraded && t.GetData<string>(TransactionDataKeys.ItemRef) == failCondition.ItemRef &&
                t.TryGetData<bool>(TransactionDataKeys.IsBuying, out var bought) && bought));

        if (!hadItem)
            return false;

        // Check if the item was subsequently lost (sold, traded away, or explicitly removed)
        var lostItem = transactions.Any(t =>
            t.Type == SagaTransactionType.ItemTraded &&
            t.GetData<string>(TransactionDataKeys.ItemRef) == failCondition.ItemRef &&
            t.TryGetData<bool>(TransactionDataKeys.IsBuying, out var buying) && !buying);

        return lostItem;
    }

    private static bool HasLeftLocation(
        QuestFailCondition failCondition,
        List<SagaTransaction> transactions,
        string? currentLocationRef)
    {
        if (string.IsNullOrEmpty(failCondition.LocationRef))
            return false;

        // If we have a current location, directly check if avatar is no longer at required location
        if (!string.IsNullOrEmpty(currentLocationRef))
        {
            // Simple string comparison - avatar is not at the required location
            return currentLocationRef != failCondition.LocationRef;
        }

        // Fall back to checking via LocationClaimed transactions (Extension type)
        // Find the most recent location claim
        var lastLocationClaim = transactions
            .Where(t => t.Type == SagaTransactionType.Extension && t.ExtensionTypeName == "LocationClaimed")
            .OrderByDescending(t => t.LocalTimestamp)
            .FirstOrDefault();

        if (lastLocationClaim == null)
            return false; // No location data available

        // Check if the location claim indicates leaving the required area
        var locationRef = lastLocationClaim.GetData<string>(TransactionDataKeys.LocationRef);
        return !string.IsNullOrEmpty(locationRef) && locationRef != failCondition.LocationRef;
    }
}
