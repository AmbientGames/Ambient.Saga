using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SubmitBuildingClaimCommand.
/// Validates building rate, reachability, and material availability.
/// </summary>
public class SubmitBuildingClaimHandler : IRequestHandler<SubmitBuildingClaimCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public SubmitBuildingClaimHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(SubmitBuildingClaimCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SubmitBuildingClaim] Avatar {command.AvatarId} placed {command.Claim.BlockCount} blocks at {command.Claim.BuildingRate:F2} blocks/sec");

        try
        {
            // Get or create Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Validate building session (anti-cheat)
            var validation = ValidateBuildingSession(command.Claim);
            if (!validation.IsValid)
            {
                System.Diagnostics.Debug.WriteLine($"[SubmitBuildingClaim] REJECTED: {validation.Reason} (confidence: {validation.CheatConfidence:P0})");
                return SagaCommandResult.Failure(instance.InstanceId, validation.Reason);
            }

            // Create transaction
            var transaction = VoxelTransactionHelper.CreateBuildingSessionClaimedTransaction(
                command.AvatarId.ToString(),
                command.Claim,
                instance.InstanceId);

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

            System.Diagnostics.Debug.WriteLine($"[SubmitBuildingClaim] Building claim accepted (sequence: {sequenceNumbers.First()})");

            if (validation.ValidationWarnings.Count > 0)
            {
                return SagaCommandResult.Success(
                    instance.InstanceId,
                    new List<Guid> { transaction.TransactionId },
                    sequenceNumbers.First(),
                    new Dictionary<string, object>
                    {
                        ["Warnings"] = validation.ValidationWarnings
                    });
            }

            return SagaCommandResult.Success(instance.InstanceId, new List<Guid> { transaction.TransactionId }, sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubmitBuildingClaim] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error submitting building claim: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate building session to detect speed hacks and infinite inventory.
    /// </summary>
    private ClaimValidationResult ValidateBuildingSession(BuildingSessionClaim claim)
    {
        var warnings = new List<string>();

        // === VALIDATION 1: Batch size limit (DOS prevention) ===
        if (claim.BlockCount > VoxelGameConstants.MAX_BLOCKS_PER_BUILDING_CLAIM)
        {
            return ClaimValidationResult.Failure(
                $"Too many blocks in single claim: {claim.BlockCount} (max: {VoxelGameConstants.MAX_BLOCKS_PER_BUILDING_CLAIM})",
                0.80f);
        }

        // === VALIDATION 2: Building rate plausibility ===
        if (claim.BuildingRate > VoxelGameConstants.MAX_BUILDING_RATE_BLOCKS_PER_SECOND)
        {
            return ClaimValidationResult.Failure(
                $"Building too fast: {claim.BuildingRate:F1} blocks/sec (max: {VoxelGameConstants.MAX_BUILDING_RATE_BLOCKS_PER_SECOND})",
                0.90f);
        }

        // Warn if building at suspiciously high (but not impossible) rate
        if (claim.BuildingRate > VoxelGameConstants.MAX_BUILDING_RATE_BLOCKS_PER_SECOND * 0.9f)
        {
            warnings.Add($"High building rate: {claim.BuildingRate:F1} blocks/sec");
        }

        // === VALIDATION 3: Block reachability ===
        foreach (var block in claim.BlocksPlaced)
        {
            // Check distance from start location
            var distanceFromStart = VoxelGameConstants.CalculateDistance(
                claim.StartLocation.X, claim.StartLocation.Y, claim.StartLocation.Z,
                block.Position.X, block.Position.Y, block.Position.Z);

            // Check distance from end location
            var distanceFromEnd = VoxelGameConstants.CalculateDistance(
                claim.EndLocation.X, claim.EndLocation.Y, claim.EndLocation.Z,
                block.Position.X, block.Position.Y, block.Position.Z);

            // Block must be within reach of either start or end position
            var minDistance = Math.Min(distanceFromStart, distanceFromEnd);
            if (minDistance > VoxelGameConstants.MAX_BUILDING_REACH_METERS)
            {
                return ClaimValidationResult.Failure(
                    $"Block at {block.Position} is {minDistance:F1}m away (max reach: {VoxelGameConstants.MAX_BUILDING_REACH_METERS}m)",
                    0.95f);
            }
        }

        // === VALIDATION 4: Material consumption matches blocks placed ===
        var blockTypeCounts = claim.BlocksPlaced
            .GroupBy(b => b.BlockType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Check that materials consumed matches blocks placed
        foreach (var kvp in blockTypeCounts)
        {
            var blockType = kvp.Key;
            var blocksPlaced = kvp.Value;

            // Check if this block type was consumed
            if (!claim.MaterialsConsumed.TryGetValue(blockType, out var materialsConsumed))
            {
                return ClaimValidationResult.Failure(
                    $"Placed {blocksPlaced} {blockType} blocks but consumed 0 materials",
                    0.90f);
            }

            // Materials consumed should equal blocks placed
            if (materialsConsumed != blocksPlaced)
            {
                return ClaimValidationResult.Failure(
                    $"Material mismatch for {blockType}: placed {blocksPlaced} blocks but consumed {materialsConsumed} materials",
                    0.85f);
            }
        }

        // Check for materials consumed that weren't placed (suspicious)
        foreach (var kvp in claim.MaterialsConsumed)
        {
            if (!blockTypeCounts.ContainsKey(kvp.Key))
            {
                warnings.Add($"Consumed {kvp.Value} {kvp.Key} materials but didn't place any blocks of that type");
            }
        }

        // === VALIDATION 5: Movement during building ===
        var movementDistance = VoxelGameConstants.CalculateDistance(
            claim.StartLocation.X, claim.StartLocation.Y, claim.StartLocation.Z,
            claim.EndLocation.X, claim.EndLocation.Y, claim.EndLocation.Z);
        var movementSpeed = claim.DurationSeconds > 0 ? movementDistance / claim.DurationSeconds : 0;

        if (movementSpeed > VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND)
        {
            return ClaimValidationResult.Failure(
                $"Movement during building too fast: {movementSpeed:F1} m/s (max: {VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND})",
                0.85f);
        }

        // Return success (possibly with warnings)
        if (warnings.Count > 0)
        {
            return new ClaimValidationResult
            {
                IsValid = true,
                Reason = "Valid with warnings",
                CheatConfidence = 0.3f,
                ValidationWarnings = warnings
            };
        }

        return ClaimValidationResult.Success();
    }
}
