using Ambient.Domain;
using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to start dialogue with a character.
///
/// Side Effects:
/// - Creates DialogueStarted transaction
/// - Tracks dialogue initiation for achievements
/// </summary>
public record StartDialogueCommand : IRequest<SagaCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string SagaArcRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required AvatarBase Avatar { get; init; }
}
