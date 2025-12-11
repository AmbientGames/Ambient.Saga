using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

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
            // Verify Saga exists
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{command.SagaArcRef}' not found");
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Get character state from transaction log replay
            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
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

            // Validate the dialogue tree exists
            if (!_world.DialogueTreesLookup.ContainsKey(characterTemplate.Interactable.DialogueTreeRef))
            {
                return SagaCommandResult.Failure(instance.InstanceId,
                    $"Character '{characterTemplate.RefName}' references DialogueTree '{characterTemplate.Interactable.DialogueTreeRef}' which does not exist.");
            }

            // Create DialogueStarted transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.DialogueStarted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = command.CharacterInstanceId.ToString(),
                    ["CharacterRef"] = characterState.CharacterRef,
                    ["DialogueTreeRef"] = characterTemplate.Interactable.DialogueTreeRef
                }
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            // Commit transaction
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
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
