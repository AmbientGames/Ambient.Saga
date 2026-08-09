using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to deal damage to a character in an arc.
///
/// Side Effects:
/// - Creates CharacterDamaged transaction
/// - May create CharacterDefeated transaction if health reaches 0
/// </summary>
public record DamageCharacterCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar dealing the damage
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the character
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Character instance being damaged
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }

    /// <summary>
    /// Amount of damage (0.0 to 1.0, where 1.0 = full health)
    /// </summary>
    public required double Damage { get; init; }

    /// <summary>
    /// Source of damage (weapon, spell, environmental, etc.)
    /// </summary>
    public string? DamageSource { get; init; }
}
