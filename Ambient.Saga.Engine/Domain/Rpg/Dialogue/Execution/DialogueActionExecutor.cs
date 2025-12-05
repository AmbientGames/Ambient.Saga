using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;

namespace Ambient.Saga.Engine.Domain.Rpg.Dialogue.Execution;

/// <summary>
/// Executes dialogue actions (give/take items, unlock achievements, trigger system transitions).
/// Fully data-driven - no special cases needed for new action types.
/// </summary>
public class DialogueActionExecutor
{
    private readonly IDialogueStateProvider _stateProvider;
    private readonly SagaDialogueContext? _sagaContext;
    private readonly List<DialogueSystemEvent> _raisedEvents = new();

    public DialogueActionExecutor(IDialogueStateProvider stateProvider, SagaDialogueContext? sagaContext = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _sagaContext = sagaContext;
    }

    /// <summary>
    /// Events raised during action execution (for system transitions).
    /// </summary>
    public IReadOnlyList<DialogueSystemEvent> RaisedEvents => _raisedEvents.AsReadOnly();

    /// <summary>
    /// Clears all raised events.
    /// Call this after processing events to prepare for next dialogue node.
    /// </summary>
    public void ClearEvents() => _raisedEvents.Clear();

    /// <summary>
    /// Executes a single action.
    /// </summary>
    /// <param name="action">Action to execute</param>
    /// <param name="dialogueTreeRef">Dialogue tree reference (for context)</param>
    /// <param name="nodeId">Node ID (for context)</param>
    /// <param name="characterRef">Character reference (for idempotency checking)</param>
    /// <param name="shouldAwardRewards">Whether rewards should be awarded (checked once per node, not per action)</param>
    public void Execute(DialogueAction action, string dialogueTreeRef, string nodeId, string characterRef, bool shouldAwardRewards)
    {
        switch (action.Type)
        {
            // Quest tokens - IDEMPOTENT (only give on first visit)
            case DialogueActionType.GiveQuestToken:
                if (shouldAwardRewards)
                {
                    _stateProvider.AddQuestToken(action.RefName);

                    // Create transaction for persistence and achievement tracking
                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreateQuestTokenAwardedTransaction(
                            _sagaContext.AvatarId,
                            action.RefName,
                            characterRef, // Source = character who gave the token
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;
            case DialogueActionType.TakeQuestToken:
                if (shouldAwardRewards)
                    _stateProvider.RemoveQuestToken(action.RefName);
                break;

            // Stackable items - IDEMPOTENT (only give on first visit)
            case DialogueActionType.GiveConsumable:
                if (shouldAwardRewards)
                    _stateProvider.AddConsumable(action.RefName, action.Amount);
                break;
            case DialogueActionType.TakeConsumable:
                if (shouldAwardRewards)
                    _stateProvider.RemoveConsumable(action.RefName, action.Amount);
                break;
            case DialogueActionType.GiveMaterial:
                if (shouldAwardRewards)
                    _stateProvider.AddMaterial(action.RefName, action.Amount);
                break;
            case DialogueActionType.TakeMaterial:
                if (shouldAwardRewards)
                    _stateProvider.RemoveMaterial(action.RefName, action.Amount);
                break;
            case DialogueActionType.GiveBlock:
                if (shouldAwardRewards)
                    _stateProvider.AddBlock(action.RefName, action.Amount);
                break;
            case DialogueActionType.TakeBlock:
                if (shouldAwardRewards)
                    _stateProvider.RemoveBlock(action.RefName, action.Amount);
                break;

            // Degradable items - IDEMPOTENT (only give on first visit)
            case DialogueActionType.GiveEquipment:
                if (shouldAwardRewards)
                    _stateProvider.AddEquipment(action.RefName);
                break;
            case DialogueActionType.TakeEquipment:
                if (shouldAwardRewards)
                    _stateProvider.RemoveEquipment(action.RefName);
                break;
            case DialogueActionType.GiveTool:
                if (shouldAwardRewards)
                    _stateProvider.AddTool(action.RefName);
                break;
            case DialogueActionType.TakeTool:
                if (shouldAwardRewards)
                    _stateProvider.RemoveTool(action.RefName);
                break;
            case DialogueActionType.GiveSpell:
                if (shouldAwardRewards)
                    _stateProvider.AddSpell(action.RefName);
                break;
            case DialogueActionType.TakeSpell:
                if (shouldAwardRewards)
                    _stateProvider.RemoveSpell(action.RefName);
                break;

            // Currency - IDEMPOTENT (only transfer on first visit)
            case DialogueActionType.TransferCurrency:
                if (shouldAwardRewards)
                    _stateProvider.TransferCurrency(action.Amount);
                break;

            // Achievements - IDEMPOTENT (only unlock on first visit)
            case DialogueActionType.UnlockAchievement:
                if (shouldAwardRewards)
                    _stateProvider.UnlockAchievement(action.RefName);
                break;

            // System transitions (raise events) - NOT IDEMPOTENT (always trigger)
            // These are UI/system events, not rewards
            case DialogueActionType.OpenMerchantTrade:
                _raisedEvents.Add(new OpenMerchantTradeEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    CharacterRef = action.CharacterRef
                });
                break;

            case DialogueActionType.StartBossBattle:
                _raisedEvents.Add(new StartBossBattleEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    CharacterRef = action.CharacterRef
                });
                break;

            case DialogueActionType.StartCombat:
                _raisedEvents.Add(new StartCombatEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    CharacterRef = action.CharacterRef
                });
                break;

            case DialogueActionType.SpawnCharacters:
                _raisedEvents.Add(new SpawnCharactersEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    CharacterArchetypeRef = action.CharacterArchetypeRef,
                    Amount = action.Amount
                });
                break;

            // Character trait management - IDEMPOTENT (only assign on first visit)
            case DialogueActionType.AssignTrait:
                if (shouldAwardRewards)
                {
                    var traitValue = action.TraitValueSpecified ? (int?)action.TraitValue : null;
                    _stateProvider.AssignTrait(action.Trait.ToString(), traitValue);

                    // Create transaction for persistence and achievement tracking
                    // Traits are assigned TO the character being talked to, not the avatar
                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreateTraitAssignedTransaction(
                            _sagaContext.AvatarId,
                            characterRef, // Character receiving the trait
                            action.Trait.ToString(),
                            traitValue,
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;

            case DialogueActionType.RemoveTrait:
                if (shouldAwardRewards)
                {
                    _stateProvider.RemoveTrait(action.Trait.ToString());

                    // Create transaction for persistence
                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreateTraitRemovedTransaction(
                            _sagaContext.AvatarId,
                            characterRef, // Character losing the trait
                            action.Trait.ToString(),
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;

            // Character state management - IDEMPOTENT (only set on first visit)
            case DialogueActionType.SetCharacterState:
                if (shouldAwardRewards)
                {
                    _stateProvider.SetCharacterState(action.RefName);

                    // Create transaction for persistence
                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreateTraitAssignedTransaction(
                            _sagaContext.AvatarId,
                            characterRef, // Character whose state is changing
                            action.RefName, // State name (Neutral, Friendly, Hostile, InBattle, Defeated)
                            null,
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;

            // Quest management - NOT IDEMPOTENT (always raise events)
            // These trigger CQRS commands which handle the actual quest logic
            case DialogueActionType.AcceptQuest:
                _raisedEvents.Add(new AcceptQuestEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    QuestRef = action.RefName,
                    SagaRef = _sagaContext?.SagaInstance.SagaRef ?? string.Empty,
                    QuestGiverRef = characterRef
                });
                break;

            case DialogueActionType.CompleteQuest:
                _raisedEvents.Add(new CompleteQuestEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    QuestRef = action.RefName,
                    SagaRef = _sagaContext?.SagaInstance.SagaRef ?? string.Empty
                });
                break;

            case DialogueActionType.AbandonQuest:
                _raisedEvents.Add(new AbandonQuestEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    QuestRef = action.RefName,
                    SagaRef = _sagaContext?.SagaInstance.SagaRef ?? string.Empty
                });
                break;

            // Party management - IDEMPOTENT (only modify on first visit)
            case DialogueActionType.JoinParty:
                if (shouldAwardRewards)
                {
                    // Use CharacterRef from action, or fall back to the current dialogue character
                    var joinCharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef;
                    _stateProvider.AddPartyMember(joinCharacterRef);

                    // Raise event for UI/system handling
                    _raisedEvents.Add(new PartyMemberJoinedEvent
                    {
                        DialogueTreeRef = dialogueTreeRef,
                        NodeId = nodeId,
                        CharacterRef = joinCharacterRef
                    });

                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreatePartyMemberJoinedTransaction(
                            _sagaContext.AvatarId,
                            joinCharacterRef,
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;

            case DialogueActionType.LeaveParty:
                if (shouldAwardRewards)
                {
                    // Use CharacterRef from action, or fall back to the current dialogue character
                    var leaveCharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef;
                    _stateProvider.RemovePartyMember(leaveCharacterRef);

                    // Raise event for UI/system handling
                    _raisedEvents.Add(new PartyMemberLeftEvent
                    {
                        DialogueTreeRef = dialogueTreeRef,
                        NodeId = nodeId,
                        CharacterRef = leaveCharacterRef
                    });

                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreatePartyMemberLeftTransaction(
                            _sagaContext.AvatarId,
                            leaveCharacterRef,
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;

            // Faction reputation - IDEMPOTENT (only change on first visit)
            case DialogueActionType.ChangeReputation:
                if (shouldAwardRewards)
                {
                    _stateProvider.ChangeReputation(action.FactionRef, action.Amount);

                    // Raise event for UI/system handling
                    _raisedEvents.Add(new ReputationChangedEvent
                    {
                        DialogueTreeRef = dialogueTreeRef,
                        NodeId = nodeId,
                        FactionRef = action.FactionRef,
                        Amount = action.Amount
                    });

                    if (_sagaContext != null)
                    {
                        var transaction = DialogueTransactionHelper.CreateReputationChangedTransaction(
                            _sagaContext.AvatarId,
                            action.FactionRef,
                            action.Amount,
                            _sagaContext.SagaInstance.InstanceId
                        );
                        _sagaContext.SagaInstance.AddTransaction(transaction);
                    }
                }
                break;

            // Battle-related actions - NOT IDEMPOTENT (always raise events)
            // These are system events that trigger battle mechanics
            case DialogueActionType.ChangeStance:
                _raisedEvents.Add(new ChangeStanceEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    StanceRef = action.RefName,
                    CharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef
                });
                break;

            case DialogueActionType.ChangeAffinity:
                _raisedEvents.Add(new ChangeAffinityEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    AffinityRef = action.RefName,
                    CharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef
                });
                break;

            case DialogueActionType.CastSpell:
                _raisedEvents.Add(new CastSpellEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    SpellRef = action.RefName,
                    CharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef
                });
                break;

            case DialogueActionType.SummonAlly:
                _raisedEvents.Add(new SummonAllyEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    CharacterRef = action.CharacterRef,
                    CharacterArchetypeRef = action.CharacterArchetypeRef
                });
                break;

            case DialogueActionType.EndBattle:
                _raisedEvents.Add(new EndBattleEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    Result = action.RefName // RefName contains the result: "Victory", "Defeat", "Flee", "Draw"
                });
                break;

            case DialogueActionType.HealSelf:
                _raisedEvents.Add(new HealSelfEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    CharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef,
                    Amount = action.Amount
                });
                break;

            case DialogueActionType.ApplyStatusEffect:
                _raisedEvents.Add(new ApplyStatusEffectEvent
                {
                    DialogueTreeRef = dialogueTreeRef,
                    NodeId = nodeId,
                    StatusEffectRef = action.RefName,
                    TargetCharacterRef = !string.IsNullOrEmpty(action.CharacterRef) ? action.CharacterRef : characterRef
                });
                break;

            default:
                throw new NotSupportedException($"Unknown action type: {action.Type}");
        }
    }

    /// <summary>
    /// Executes multiple actions in sequence.
    /// </summary>
    /// <param name="actions">Actions to execute</param>
    /// <param name="dialogueTreeRef">Dialogue tree reference</param>
    /// <param name="nodeId">Node ID</param>
    /// <param name="characterRef">Character reference (for idempotency checking)</param>
    public void ExecuteAll(DialogueAction[] actions, string dialogueTreeRef, string nodeId, string characterRef)
    {
        if (actions == null || actions.Length == 0)
            return;

        // Check idempotency ONCE for all actions in this node
        var shouldAwardRewards = _stateProvider.ShouldAwardNodeRewards(characterRef, nodeId);

        foreach (var action in actions)
        {
            Execute(action, dialogueTreeRef, nodeId, characterRef, shouldAwardRewards);
        }
    }
}
