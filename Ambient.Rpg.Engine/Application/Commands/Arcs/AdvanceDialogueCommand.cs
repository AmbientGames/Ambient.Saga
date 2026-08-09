using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to advance dialogue to the next node when current node has no choices.
/// Used when a dialogue node displays text and expects the avatar to click "Continue".
///
/// Side Effects:
/// - Creates DialogueNodeVisited transaction for the next node
/// - May execute actions defined on the next node (awards, relationship changes, etc.)
/// </summary>
public record AdvanceDialogueCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar advancing the dialogue
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the character
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Character instance being talked to
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }

    /// <summary>
    /// Avatar entity for state provider
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
