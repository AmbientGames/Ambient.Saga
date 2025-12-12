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
/// Handler for SubmitLocationClaimCommand.
/// Validates movement speed and creates LocationClaimed transaction.
/// </summary>
public class SubmitLocationClaimHandler : IRequestHandler<SubmitLocationClaimCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public SubmitLocationClaimHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(SubmitLocationClaimCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SubmitLocationClaim] Avatar {command.AvatarId} at position {command.Claim.Position}");

        try
        {
            // Get or create Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Validate movement speed (anti-cheat)
            var validation = ValidateMovement(command.Claim);
            if (!validation.IsValid)
            {
                System.Diagnostics.Debug.WriteLine($"[SubmitLocationClaim] REJECTED: {validation.Reason} (confidence: {validation.CheatConfidence:P0})");
                return SagaCommandResult.Failure(instance.InstanceId, validation.Reason);
            }

            // Create transaction
            var transaction = VoxelTransactionHelper.CreateLocationClaimedTransaction(
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

            System.Diagnostics.Debug.WriteLine($"[SubmitLocationClaim] Location claim accepted (sequence: {sequenceNumbers.First()})");

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
            System.Diagnostics.Debug.WriteLine($"[SubmitLocationClaim] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error submitting location claim: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate movement speed to detect teleportation and fly hacks.
    /// </summary>
    private ClaimValidationResult ValidateMovement(LocationClaim claim)
    {
        // If no previous position, can't validate velocity
        if (claim.PreviousPosition == null || !claim.PreviousTimestamp.HasValue)
        {
            return ClaimValidationResult.Success();
        }

        // Calculate time delta
        var timeDelta = (claim.Timestamp - claim.PreviousTimestamp.Value).TotalSeconds;
        if (timeDelta <= 0)
        {
            return ClaimValidationResult.Failure("Invalid timestamp: current time is before previous time", 0.95f);
        }

        // Calculate 3D distance
        var distance = VoxelGameConstants.CalculateDistance(
            claim.PreviousPosition.X, claim.PreviousPosition.Y, claim.PreviousPosition.Z,
            claim.Position.X, claim.Position.Y, claim.Position.Z);

        // Calculate horizontal distance (for horizontal speed check)
        var horizontalDistance = VoxelGameConstants.CalculateDistance2D(
            claim.PreviousPosition.X, claim.PreviousPosition.Z,
            claim.Position.X, claim.Position.Z);

        // Calculate vertical distance (for vertical speed check)
        var verticalDistance = Math.Abs(claim.Position.Y - claim.PreviousPosition.Y);

        // Calculate speeds
        var horizontalSpeed = horizontalDistance / timeDelta;
        var verticalSpeed = verticalDistance / timeDelta;

        // Validate horizontal movement speed
        if (horizontalSpeed > VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND)
        {
            return ClaimValidationResult.Failure(
                $"Movement too fast: {horizontalSpeed:F1} m/s (max: {VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND} m/s)",
                0.90f);
        }

        // Validate vertical movement speed (falling/flying)
        if (verticalSpeed > VoxelGameConstants.MAX_VERTICAL_SPEED_METERS_PER_SECOND)
        {
            return ClaimValidationResult.Failure(
                $"Vertical movement too fast: {verticalSpeed:F1} m/s (max: {VoxelGameConstants.MAX_VERTICAL_SPEED_METERS_PER_SECOND} m/s)",
                0.90f);
        }

        // Warn if moving at suspicious speeds (not quite cheating, but unusual)
        var warnings = new List<string>();
        if (horizontalSpeed > VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND * 0.9f)
        {
            warnings.Add($"High horizontal speed: {horizontalSpeed:F1} m/s");
        }
        if (verticalSpeed > VoxelGameConstants.MAX_VERTICAL_SPEED_METERS_PER_SECOND * 0.9f)
        {
            warnings.Add($"High vertical speed: {verticalSpeed:F1} m/s");
        }

        if (warnings.Count > 0)
        {
            return new ClaimValidationResult
            {
                IsValid = true,
                Reason = "Valid with warnings",
                CheatConfidence = 0.2f,
                ValidationWarnings = warnings
            };
        }

        return ClaimValidationResult.Success();
    }
}
