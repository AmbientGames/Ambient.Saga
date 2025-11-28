using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to mark a Saga as completed (all objectives done, quest finished).
///
/// Side Effects:
/// - Creates SagaCompleted transaction
/// - Tracks completion for achievements
/// - May trigger cleanup/despawn logic
/// </summary>
public record CompleteSagaCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar completing the Saga
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga being completed
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Completion method (for tracking/achievements)
    /// </summary>
    public string? CompletionMethod { get; init; }
}
