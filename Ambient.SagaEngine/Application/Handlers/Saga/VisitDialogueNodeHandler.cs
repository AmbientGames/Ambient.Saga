using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Application.Commands.Saga;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for VisitDialogueNodeCommand.
/// Creates DialogueNodeVisited transaction.
/// </summary>
internal sealed class VisitDialogueNodeHandler : IRequestHandler<VisitDialogueNodeCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public VisitDialogueNodeHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(VisitDialogueNodeCommand command, CancellationToken ct)
    {
        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Create DialogueNodeVisited transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.DialogueNodeVisited,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterRef"] = command.CharacterRef,
                    ["DialogueTreeRef"] = command.DialogueTreeRef,
                    ["DialogueNodeId"] = command.DialogueNodeId
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
            return SagaCommandResult.Failure(Guid.Empty, $"Error visiting dialogue node: {ex.Message}");
        }
    }
}
