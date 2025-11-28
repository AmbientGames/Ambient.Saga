using Ambient.Domain;
using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

/// <summary>
/// Gets the current dialogue state for a character interaction.
/// Replays transactions to determine current node, text, and available choices.
/// </summary>
public record GetDialogueStateQuery : IRequest<DialogueStateResult>
{
    public required Guid AvatarId { get; init; }
    public required string SagaRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required AvatarBase Avatar { get; init; }
}
