using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for DefeatCharacterCommand.
/// Creates CharacterDefeated transaction.
/// </summary>
internal sealed class DefeatCharacterHandler : IRequestHandler<DefeatCharacterCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public DefeatCharacterHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(DefeatCharacterCommand command, CancellationToken ct)
    {
        try
        {
            // Verify Saga template exists
            if (!_world.SagaArcLookup.ContainsKey(command.SagaArcRef))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{command.SagaArcRef}' not found");
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Validate character exists by checking transactions
            var characterExists = instance.GetCommittedTransactions()
                .Any(t => t.Type == SagaTransactionType.CharacterSpawned &&
                         t.Data.ContainsKey("CharacterInstanceId") &&
                         t.Data["CharacterInstanceId"] == command.CharacterInstanceId.ToString());

            if (!characterExists)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character with instance ID '{command.CharacterInstanceId}' not found");
            }

            // Create CharacterDefeated transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = command.CharacterInstanceId.ToString(),
                    ["VictorAvatarId"] = command.AvatarId.ToString(),
                    ["DefeatMethod"] = command.DefeatMethod ?? "Unknown"
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
            return SagaCommandResult.Failure(Guid.Empty, $"Error defeating character: {ex.Message}");
        }
    }
}
