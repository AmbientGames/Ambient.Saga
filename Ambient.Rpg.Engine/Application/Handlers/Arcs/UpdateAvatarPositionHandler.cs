using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for UpdateAvatarPositionCommand.
/// Updates avatar position and checks for Arc discoveries/trigger activations.
/// </summary>
internal sealed class UpdateAvatarPositionHandler : IRequestHandler<UpdateAvatarPositionCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorldStateRepository _worldStateRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IWorld _world;

    public UpdateAvatarPositionHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorldStateRepository worldStateRepository,
        IAvatarProgressRepository avatarProgressRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _worldStateRepository = worldStateRepository;
        _avatarProgressRepository = avatarProgressRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(UpdateAvatarPositionCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[UpdateAvatarPosition] Called for ArcRef={command.ArcRef}, Avatar=({command.Latitude:F6}, {command.Longitude:F6})");

        try
        {
            // Get Arc template
            if (!_world.ArcLookup.TryGetValue(command.ArcRef, out var arcTemplate))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{command.ArcRef}' not found");
            }

            // Get or create Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);
            System.Diagnostics.Debug.WriteLine($"[UpdateAvatarPosition] Got instance {instance.InstanceId}, current tx count: {instance.Transactions.Count}");

            // Get expanded triggers
            if (!_world.ArcTriggersLookup.TryGetValue(command.ArcRef, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{command.ArcRef}'");
            }

            // Create domain service
            var service = new ArcInteractionService(arcTemplate, expandedTriggers, _world);

            // Convert world coordinates to Arc-relative coordinates
            var (avatarX, avatarZ) = ConvertToArcRelative(command.Latitude, command.Longitude, arcTemplate);

            // Create transactions list to track what gets created
            var transactionsBefore = instance.Transactions.Count;

            // Update position (creates transactions internally). The progress repository
            // gives trigger gating the cross-arc quest-token table — tokens are awarded
            // by other arcs, so per-instance AwardedQuestTokens alone can never satisfy
            // a RequiresQuestTokenRef authored against a different arc's token.
            service.UpdateWithAvatarPosition(instance, avatarX, avatarZ, command.Avatar, _avatarProgressRepository);

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
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count);
            }

            // Persist and commit transactions
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            // Record arc discovery in AvatarDiscovery table for UI visibility
            if (newTransactions.Any(t => t.Type == ArcTransactionType.ArcDiscovered))
            {
                await _worldStateRepository.RecordDiscoveryAsync(
                    command.AvatarId.ToString(),
                    "Arc",
                    command.ArcRef);
            }

            // Invalidate read model cache (will be rebuilt on next query)
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // Return pure command result - NO STATE DATA
            // Client should use GetAvailableInteractionsQuery to see what happened
            return ArcCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.Last());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error updating avatar position: {ex.Message}");
        }
    }

    private (double x, double z) ConvertToArcRelative(double latitude, double longitude, Arc arc)
    {
        // Convert GPS coordinates to Arc-relative coordinates
        // Arc center is at (0, 0) in Arc-relative space
        var x = CoordinateConverter.LongitudeToArcRelativeX(longitude, arc.Longitude, _world);
        var z = CoordinateConverter.LatitudeToArcRelativeZ(latitude, arc.Latitude, _world);

        return (x, z);
    }

}
