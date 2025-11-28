using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to instantly defeat a character (e.g., boss defeated, scripted death).
///
/// Side Effects:
/// - Creates CharacterDefeated transaction
/// - Sets character health to 0
/// - Marks character as defeated
/// </summary>
public record DefeatCharacterCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar who defeated the character (for achievement tracking)
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the character
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Character instance being defeated
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }

    /// <summary>
    /// How the character was defeated (for lore/tracking)
    /// </summary>
    public string? DefeatMethod { get; init; }
}
