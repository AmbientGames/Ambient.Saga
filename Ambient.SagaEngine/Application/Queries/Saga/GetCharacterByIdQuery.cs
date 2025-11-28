using Ambient.Domain;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

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
    /// Saga containing the character
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Character instance ID
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }
}
