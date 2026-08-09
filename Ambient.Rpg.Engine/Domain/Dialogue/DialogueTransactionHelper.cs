using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Domain.Dialogue;

/// <summary>
/// Helper service for creating dialogue-related Arc transactions.
/// Ensures proper idempotency and tracks rewards/actions for achievement progress.
/// </summary>
public static class DialogueTransactionHelper
{
    /// <summary>
    /// Creates a transaction for starting a dialogue conversation.
    /// </summary>
    public static ArcTransaction CreateDialogueStartedTransaction(
        string avatarId,
        string characterRef,
        string dialogueTreeRef,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.DialogueStarted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for visiting a dialogue node.
    /// CRITICAL: This transaction records the INTENT to award items/traits/tokens.
    /// The ArcStateMachine will ensure these rewards are only given on FIRST visit.
    /// </summary>
    /// <param name="avatarId">Avatar visiting the node</param>
    /// <param name="characterRef">Character whose dialogue is being navigated</param>
    /// <param name="dialogueTreeRef">Dialogue tree being navigated</param>
    /// <param name="nodeId">Specific node being visited</param>
    /// <param name="dialogueNode">The actual dialogue node (to extract actions)</param>
    /// <param name="arcInstanceId">Arc instance where this is happening</param>
    public static ArcTransaction CreateDialogueNodeVisitedTransaction(
        string avatarId,
        string characterRef,
        string dialogueTreeRef,
        string nodeId,
        DialogueNode dialogueNode,
        Guid arcInstanceId)
    {
        var transaction = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.DialogueNodeVisited,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef,
                [TransactionDataKeys.DialogueNodeId] = nodeId,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };

        // Extract actions from dialogue node and record them
        // This allows ArcStateMachine to check if rewards were already given
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
    public static ArcTransaction CreateDialogueCompletedTransaction(
        string avatarId,
        string characterRef,
        string dialogueTreeRef,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.DialogueCompleted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for assigning a trait to a character.
    /// </summary>
    public static ArcTransaction CreateTraitAssignedTransaction(
        string avatarId,
        string characterRef,
        string traitType,
        int? traitValue,
        Guid arcInstanceId)
    {
        var transaction = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.TraitAssigned,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.TraitType] = traitType,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
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
    public static ArcTransaction CreateTraitRemovedTransaction(
        string avatarId,
        string characterRef,
        string traitType,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.TraitRemoved,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.TraitType] = traitType,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
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
    public static ArcTransaction CreateQuestTokenAwardedTransaction(
        string avatarId,
        string questTokenRef,
        string sourceRef, // Quest/NPC/trigger that awarded it
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.QuestTokenAwarded,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestTokenRef] = questTokenRef,
                [TransactionDataKeys.SourceRef] = sourceRef,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Checks if a dialogue node has already been visited by checking ArcState.
    /// Returns true if this is a first visit (rewards should be given).
    /// Returns false if already visited (rewards should NOT be given).
    /// </summary>
    public static bool ShouldAwardNodeRewards(
        ArcState arcState,
        string avatarId,
        string characterRef,
        string nodeId)
    {
        var visitKey = $"{avatarId}_{characterRef}_{nodeId}";
        return !arcState.DialogueNodeVisits.ContainsKey(visitKey);
    }

    /// <summary>
    /// Gets the visit count for a specific dialogue node.
    /// Returns 0 if never visited.
    /// </summary>
    public static int GetNodeVisitCount(
        ArcState arcState,
        string avatarId,
        string characterRef,
        string nodeId)
    {
        var visitKey = $"{avatarId}_{characterRef}_{nodeId}";
        return arcState.DialogueNodeVisits.TryGetValue(visitKey, out var visit)
            ? visit.VisitCount
            : 0;
    }

    /// <summary>
    /// Creates a transaction for a party member joining.
    /// </summary>
    public static ArcTransaction CreatePartyMemberJoinedTransaction(
        string avatarId,
        string characterRef,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.PartyMemberJoined,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for a party member leaving.
    /// </summary>
    public static ArcTransaction CreatePartyMemberLeftTransaction(
        string avatarId,
        string characterRef,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.PartyMemberLeft,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for changing faction reputation.
    /// </summary>
    public static ArcTransaction CreateReputationChangedTransaction(
        string avatarId,
        string factionRef,
        int amount,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.ReputationChanged,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = factionRef,
                [TransactionDataKeys.Amount] = amount.ToString(),
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }

    /// <summary>
    /// Creates a transaction for granting a character affinity to the avatar.
    /// </summary>
    public static ArcTransaction CreateAffinityGrantedTransaction(
        string avatarId,
        string affinityRef,
        string capturedFromCharacterRef,
        Guid arcInstanceId)
    {
        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.AffinityGranted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.AffinityRef] = affinityRef,
                [TransactionDataKeys.CapturedFromCharacterRef] = capturedFromCharacterRef,
                [TransactionDataKeys.ArcInstanceId] = arcInstanceId.ToString()
            }
        };
    }
}
