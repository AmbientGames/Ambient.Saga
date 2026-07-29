using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to explicitly end a dialogue session — sent when the player leaves the dialogue UI
/// (dismiss, walk away, etc.) without having reached a terminal node. Creates a
/// DialogueCompleted transaction so the session is sealed and may be restarted cleanly.
///
/// Idempotent: if the most recent dialogue session for the character has already ended,
/// the command succeeds as a no-op.
/// </summary>
public record CloseDialogueCommand : IRequest<ArcCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string ArcRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required AvatarBase Avatar { get; init; }
}
