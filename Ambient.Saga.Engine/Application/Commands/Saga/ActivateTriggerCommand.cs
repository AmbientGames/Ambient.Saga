using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to manually activate a Saga trigger (bypassing proximity checks).
/// Used for scripted events, quest progression, or admin commands.
///
/// Side Effects:
/// - Creates TriggerActivated transaction
/// - Creates CharacterSpawned transactions if trigger has spawns
/// - Creates QuestTokenAwarded transactions if trigger awards tokens
/// </summary>
public record ActivateTriggerCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar activating the trigger
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the trigger
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Trigger to activate
    /// </summary>
    public required string TriggerRef { get; init; }

    /// <summary>
    /// Avatar position at time of activation (for spawn calculations)
    /// </summary>
    public required double AvatarX { get; init; }
    public required double AvatarZ { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks)
    /// </summary>
    public required AvatarBase Avatar { get; init; }

    /// <summary>
    /// Force activation even if quest token requirements not met (admin override)
    /// </summary>
    public bool ForceActivation { get; init; } = false;
}
