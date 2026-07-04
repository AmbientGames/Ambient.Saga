using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for StartDialogueCommand.
/// Creates DialogueStarted transaction.
/// </summary>
internal sealed class StartDialogueHandler : IRequestHandler<StartDialogueCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public StartDialogueHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(StartDialogueCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
            }

            // Verify Saga template exists
            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{sagaRefForLookup}' not found");
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Get character state from transaction log replay
            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Find the character
            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == command.CharacterInstanceId);
            if (characterState == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character {command.CharacterInstanceId} not found");
            }

            // Get character template
            if (!_world.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character template '{characterState.CharacterRef}' not found");
            }

            // Resolve the tree: an explicit override (battle dialogue triggers open
            // their own battle trees) or the character's Interactable default
            string dialogueTreeRef;
            if (!string.IsNullOrEmpty(command.DialogueTreeRefOverride))
            {
                dialogueTreeRef = command.DialogueTreeRefOverride;
            }
            else
            {
                // Check if character has dialogue
                if (characterTemplate.Interactable == null)
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Character '{characterTemplate.RefName}' has no Interactable section defined. Add <Interactable><DialogueTreeRef>...</DialogueTreeRef></Interactable> to the character definition.");
                }

                if (string.IsNullOrEmpty(characterTemplate.Interactable.DialogueTreeRef))
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Character '{characterTemplate.RefName}' has no DialogueTreeRef. Add <DialogueTreeRef>your_dialogue_tree</DialogueTreeRef> to the character's Interactable section.");
                }

                dialogueTreeRef = characterTemplate.Interactable.DialogueTreeRef;
            }

            // Validate the dialogue tree exists
            if (!_world.DialogueTreesLookup.ContainsKey(dialogueTreeRef))
            {
                return SagaCommandResult.Failure(instance.InstanceId,
                    $"Character '{characterTemplate.RefName}' references DialogueTree '{dialogueTreeRef}' which does not exist.");
            }

            // Safety net: if a prior dialogue session for this character was left dangling (no
            // DialogueCompleted), seal it now so session scoping stays clean.
            var transactionsToCommit = new List<SagaTransaction>();
            var characterDialogueTxs = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var lastStarted = characterDialogueTxs.LastOrDefault(t => t.Type == SagaTransactionType.DialogueStarted);
            var lastCompleted = characterDialogueTxs.LastOrDefault(t => t.Type == SagaTransactionType.DialogueCompleted);

            bool priorSessionDangling = lastStarted != null &&
                (lastCompleted == null || lastCompleted.SequenceNumber < lastStarted.SequenceNumber);

            if (priorSessionDangling && lastStarted != null)
            {
                var priorTreeRef = lastStarted.Data.TryGetValue(TransactionDataKeys.DialogueTreeRef, out var ptr) ? ptr : string.Empty;
                var sealTx = DialogueTransactionHelper.CreateDialogueCompletedTransaction(
                    command.AvatarId.ToString(),
                    characterState.CharacterRef,
                    priorTreeRef,
                    instance.InstanceId
                );
                instance.AddTransaction(sealTx);
                transactionsToCommit.Add(sealTx);
            }

            // Create DialogueStarted transaction (the session's tree — and start node,
            // when overridden — ride on it; the session handlers read them back)
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.DialogueStarted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.CharacterInstanceId] = command.CharacterInstanceId.ToString(),
                    [TransactionDataKeys.CharacterRef] = characterState.CharacterRef,
                    [TransactionDataKeys.DialogueTreeRef] = dialogueTreeRef
                }
            };

            if (!string.IsNullOrEmpty(command.StartNodeIdOverride))
            {
                transaction.Data[TransactionDataKeys.NodeId] = command.StartNodeIdOverride;
            }

            instance.AddTransaction(transaction);
            transactionsToCommit.Add(transaction);

            // Persist and commit transaction(s)
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactionsToCommit,
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error starting dialogue: {ex.Message}");
        }
    }
}
