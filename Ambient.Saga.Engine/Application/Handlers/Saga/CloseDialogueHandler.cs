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
/// Handler for CloseDialogueCommand. Seals the current dialogue session by emitting a
/// DialogueCompleted transaction. Safe to call when no session is active (no-op).
/// </summary>
internal sealed class CloseDialogueHandler : IRequestHandler<CloseDialogueCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public CloseDialogueHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(CloseDialogueCommand command, CancellationToken ct)
    {
        try
        {
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
            }

            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{sagaRefForLookup}' not found");
            }

            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{sagaRefForLookup}'");
            }

            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == command.CharacterInstanceId);
            if (characterState == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character {command.CharacterInstanceId} not found");
            }

            // Find the most recent dialogue session for this character
            var characterDialogueTxs = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var lastStarted = characterDialogueTxs.LastOrDefault(t => t.Type == SagaTransactionType.DialogueStarted);
            var lastCompleted = characterDialogueTxs.LastOrDefault(t => t.Type == SagaTransactionType.DialogueCompleted);

            // No session to close, or the most recent session is already sealed: no-op success.
            if (lastStarted == null ||
                (lastCompleted != null && lastCompleted.SequenceNumber > lastStarted.SequenceNumber))
            {
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count);
            }

            var dialogueTreeRef = lastStarted.Data.TryGetValue(TransactionDataKeys.DialogueTreeRef, out var treeRef)
                ? treeRef
                : string.Empty;

            var sealTx = DialogueTransactionHelper.CreateDialogueCompletedTransaction(
                command.AvatarId.ToString(),
                characterState.CharacterRef,
                dialogueTreeRef,
                instance.InstanceId
            );

            instance.AddTransaction(sealTx);

            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { sealTx },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { sealTx.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error closing dialogue: {ex.Message}");
        }
    }
}
