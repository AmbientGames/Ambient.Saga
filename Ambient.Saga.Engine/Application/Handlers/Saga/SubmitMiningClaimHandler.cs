using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SubmitMiningClaimCommand.
/// Validates mining rate, reachability, tool wear, and rare ore distribution.
/// Most comprehensive anti-cheat validation.
/// </summary>
public class SubmitMiningClaimHandler : IRequestHandler<SubmitMiningClaimCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public SubmitMiningClaimHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(SubmitMiningClaimCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SubmitMiningClaim] Avatar {command.AvatarId} mined {command.Claim.BlockCount} blocks at {command.Claim.MiningRate:F2} blocks/sec");

        try
        {
            // Get or create Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Validate mining session (anti-cheat)
            var validation = ValidateMiningSession(command.Claim);
            if (!validation.IsValid)
            {
                System.Diagnostics.Debug.WriteLine($"[SubmitMiningClaim] REJECTED: {validation.Reason} (confidence: {validation.CheatConfidence:P0})");
                return SagaCommandResult.Failure(instance.InstanceId, validation.Reason);
            }

            // Create transaction
            var transaction = VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
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

            System.Diagnostics.Debug.WriteLine($"[SubmitMiningClaim] Mining claim accepted (sequence: {sequenceNumbers.First()})");

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
            System.Diagnostics.Debug.WriteLine($"[SubmitMiningClaim] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error submitting mining claim: {ex.Message}");
        }
    }

    /// <summary>
    /// Comprehensive mining validation to detect multiple cheat types.
    /// </summary>
    private ClaimValidationResult ValidateMiningSession(MiningSessionClaim claim)
    {
        var warnings = new List<string>();

        // === VALIDATION 1: Batch size limit (DOS prevention) ===
        if (claim.BlockCount > VoxelGameConstants.MAX_BLOCKS_PER_MINING_CLAIM)
        {
            return ClaimValidationResult.Failure(
                $"Too many blocks in single claim: {claim.BlockCount} (max: {VoxelGameConstants.MAX_BLOCKS_PER_MINING_CLAIM})",
                0.80f);
        }

        // === VALIDATION 2: Mining rate plausibility ===
        if (claim.MiningRate > VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND)
        {
            return ClaimValidationResult.Failure(
                $"Mining too fast: {claim.MiningRate:F1} blocks/sec (max: {VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND})",
                0.90f);
        }

        // Warn if mining at suspiciously high (but not impossible) rate
        if (claim.MiningRate > VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND * 0.9f)
        {
            warnings.Add($"High mining rate: {claim.MiningRate:F1} blocks/sec");
        }

        // === VALIDATION 3: Block reachability ===
        foreach (var block in claim.BlocksMined)
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
            if (minDistance > VoxelGameConstants.MAX_MINING_REACH_METERS)
            {
                return ClaimValidationResult.Failure(
                    $"Block at {block.Position} is {minDistance:F1}m away (max reach: {VoxelGameConstants.MAX_MINING_REACH_METERS}m)",
                    0.95f);
            }
        }

        // === VALIDATION 4: Tool wear plausibility ===
        var toolWearDelta = claim.ToolConditionBefore - claim.ToolConditionAfter;
        if (toolWearDelta < 0)
        {
            return ClaimValidationResult.Failure(
                $"Tool condition increased during mining (was {claim.ToolConditionBefore:F4}, now {claim.ToolConditionAfter:F4})",
                0.99f);
        }

        // Calculate expected wear (average across all block types mined)
        var blockTypeCounts = claim.BlocksMined
            .GroupBy(b => b.BlockType)
            .ToDictionary(g => g.Key, g => g.Count());

        var expectedTotalWear = 0.0f;
        foreach (var kvp in blockTypeCounts)
        {
            var expectedWearPerBlock = VoxelGameConstants.GetExpectedToolWear(claim.ToolRef, kvp.Key);
            expectedTotalWear += expectedWearPerBlock * kvp.Value;
        }

        // Validate wear is within tolerance
        var wearTolerance = VoxelGameConstants.TOOL_WEAR_TOLERANCE;
        if (Math.Abs(toolWearDelta - expectedTotalWear) > expectedTotalWear * wearTolerance)
        {
            // Too little wear = durability hack
            if (toolWearDelta < expectedTotalWear * (1.0f - wearTolerance))
            {
                return ClaimValidationResult.Failure(
                    $"Tool wear too low: {toolWearDelta:F4} (expected: {expectedTotalWear:F4})",
                    0.95f);
            }
            // Too much wear = possible but unusual
            else
            {
                warnings.Add($"Tool wear higher than expected: {toolWearDelta:F4} (expected: {expectedTotalWear:F4})");
            }
        }

        // === VALIDATION 5: Rare ore distribution (X-ray detection) ===
        var rareOreCount = claim.BlocksMined.Count(b => VoxelGameConstants.IsRareOre(b.BlockType));
        var rareOrePercentage = claim.BlockCount > 0 ? (float)rareOreCount / claim.BlockCount : 0;

        var expectedRareOrePercentage = VoxelGameConstants.EXPECTED_RARE_ORE_PERCENTAGE;
        var rareOreThreshold = expectedRareOrePercentage * VoxelGameConstants.RARE_ORE_DETECTION_MULTIPLIER;

        if (rareOrePercentage > rareOreThreshold)
        {
            return ClaimValidationResult.Failure(
                $"Rare ore rate too high: {rareOrePercentage:P1} (expected: {expectedRareOrePercentage:P1}, threshold: {rareOreThreshold:P1})",
                0.95f);
        }

        // Warn if rare ore rate is suspiciously high (but not quite cheating)
        if (rareOrePercentage > expectedRareOrePercentage * 3.0f && claim.BlockCount > 10)
        {
            warnings.Add($"High rare ore rate: {rareOrePercentage:P1} (expected: {expectedRareOrePercentage:P1})");
        }

        // === VALIDATION 6: Movement during mining ===
        var movementDistance = VoxelGameConstants.CalculateDistance(
            claim.StartLocation.X, claim.StartLocation.Y, claim.StartLocation.Z,
            claim.EndLocation.X, claim.EndLocation.Y, claim.EndLocation.Z);
        var movementSpeed = claim.DurationSeconds > 0 ? movementDistance / claim.DurationSeconds : 0;

        if (movementSpeed > VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND)
        {
            return ClaimValidationResult.Failure(
                $"Movement during mining too fast: {movementSpeed:F1} m/s (max: {VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND})",
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
