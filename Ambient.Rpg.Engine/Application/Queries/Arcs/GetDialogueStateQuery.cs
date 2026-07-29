using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Gets the current dialogue state for a character interaction.
/// Replays transactions to determine current node, text, and available choices.
/// </summary>
public record GetDialogueStateQuery : IRequest<DialogueStateResult>
{
    public required Guid AvatarId { get; init; }
    public required string ArcRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required AvatarBase Avatar { get; init; }
}
