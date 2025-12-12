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
/// Handler for SubmitToolWearClaimCommand.
/// Validates tool wear rate and creates ToolWearClaimed transaction.
/// </summary>
public class SubmitToolWearClaimHandler : IRequestHandler<SubmitToolWearClaimCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public SubmitToolWearClaimHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(SubmitToolWearClaimCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SubmitToolWearClaim] Avatar {command.AvatarId} tool {command.Claim.ToolRef} wear: {command.Claim.ConditionBefore:F4} -> {command.Claim.ConditionAfter:F4}");

        try
        {
            // Get or create Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Validate tool wear (anti-cheat)
            var validation = ValidateToolWear(command.Claim);
            if (!validation.IsValid)
            {
                System.Diagnostics.Debug.WriteLine($"[SubmitToolWearClaim] REJECTED: {validation.Reason} (confidence: {validation.CheatConfidence:P0})");
                return SagaCommandResult.Failure(instance.InstanceId, validation.Reason);
            }

            // Create transaction
            var transaction = VoxelTransactionHelper.CreateToolWearClaimedTransaction(
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

            System.Diagnostics.Debug.WriteLine($"[SubmitToolWearClaim] Tool wear claim accepted (sequence: {sequenceNumbers.First()})");

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
            System.Diagnostics.Debug.WriteLine($"[SubmitToolWearClaim] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error submitting tool wear claim: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate tool wear rate to detect infinite durability hacks.
    /// </summary>
    private ClaimValidationResult ValidateToolWear(ToolWearClaim claim)
    {
        // Validate condition values are in range [0, 1]
        if (claim.ConditionBefore < 0 || claim.ConditionBefore > 1 ||
            claim.ConditionAfter < 0 || claim.ConditionAfter > 1)
        {
            return ClaimValidationResult.Failure("Tool condition must be between 0 and 1", 0.99f);
        }

        // Validate condition decreased (or stayed same for perfect tools)
        var wearDelta = claim.ConditionBefore - claim.ConditionAfter;
        if (wearDelta < 0)
        {
            return ClaimValidationResult.Failure(
                $"Tool condition increased (was {claim.ConditionBefore:F4}, now {claim.ConditionAfter:F4}) - invalid repair",
                0.99f);
        }

        // If no blocks mined, wear should be zero
        if (claim.BlocksMinedWithTool == 0)
        {
            if (wearDelta > 0.0001f) // Allow tiny floating point errors
            {
                return ClaimValidationResult.Failure(
                    "Tool wear occurred with zero blocks mined",
                    0.85f);
            }
            return ClaimValidationResult.Success();
        }

        // Calculate expected wear
        var blockType = claim.DominantBlockType ?? "stone"; // Default to stone if not specified
        var expectedWearPerBlock = VoxelGameConstants.GetExpectedToolWear(claim.ToolRef, blockType);
        var expectedTotalWear = expectedWearPerBlock * claim.BlocksMinedWithTool;

        // Calculate actual wear rate
        var actualWearRate = wearDelta / claim.BlocksMinedWithTool;

        // Validate wear is within tolerance
        var wearRatio = actualWearRate / expectedWearPerBlock;
        var tolerance = VoxelGameConstants.TOOL_WEAR_TOLERANCE;

        // Too little wear = durability hack
        if (wearRatio < 1.0f - tolerance)
        {
            return ClaimValidationResult.Failure(
                $"Tool wear too low: {actualWearRate:F6} per block (expected: {expectedWearPerBlock:F6}, ratio: {wearRatio:P0})",
                0.95f);
        }

        // Too much wear = possible, but unusual (maybe mining very hard blocks)
        if (wearRatio > 1.0f + tolerance * 2.0f) // More lenient on high side (legitimate variation)
        {
            return ClaimValidationResult.Warning(
                $"Tool wear unusually high: {actualWearRate:F6} per block (expected: {expectedWearPerBlock:F6}, ratio: {wearRatio:P0})");
        }

        return ClaimValidationResult.Success();
    }
}
