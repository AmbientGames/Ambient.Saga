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
/// Handler for StartDialogueCommand.
/// Creates DialogueStarted transaction.
/// </summary>
internal sealed class StartDialogueHandler : IRequestHandler<StartDialogueCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public StartDialogueHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(StartDialogueCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
            }

            // Verify Arc template exists
            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{arcRefForLookup}' not found");
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Get character state from transaction log replay
            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{command.ArcRef}'");
            }
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Find the character
            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == command.CharacterInstanceId);
            if (characterState == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character {command.CharacterInstanceId} not found");
            }

            // Get character template
            if (!_world.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character template '{characterState.CharacterRef}' not found");
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
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Character '{characterTemplate.RefName}' has no Interactable section defined. Add <Interactable><DialogueTreeRef>...</DialogueTreeRef></Interactable> to the character definition.");
                }

                if (string.IsNullOrEmpty(characterTemplate.Interactable.DialogueTreeRef))
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Character '{characterTemplate.RefName}' has no DialogueTreeRef. Add <DialogueTreeRef>your_dialogue_tree</DialogueTreeRef> to the character's Interactable section.");
                }

                dialogueTreeRef = characterTemplate.Interactable.DialogueTreeRef;
            }

            // Validate the dialogue tree exists
            if (!_world.DialogueTreesLookup.ContainsKey(dialogueTreeRef))
            {
                return ArcCommandResult.Failure(instance.InstanceId,
                    $"Character '{characterTemplate.RefName}' references DialogueTree '{dialogueTreeRef}' which does not exist.");
            }

            // Safety net: if a prior dialogue session for this character was left dangling (no
            // DialogueCompleted), seal it now so session scoping stays clean.
            var transactionsToCommit = new List<ArcTransaction>();
            var characterDialogueTxs = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var lastStarted = characterDialogueTxs.LastOrDefault(t => t.Type == ArcTransactionType.DialogueStarted);
            var lastCompleted = characterDialogueTxs.LastOrDefault(t => t.Type == ArcTransactionType.DialogueCompleted);

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
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.DialogueStarted,
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
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error starting dialogue: {ex.Message}");
        }
    }
}
