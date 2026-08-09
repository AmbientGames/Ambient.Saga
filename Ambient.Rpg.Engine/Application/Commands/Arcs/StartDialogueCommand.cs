using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to start dialogue with a character.
///
/// Side Effects:
/// - Creates DialogueStarted transaction
/// - Tracks dialogue initiation for achievements
/// </summary>
public record StartDialogueCommand : IRequest<ArcCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string ArcRef { get; init; }
    public required Guid CharacterInstanceId { get; init; }
    public required AvatarBase Avatar { get; init; }

    /// <summary>
    /// Optional: open a specific tree instead of the character's Interactable
    /// default — used by battle dialogue triggers (boss taunts reference their
    /// own battle trees).
    /// </summary>
    public string? DialogueTreeRefOverride { get; init; }

    /// <summary>
    /// Optional: start the conversation at a specific node of that tree —
    /// battle triggers author per-moment entry points (battle_enraged, ...).
    /// </summary>
    public string? StartNodeIdOverride { get; init; }
}
