using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for DamageCharacterCommand.
/// Creates CharacterDamaged transaction.
/// </summary>
internal sealed class DamageCharacterHandler : IRequestHandler<DamageCharacterCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public DamageCharacterHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(DamageCharacterCommand command, CancellationToken ct)
    {
        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Verify Saga and get expanded triggers
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{command.SagaArcRef}' not found");
            }

            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }

            // Replay to get current state
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Verify character exists and is alive
            var characterKey = command.CharacterInstanceId.ToString();
            if (!currentState.Characters.TryGetValue(characterKey, out var character))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character '{command.CharacterInstanceId}' not found");
            }

            if (!character.IsAlive)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Cannot damage dead character");
            }

            // Create CharacterDamaged transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDamaged,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = command.CharacterInstanceId.ToString(),
                    ["Damage"] = command.Damage.ToString(),
                    ["DamageSource"] = command.DamageSource ?? "Unknown"
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

            // Check if character died from this damage
            var newHealth = character.CurrentHealth - command.Damage;
            var resultData = new Dictionary<string, object>
            {
                ["NewHealth"] = newHealth,
                ["CharacterDied"] = newHealth <= 0
            };

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error damaging character: {ex.Message}");
        }
    }
}
