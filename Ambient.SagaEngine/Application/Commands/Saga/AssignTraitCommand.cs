using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to assign a trait to a character (befriend, anger, trade discount, etc.).
///
/// Side Effects:
/// - Creates TraitAssigned transaction
/// - Tracks character relationship for achievements
/// </summary>
public record AssignTraitCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar assigning the trait
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the character
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Character receiving the trait
    /// </summary>
    public required string CharacterRef { get; init; }

    /// <summary>
    /// Trait type being assigned (Friendly, Hostile, TradeDiscount, etc.)
    /// </summary>
    public required string TraitType { get; init; }

    /// <summary>
    /// Reason for trait assignment (for tracking/lore)
    /// </summary>
    public string? Reason { get; init; }
}
