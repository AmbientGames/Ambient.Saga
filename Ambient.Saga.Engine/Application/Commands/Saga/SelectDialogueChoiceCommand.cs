using Ambient.Domain;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Selects a dialogue choice.
/// Creates DialogueNodeVisited transaction and potentially TraitAssigned/TraitRemoved transactions.
/// </summary>
public record SelectDialogueChoiceCommand : IRequest<SagaCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string SagaArcRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required string ChoiceId { get; init; }
    public required AvatarBase Avatar { get; init; }
}
