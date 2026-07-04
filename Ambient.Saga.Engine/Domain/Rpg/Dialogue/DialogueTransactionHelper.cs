using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Dialogue;

/// <summary>
/// Helper service for creating dialogue-related Saga transactions.
/// Ensures proper idempotency and tracks rewards/actions for achievement progress.
/// </summary>
public static class DialogueTransactionHelper
{
    /// <summary>
    /// Creates a transaction for starting a dialogue conversation.
    /// </summary>
    public static SagaTransaction CreateDialogueStartedTransaction(
        string avatarId,
        string characterRef,
        string dialogueTreeRef,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.DialogueStarted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for visiting a dialogue node.
    /// CRITICAL: This transaction records the INTENT to award items/traits/tokens.
    /// The SagaStateMachine will ensure these rewards are only given on FIRST visit.
    /// </summary>
    /// <param name="avatarId">Avatar visiting the node</param>
    /// <param name="characterRef">Character whose dialogue is being navigated</param>
    /// <param name="dialogueTreeRef">Dialogue tree being navigated</param>
    /// <param name="nodeId">Specific node being visited</param>
    /// <param name="dialogueNode">The actual dialogue node (to extract actions)</param>
    /// <param name="sagaInstanceId">Saga instance where this is happening</param>
    public static SagaTransaction CreateDialogueNodeVisitedTransaction(
        string avatarId,
        string characterRef,
        string dialogueTreeRef,
        string nodeId,
        DialogueNode dialogueNode,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.DialogueNodeVisited,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef,
                [TransactionDataKeys.DialogueNodeId] = nodeId,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };

        // Extract actions from dialogue node and record them
        // This allows SagaStateMachine to check if rewards were already given
        if (dialogueNode.Action != null && dialogueNode.Action.Length > 0)
        {
            var itemsAwarded = new List<string>();
            var traitsAssigned = new List<string>();
            var questTokens = new List<string>();
            var currencyTransferred = 0;

            foreach (var action in dialogueNode.Action)
            {
                switch (action.Type)
                {
                    case DialogueActionType.GiveEquipment:
                    case DialogueActionType.GiveTool:
                    case DialogueActionType.GiveSpell:
                    case DialogueActionType.GiveConsumable:
                    case DialogueActionType.GiveMaterial:
                        if (!string.IsNullOrEmpty(action.RefName))
                            itemsAwarded.Add(action.RefName);
                        break;

                    case DialogueActionType.AssignTrait:
                        if (action.TraitSpecified)
                            traitsAssigned.Add(action.Trait.ToString());
                        break;

                    case DialogueActionType.GiveQuestToken:
                        if (!string.IsNullOrEmpty(action.RefName))
                            questTokens.Add(action.RefName);
                        break;

                    case DialogueActionType.TransferCurrency:
                        currencyTransferred += action.Amount;
                        break;
                }
            }

            // Store as comma-separated lists for easy parsing
            if (itemsAwarded.Count > 0)
                transaction.Data[TransactionDataKeys.ItemsAwarded] = string.Join(",", itemsAwarded);

            if (traitsAssigned.Count > 0)
                transaction.Data[TransactionDataKeys.TraitsAssigned] = string.Join(",", traitsAssigned);

            if (questTokens.Count > 0)
                transaction.Data[TransactionDataKeys.QuestTokensAwarded] = string.Join(",", questTokens);

            if (currencyTransferred != 0)
                transaction.Data[TransactionDataKeys.CurrencyTransferred] = currencyTransferred.ToString();
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for completing a dialogue conversation.
    /// </summary>
    public static SagaTransaction CreateDialogueCompletedTransaction(
        string avatarId,
        string characterRef,
        string dialogueTreeRef,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.DialogueCompleted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for assigning a trait to a character.
    /// </summary>
    public static SagaTransaction CreateTraitAssignedTransaction(
        string avatarId,
        string characterRef,
        string traitType,
        int? traitValue,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.TraitAssigned,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.TraitType] = traitType,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };

        if (traitValue.HasValue)
        {
            transaction.Data[TransactionDataKeys.TraitValue] = traitValue.Value.ToString();
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for removing a trait from a character.
    /// </summary>
    public static SagaTransaction CreateTraitRemovedTransaction(
        string avatarId,
        string characterRef,
        string traitType,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.TraitRemoved,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.TraitType] = traitType,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    // CreateItemTradedTransaction was deleted: it had zero callers and wrote drifted
    // keys (CharacterRef/Price/Direction) that no consumer reads — TradeItemHandler
    // owns ItemTraded transactions. (CreateLootAwardedTransaction went with the
    // corpse-looting feature, removed 2026-07-04.)

    /// <summary>
    /// Creates a transaction for awarding a quest token.
    /// </summary>
    public static SagaTransaction CreateQuestTokenAwardedTransaction(
        string avatarId,
        string questTokenRef,
        string sourceRef, // Quest/NPC/trigger that awarded it
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.QuestTokenAwarded,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestTokenRef] = questTokenRef,
                [TransactionDataKeys.SourceRef] = sourceRef,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Checks if a dialogue node has already been visited by checking SagaState.
    /// Returns true if this is a first visit (rewards should be given).
    /// Returns false if already visited (rewards should NOT be given).
    /// </summary>
    public static bool ShouldAwardNodeRewards(
        SagaState sagaState,
        string avatarId,
        string characterRef,
        string nodeId)
    {
        var visitKey = $"{avatarId}_{characterRef}_{nodeId}";
        return !sagaState.DialogueNodeVisits.ContainsKey(visitKey);
    }

    /// <summary>
    /// Gets the visit count for a specific dialogue node.
    /// Returns 0 if never visited.
    /// </summary>
    public static int GetNodeVisitCount(
        SagaState sagaState,
        string avatarId,
        string characterRef,
        string nodeId)
    {
        var visitKey = $"{avatarId}_{characterRef}_{nodeId}";
        return sagaState.DialogueNodeVisits.TryGetValue(visitKey, out var visit)
            ? visit.VisitCount
            : 0;
    }

    /// <summary>
    /// Creates a transaction for a party member joining.
    /// </summary>
    public static SagaTransaction CreatePartyMemberJoinedTransaction(
        string avatarId,
        string characterRef,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.PartyMemberJoined,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for a party member leaving.
    /// </summary>
    public static SagaTransaction CreatePartyMemberLeftTransaction(
        string avatarId,
        string characterRef,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.PartyMemberLeft,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for changing faction reputation.
    /// </summary>
    public static SagaTransaction CreateReputationChangedTransaction(
        string avatarId,
        string factionRef,
        int amount,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ReputationChanged,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = factionRef,
                [TransactionDataKeys.Amount] = amount.ToString(),
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for granting a character affinity to the avatar.
    /// </summary>
    public static SagaTransaction CreateAffinityGrantedTransaction(
        string avatarId,
        string affinityRef,
        string capturedFromCharacterRef,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.AffinityGranted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.AffinityRef] = affinityRef,
                [TransactionDataKeys.CapturedFromCharacterRef] = capturedFromCharacterRef,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }
}
