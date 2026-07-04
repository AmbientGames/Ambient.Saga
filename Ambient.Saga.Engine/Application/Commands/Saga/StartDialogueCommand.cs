using Ambient.Domain;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

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
