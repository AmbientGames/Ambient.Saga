using Ambient.Domain.Entities;

namespace Ambient.Rpg.Engine.Application.Results.Arcs;

/// <summary>
/// Result of an arc command execution.
/// Contains transaction IDs and success/failure information.
/// </summary>
public record ArcCommandResult
{
    /// <summary>
    /// The Arc instance ID that was modified
    /// </summary>
    public Guid ArcInstanceId { get; init; }

    /// <summary>
    /// Transaction IDs created by this command
    /// </summary>
    public List<Guid> TransactionIds { get; init; } = new();

    /// <summary>
    /// The new sequence number after transactions were applied
    /// </summary>
    public long NewSequenceNumber { get; init; }

    /// <summary>
    /// Additional data returned by the command (e.g., spawned character IDs, loot awarded)
    /// </summary>
    public Dictionary<string, object> Data { get; init; } = new();

    /// <summary>
    /// Whether the command succeeded
    /// </summary>
    public bool Successful { get; init; }

    /// <summary>
    /// Error message if command failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Updated avatar entity after command execution (if command modified avatar state).
    /// Client uses this directly instead of reloading from database.
    /// </summary>
    public AvatarEntity? UpdatedAvatar { get; init; }

    /// <summary>
    /// Creates a successful Arc command result
    /// </summary>
    public static ArcCommandResult Success(
        Guid arcInstanceId,
        List<Guid> transactionIds,
        long newSequenceNumber,
        Dictionary<string, object>? data = null,
        AvatarEntity? updatedAvatar = null)
    {
        return new ArcCommandResult
        {
            ArcInstanceId = arcInstanceId,
            TransactionIds = transactionIds,
            NewSequenceNumber = newSequenceNumber,
            Data = data ?? new(),
            UpdatedAvatar = updatedAvatar,
            Successful = true
        };
    }

    /// <summary>
    /// Creates a failed Arc command result
    /// </summary>
    public static ArcCommandResult Failure(
        Guid arcInstanceId,
        string errorMessage)
    {
        return new ArcCommandResult
        {
            ArcInstanceId = arcInstanceId,
            ErrorMessage = errorMessage,
            Successful = false
        };
    }
}
