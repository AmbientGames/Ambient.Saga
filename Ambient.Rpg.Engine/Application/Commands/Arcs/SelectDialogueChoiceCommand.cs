using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Selects a dialogue choice.
/// Creates DialogueNodeVisited transaction and potentially TraitAssigned/TraitRemoved transactions.
/// </summary>
public record SelectDialogueChoiceCommand : IRequest<ArcCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string ArcRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required string ChoiceId { get; init; }
    public required AvatarBase Avatar { get; init; }
}
