using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for UpdateAvatarPositionCommand.
/// Updates avatar position and checks for Saga discoveries/trigger activations.
/// </summary>
internal sealed class UpdateAvatarPositionHandler : IRequestHandler<UpdateAvatarPositionCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorldStateRepository _worldStateRepository;
    private readonly IWorld _world;

    public UpdateAvatarPositionHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorldStateRepository worldStateRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _worldStateRepository = worldStateRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(UpdateAvatarPositionCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[UpdateAvatarPosition] Called for SagaRef={command.SagaArcRef}, Avatar=({command.Latitude:F6}, {command.Longitude:F6})");

        try
        {
            // Get Saga template
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{command.SagaArcRef}' not found");
            }

            // Get or create Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);
            System.Diagnostics.Debug.WriteLine($"[UpdateAvatarPosition] Got instance {instance.InstanceId}, current tx count: {instance.Transactions.Count}");

            // Get expanded triggers
            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }

            // Create domain service
            var service = new SagaInteractionService(sagaTemplate, expandedTriggers, _world);

            // Convert world coordinates to Saga-relative coordinates
            var (avatarX, avatarZ) = ConvertToSagaRelative(command.Latitude, command.Longitude, sagaTemplate);

            // Create transactions list to track what gets created
            var transactionsBefore = instance.Transactions.Count;

            // Update position (creates transactions internally)
            service.UpdateWithAvatarPosition(instance, avatarX, avatarZ, command.Avatar);

            // Get newly created transactions
            var newTransactions = instance.Transactions.Skip(transactionsBefore).ToList();

            System.Diagnostics.Debug.WriteLine($"[UpdateAvatarPosition] Created {newTransactions.Count} new transactions");
            foreach (var tx in newTransactions)
            {
                System.Diagnostics.Debug.WriteLine($"  - {tx.Type}: {string.Join(", ", tx.Data.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            }

            if (newTransactions.Count == 0)
            {
                // No new transactions = no events triggered
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count);
            }

            // Persist transactions
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(instance.InstanceId, newTransactions, ct);

            // Mark as committed (optimistic concurrency)
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            // Record saga discovery in PlayerDiscovery table for UI visibility
            if (newTransactions.Any(t => t.Type == SagaTransactionType.SagaDiscovered))
            {
                await _worldStateRepository.RecordDiscoveryAsync(
                    command.AvatarId.ToString(),
                    "Saga",
                    command.SagaArcRef);
            }

            // Invalidate read model cache (will be rebuilt on next query)
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Return pure command result - NO STATE DATA
            // Client should use GetAvailableInteractionsQuery to see what happened
            return SagaCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.Last());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error updating avatar position: {ex.Message}");
        }
    }

    private (double x, double z) ConvertToSagaRelative(double latitude, double longitude, SagaArc sagaArc)
    {
        // Convert GPS coordinates to Saga-relative coordinates
        // Saga center is at (0, 0) in Saga-relative space
        var x = CoordinateConverter.LongitudeToSagaRelativeX(longitude, sagaArc.LongitudeX, _world);
        var z = CoordinateConverter.LatitudeToSagaRelativeZ(latitude, sagaArc.LatitudeZ, _world);

        return (x, z);
    }
}
