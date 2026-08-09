using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Domain.Quests;

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
        List<ArcTransaction> transactions,
        IWorld world)
    {
        var committedTransactions = transactions
            .Where(t => t.Status == TransactionStatus.Committed)
            .ToList();
        var relevantTransactions = ScopeToCurrentAcceptance(quest, committedTransactions);

        return objective.Type switch
        {
            QuestObjectiveType.CharacterDefeated => CountCharacterDefeated(objective, relevantTransactions),
            QuestObjectiveType.CharactersDefeatedByTrait => CountCharacterDefeatedByTrait(objective, relevantTransactions, committedTransactions, world),
            QuestObjectiveType.CharactersDefeatedAtTrigger => CountCharacterDefeatedAtTrigger(objective, relevantTransactions, transactions),

            QuestObjectiveType.DialogueCompleted => CountDialogueCompleted(objective, relevantTransactions),
            QuestObjectiveType.DialogueChoiceSelected => CountDialogueChoiceSelected(objective, relevantTransactions),
            QuestObjectiveType.DialogueNodeVisited => CountDialogueNodeVisited(objective, relevantTransactions),

            QuestObjectiveType.ItemCollected => CountItemCollected(objective, relevantTransactions),
            QuestObjectiveType.ItemDelivered => CountItemDelivered(objective, relevantTransactions, committedTransactions),
            QuestObjectiveType.ItemTraded => CountItemTraded(objective, relevantTransactions),

            // DESIGN RULING (2026-07-12): quest tokens are permanent world-knowledge
            // facts, so QuestTokenCollected counts the FULL committed log, not the
            // acceptance scope. Their producers (dialogue GiveQuestToken, ...) are
            // first-visit-EVER idempotent — the DialogueNodeVisits ledger never
            // resets — so a token excluded by an abandon + re-accept could never be
            // re-earned and the objective soft-locked forever (e.g. EverestTrail
            // HiddenCache_Quest). Tokens alone bridge abandon/re-accept; every other
            // objective type stays scoped to the current acceptance.
            QuestObjectiveType.QuestTokenCollected => CountQuestTokenCollected(objective, committedTransactions),

            QuestObjectiveType.ArcDiscovered => CountArcDiscovered(objective, relevantTransactions),
            QuestObjectiveType.LocationReached => CountLocationReached(objective, relevantTransactions, world),
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
        List<ArcTransaction> transactions,
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
        List<ArcTransaction> transactions,
        IWorld world)
    {
        // If stage has branches, check if a branch was chosen — scoped to the
        // current acceptance so a branch chosen before an abandon + re-accept
        // doesn't mark the new run's stage as already complete
        if (stage.Branches != null)
        {
            return ScopeToCurrentAcceptance(quest, transactions).Any(t =>
                t.Type == ArcTransactionType.QuestBranchChosen &&
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
        List<ArcTransaction> transactions)
    {
        // If stage has branches, check which branch was chosen and use its NextStage —
        // scoped to the current acceptance so a previous run's choice doesn't route
        // this one (see ScopeToCurrentAcceptance)
        if (currentStage.Branches != null)
        {
            var branchTransaction = ScopeToCurrentAcceptance(quest, transactions)
                .Where(t => t.Type == ArcTransactionType.QuestBranchChosen &&
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
    /// Restricts evaluation to transactions from the quest's CURRENT acceptance.
    /// Without this, abandon-then-re-accept inherited all prior progress
    /// (objectives instantly complete, stage rewards granted again, old branch
    /// choices auto-"chosen"). Public so command handlers can scope their own
    /// log queries (e.g. branch exclusivity) with the same rule.
    ///
    /// The scope starts at the beginning of the dialogue session the acceptance
    /// came from, not at the QuestAccepted transaction itself: the dialogue
    /// handlers stage node-action transactions (DialogueNodeVisited, ...) before
    /// they dispatch the nested AcceptQuestCommand, so events produced by the
    /// very node that accepts the quest end up with a LOWER sequence number
    /// than QuestAccepted — scoping strictly from the acceptance would exclude
    /// them forever (node visits are first-visit-only, so they can't recur).
    /// Acceptances that did not come from a dialogue session keep the strict
    /// from-QuestAccepted scope.
    ///
    /// NOTE: QuestTokenCollected objectives deliberately do NOT use this scope —
    /// quest tokens are permanent world-knowledge facts and count from the full
    /// committed log (see EvaluateObjectiveProgress).
    /// </summary>
    public static List<ArcTransaction> ScopeToCurrentAcceptance(Quest quest, List<ArcTransaction> transactions)
    {
        var latestAccept = transactions
            .Where(t => t.Type == ArcTransactionType.QuestAccepted &&
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
        ArcTransaction latestAccept,
        List<ArcTransaction> transactions)
    {
        var giverRef = latestAccept.GetData<string>(TransactionDataKeys.QuestGiverRef);

        var prior = transactions
            .Where(t => t.SequenceNumber < latestAccept.SequenceNumber)
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        var sessionStart = prior.LastOrDefault(t =>
            t.Type == ArcTransactionType.DialogueStarted &&
            (string.IsNullOrEmpty(giverRef) ||
             t.GetData<string>(TransactionDataKeys.CharacterRef) == giverRef));

        if (sessionStart == null)
            return null;

        var sessionCharacterRef = sessionStart.GetData<string>(TransactionDataKeys.CharacterRef);

        var sessionCompletion = prior.LastOrDefault(t =>
            t.Type == ArcTransactionType.DialogueCompleted &&
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
                      !(t.Type == ArcTransactionType.QuestAbandoned &&
                        t.GetData<string>(TransactionDataKeys.QuestRef) == questRef));

        return acceptanceBelongsToSealedSession ? sessionStart.SequenceNumber : null;
    }

    /// <summary>
    /// Transaction types the dialogue handlers' quest-event dispatch (and the
    /// quest pipeline behaviors it triggers) can emit between a terminal node's
    /// DialogueCompleted and a QuestAccepted from that same node.
    /// </summary>
    private static readonly HashSet<ArcTransactionType> QuestLifecycleTransactionTypes = new()
    {
        ArcTransactionType.QuestAccepted,
        ArcTransactionType.QuestCompleted,
        ArcTransactionType.QuestAbandoned,
        ArcTransactionType.QuestStageAdvanced,
        ArcTransactionType.QuestObjectiveCompleted,
        ArcTransactionType.QuestBranchChosen,
        ArcTransactionType.ReputationChanged
    };

    // ===== Private Helper Methods =====

    private static int CountCharacterDefeated(QuestObjective objective, List<ArcTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == ArcTransactionType.CharacterDefeated &&
            (string.IsNullOrEmpty(objective.CharacterRef) || t.GetData<string>(TransactionDataKeys.CharacterRef) == objective.CharacterRef));
    }

    private static int CountCharacterDefeatedByTrait(
        QuestObjective objective,
        List<ArcTransaction> scopedTransactions,
        List<ArcTransaction> allCommittedTransactions,
        IWorld world)
    {
        if (!objective.TraitSpecified)
            return 0;

        // A defeat counts when the character carried the trait AT THE DEFEAT —
        // template traits alone would miss a character turned e.g. Hostile at play
        // time (dialogue AssignTrait / battle-end TraitAssigned). The runtime trait
        // timeline comes from the FULL committed log (trait changes are world state,
        // not quest progress); only the DEFEATS are acceptance-scoped.
        return scopedTransactions.Count(t =>
            t.Type == ArcTransactionType.CharacterDefeated &&
            CharacterCarriedTraitAtDefeat(world, allCommittedTransactions, t, objective.Trait));
    }

    private static bool CharacterCarriedTraitAtDefeat(
        IWorld world,
        List<ArcTransaction> allCommittedTransactions,
        ArcTransaction defeat,
        CharacterTraitType trait)
    {
        var characterRef = defeat.GetData<string>(TransactionDataKeys.CharacterRef);
        if (string.IsNullOrEmpty(characterRef))
            return false;

        // Latest runtime trait event at or before the defeat wins ("at or before":
        // battle-end emitters commit the trait change in the same batch as the
        // defeat); with no runtime event, the template decides.
        var traitName = trait.ToString();
        var lastTraitEvent = allCommittedTransactions
            .Where(t => (t.Type == ArcTransactionType.TraitAssigned || t.Type == ArcTransactionType.TraitRemoved) &&
                        t.SequenceNumber <= defeat.SequenceNumber &&
                        t.GetData<string>(TransactionDataKeys.CharacterRef) == characterRef &&
                        t.GetData<string>(TransactionDataKeys.TraitType) == traitName)
            .OrderByDescending(t => t.SequenceNumber)
            .FirstOrDefault();

        if (lastTraitEvent != null)
            return lastTraitEvent.Type == ArcTransactionType.TraitAssigned;

        return world.CharactersLookup.TryGetValue(characterRef, out var character) &&
               character.CarriesTrait(trait);
    }

    private static int CountCharacterDefeatedAtTrigger(QuestObjective objective, List<ArcTransaction> scopedTransactions, List<ArcTransaction> allTransactions)
    {
        if (string.IsNullOrEmpty(objective.TriggerRef))
            return 0;

        // Spawn records carry the trigger, defeats carry the character instance —
        // join on CharacterInstanceId so only kills at this site count. The spawn
        // set is built from the FULL committed log, not the acceptance scope:
        // spawning is quest-independent (a guardian spawned before the quest was
        // accepted must still count when defeated after acceptance, or the
        // objective soft-locks for non-respawning characters). Only the DEFEATS
        // are acceptance-scoped.
        var instancesFromTrigger = new HashSet<string>();
        foreach (var t in allTransactions)
        {
            if (t.Status == TransactionStatus.Committed &&
                t.Type == ArcTransactionType.CharacterSpawned &&
                t.GetData<string>(TransactionDataKeys.ArcTriggerRef) == objective.TriggerRef)
            {
                var instanceId = t.GetData<string>(TransactionDataKeys.CharacterInstanceId);
                if (!string.IsNullOrEmpty(instanceId))
                    instancesFromTrigger.Add(instanceId);
            }
        }

        // Common case: the player has never been near the trigger — skip the defeat scan
        if (instancesFromTrigger.Count == 0)
            return 0;

        return scopedTransactions.Count(t =>
            t.Type == ArcTransactionType.CharacterDefeated &&
            instancesFromTrigger.Contains(t.GetData<string>(TransactionDataKeys.CharacterInstanceId) ?? ""));
    }

    private static int CountDialogueCompleted(QuestObjective objective, List<ArcTransaction> transactions)
    {
        // Content filters by DialogueRef or by CharacterRef ("speak with X"); honor both
        return transactions.Count(t =>
            t.Type == ArcTransactionType.DialogueCompleted &&
            (string.IsNullOrEmpty(objective.DialogueRef) || t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == objective.DialogueRef) &&
            (string.IsNullOrEmpty(objective.CharacterRef) || t.GetData<string>(TransactionDataKeys.CharacterRef) == objective.CharacterRef));
    }

    private static int CountDialogueChoiceSelected(QuestObjective objective, List<ArcTransaction> transactions)
    {
        // Choices have no identity of their own — selecting one is recorded as a visit
        // to its target node, so ChoiceRef refers to the chosen node's id
        return transactions.Count(t =>
            t.Type == ArcTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == objective.DialogueRef &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == objective.ChoiceRef);
    }

    private static int CountDialogueNodeVisited(QuestObjective objective, List<ArcTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == ArcTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueTreeRef) == objective.DialogueRef &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == objective.NodeRef);
    }

    private static int CountItemCollected(QuestObjective objective, List<ArcTransaction> transactions)
    {
        // "Collected" means acquired, not specifically looted — merchant purchases
        // count too. Several gather objectives name materials (Screws, Mortar, ...)
        // that exist only in trade, so loot-only counting left them at 0 forever.
        //
        // Counting acquisitions ALONE lets a buy-sell-rebuy cycle double-count, so
        // trades away deduct — but the measure is the PEAK net count reached within
        // the scope, not the final net: a same-stage "collect N, deliver N" pair
        // must not un-complete the collect objective the moment the delivery
        // happens, and selling items owned from BEFORE the scope must not push the
        // counter negative (the running net is floored at zero).
        var net = 0;
        var peak = 0;
        foreach (var t in transactions.OrderBy(t => t.SequenceNumber))
        {
            var delta = t.Type switch
            {
                ArcTransactionType.LootAwarded => CountLootedItems(t, objective.ItemRef),
                ArcTransactionType.ItemTraded when
                        t.GetData<string>(TransactionDataKeys.ItemRef) == objective.ItemRef &&
                        t.TryGetData<bool>(TransactionDataKeys.IsBuying, out var isBuying)
                    => (t.TryGetData<int>(TransactionDataKeys.Quantity, out var qty) ? qty : 1)
                       * (isBuying ? 1 : -1),
                _ => 0
            };

            net = Math.Max(0, net + delta);
            peak = Math.Max(peak, net);
        }

        return peak;
    }

    /// <summary>
    /// Items of the given ref inside one LootAwarded transaction. LootAwarded is
    /// retired (corpse looting removed 2026-07-04; no producer remains) but
    /// historical transactions are still counted. It packs per-family lists:
    /// "Ref:Condition" for degradables (one entry per item), "Ref:Quantity" for
    /// stackables.
    /// </summary>
    private static int CountLootedItems(ArcTransaction t, string? itemRef)
    {
        var count = 0;
        foreach (var key in LootDegradableKeys)
            count += ParseLootEntries(t, key).Count(e => e.Ref == itemRef);
        foreach (var key in LootStackableKeys)
            count += ParseLootEntries(t, key)
                .Where(e => e.Ref == itemRef)
                .Sum(e => Math.Max(1, (int)e.Value));
        return count;
    }

    private static readonly string[] LootDegradableKeys =
        { TransactionDataKeys.Equipment, TransactionDataKeys.Spells, TransactionDataKeys.Tools };

    private static readonly string[] LootStackableKeys =
        { TransactionDataKeys.Consumables, TransactionDataKeys.Blocks, TransactionDataKeys.BuildingMaterials };

    private static IEnumerable<(string Ref, float Value)> ParseLootEntries(ArcTransaction t, string dataKey)
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

    private static int CountItemDelivered(
        QuestObjective objective,
        List<ArcTransaction> scopedTransactions,
        List<ArcTransaction> allCommittedTransactions)
    {
        // Count items traded away (IsBuying == false means the avatar sold/gave the
        // item). When the objective names a recipient (CharacterRef), only trades
        // with THAT character count — ItemTraded records the counterparty as a
        // CharacterInstanceId, so resolve instances back to their template ref via
        // the CharacterSpawned records. The spawn set comes from the FULL committed
        // log (spawning is quest-independent, same rule as
        // CountCharacterDefeatedAtTrigger); a trade whose instance can't be resolved
        // fails closed when a recipient is required.
        HashSet<string>? recipientInstanceIds = null;
        if (!string.IsNullOrEmpty(objective.CharacterRef))
        {
            recipientInstanceIds = new HashSet<string>();
            foreach (var t in allCommittedTransactions)
            {
                if (t.Type == ArcTransactionType.CharacterSpawned &&
                    t.GetData<string>(TransactionDataKeys.CharacterRef) == objective.CharacterRef)
                {
                    var instanceId = t.GetData<string>(TransactionDataKeys.CharacterInstanceId);
                    if (!string.IsNullOrEmpty(instanceId))
                        recipientInstanceIds.Add(instanceId);
                }
            }
        }

        return scopedTransactions
            .Where(t => t.Type == ArcTransactionType.ItemTraded &&
                       t.GetData<string>(TransactionDataKeys.ItemRef) == objective.ItemRef &&
                       t.TryGetData<bool>(TransactionDataKeys.IsBuying, out var isBuying) && !isBuying &&
                       (recipientInstanceIds == null ||
                        recipientInstanceIds.Contains(t.GetData<string>(TransactionDataKeys.CharacterInstanceId) ?? "")))
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Quantity, out var qty) ? qty : 1);
    }

    private static int CountItemTraded(QuestObjective objective, List<ArcTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == ArcTransactionType.ItemTraded &&
            (string.IsNullOrEmpty(objective.ItemRef) || t.GetData<string>(TransactionDataKeys.ItemRef) == objective.ItemRef));
    }

    private static int CountQuestTokenCollected(QuestObjective objective, List<ArcTransaction> transactions)
    {
        // Shipped quest templates author the token under ItemRef instead of
        // QuestTokenRef (e.g. MAIN_QUEST_05/COLLECT_TOKENS) — honor both, else
        // those objectives sit at 0 forever
        var tokenRef = !string.IsNullOrEmpty(objective.QuestTokenRef) ? objective.QuestTokenRef : objective.ItemRef;
        if (string.IsNullOrEmpty(tokenRef))
            return 0;

        return transactions
            .Where(t => t.Type == ArcTransactionType.QuestTokenAwarded &&
                       t.GetData<string>(TransactionDataKeys.QuestTokenRef) == tokenRef)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Amount, out var amt) ? amt : 1);
    }

    private static int CountArcDiscovered(QuestObjective objective, List<ArcTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == ArcTransactionType.ArcDiscovered &&
            (string.IsNullOrEmpty(objective.ArcRef) || t.GetData<string>(TransactionDataKeys.ArcRef) == objective.ArcRef));
    }

    private static int CountLocationReached(QuestObjective objective, List<ArcTransaction> transactions, IWorld world)
    {
        // An authored LocationRef is the finest-grained discriminator and wins outright.
        if (!string.IsNullOrEmpty(objective.LocationRef))
        {
            return transactions.Any(t =>
                t.Type == ArcTransactionType.TriggerActivated &&
                t.GetData<string>(TransactionDataKeys.ArcTriggerRef) == objective.LocationRef) ? 1 : 0;
        }

        // Shipped Trail content authors LocationReached with only ArcRef (the
        // TARGET arc): "reach the location" means activating one of that arc's
        // triggers. The log here is CROSS-ARC (see CrossArcQuestTransactionLog), so
        // without this filter any trigger anywhere would satisfy the objective. An
        // unresolvable arc ref fails closed — the strict content gate owns catching
        // the bad ref; degrading to any-trigger would reintroduce the bug silently.
        if (!string.IsNullOrEmpty(objective.ArcRef))
        {
            if (!world.ArcTriggersLookup.TryGetValue(objective.ArcRef, out var arcTriggers))
                return 0;

            var arcTriggerRefs = new HashSet<string>(arcTriggers.Select(trigger => trigger.RefName));
            return transactions.Any(t =>
                t.Type == ArcTransactionType.TriggerActivated &&
                t.GetData<string>(TransactionDataKeys.ArcTriggerRef) is string activatedRef &&
                arcTriggerRefs.Contains(activatedRef)) ? 1 : 0;
        }

        // No discriminator at all (stock Ise/Kagoshima main-chain and CONNECT
        // quests): any trigger activation counts, as it always has.
        return transactions.Any(t => t.Type == ArcTransactionType.TriggerActivated) ? 1 : 0;
    }

    private static int CountTriggerActivated(QuestObjective objective, List<ArcTransaction> transactions)
    {
        return transactions.Count(t =>
            t.Type == ArcTransactionType.TriggerActivated &&
            (string.IsNullOrEmpty(objective.TriggerRef) || t.GetData<string>(TransactionDataKeys.ArcTriggerRef) == objective.TriggerRef));
    }

    private static int CountCurrencyCollected(QuestObjective objective, List<ArcTransaction> transactions)
    {
        // Sum all currency gained (from trades, loot, dialogue rewards)
        // Only count positive amounts (gains, not losses)
        return transactions
            .Where(t => t.Type == ArcTransactionType.CurrencyChanged &&
                       t.TryGetData<int>(TransactionDataKeys.Amount, out var amt) && amt > 0)
            .Sum(t => t.TryGetData<int>(TransactionDataKeys.Amount, out var amt) ? amt : 0);
    }

}
