using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Dialogue;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for CloseDialogueCommand. Seals the current dialogue session by emitting a
/// DialogueCompleted transaction. Safe to call when no session is active (no-op).
/// </summary>
internal sealed class CloseDialogueHandler : IRequestHandler<CloseDialogueCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public CloseDialogueHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(CloseDialogueCommand command, CancellationToken ct)
    {
        try
        {
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
            }

            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{arcRefForLookup}' not found");
            }

            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{arcRefForLookup}'");
            }

            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == command.CharacterInstanceId);
            if (characterState == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character {command.CharacterInstanceId} not found");
            }

            // Find the most recent dialogue session for this character
            var characterDialogueTxs = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var lastStarted = characterDialogueTxs.LastOrDefault(t => t.Type == ArcTransactionType.DialogueStarted);
            var lastCompleted = characterDialogueTxs.LastOrDefault(t => t.Type == ArcTransactionType.DialogueCompleted);

            // No session to close, or the most recent session is already sealed: no-op success.
            if (lastStarted == null ||
                (lastCompleted != null && lastCompleted.SequenceNumber > lastStarted.SequenceNumber))
            {
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count);
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
                new List<ArcTransaction> { sealTx },
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { sealTx.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error closing dialogue: {ex.Message}");
        }
    }
}
