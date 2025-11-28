using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to visit a dialogue node (triggers rewards on first visit).
///
/// Side Effects (first visit only):
/// - Creates DialogueNodeVisited transaction
/// - May create ItemTraded transactions (items awarded)
/// - May create TraitAssigned transactions (relationship changes)
/// - May create QuestTokenAwarded transactions (quest progression)
/// - Idempotent: subsequent visits do NOT re-award items
/// </summary>
public record VisitDialogueNodeCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar visiting the node
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the character
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Character being talked to
    /// </summary>
    public required string CharacterRef { get; init; }

    /// <summary>
    /// Dialogue tree reference
    /// </summary>
    public required string DialogueTreeRef { get; init; }

    /// <summary>
    /// Dialogue node being visited
    /// </summary>
    public required string DialogueNodeId { get; init; }

    /// <summary>
    /// Items awarded by this node (if any)
    /// </summary>
    public string? ItemsAwarded { get; init; }

    /// <summary>
    /// Traits assigned by this node (if any)
    /// </summary>
    public string? TraitsAssigned { get; init; }

    /// <summary>
    /// Quest tokens awarded by this node (if any)
    /// </summary>
    public string? QuestTokensAwarded { get; init; }

    /// <summary>
    /// Currency transferred (positive = given to player, negative = taken from player)
    /// </summary>
    public int CurrencyTransferred { get; init; } = 0;
}
