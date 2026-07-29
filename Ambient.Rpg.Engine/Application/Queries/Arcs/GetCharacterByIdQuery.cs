using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to get a specific character by instance ID.
/// Returns character state + template data.
/// </summary>
public record GetCharacterByIdQuery : IRequest<(CharacterState? State, Character? Template)>
{
    /// <summary>
    /// Avatar requesting character data
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the character
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Character instance ID
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }
}
