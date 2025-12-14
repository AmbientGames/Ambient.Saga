using Ambient.Domain;
using MediatR;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Battle;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to submit a player's defensive reaction during the reaction phase.
/// Part of the Expedition 33-inspired active defense system.
/// </summary>
public class SubmitReactionCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar submitting the reaction
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga arc reference
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Battle instance ID
    /// </summary>
    public required Guid BattleInstanceId { get; init; }

    /// <summary>
    /// The player's chosen defensive reaction
    /// </summary>
    public required PlayerDefenseType Reaction { get; init; }

    /// <summary>
    /// Avatar entity for updates
    /// </summary>
    public required AvatarBase Avatar { get; init; }

    #region Reaction Results (populated by client after BattleEngine.ResolveReaction())

    /// <summary>
    /// The attack tell reference that was defended against
    /// </summary>
    public string? TellRefName { get; init; }

    /// <summary>
    /// Base damage from the attack before reaction modifiers
    /// </summary>
    public int BaseDamage { get; init; }

    /// <summary>
    /// Final damage taken after reaction modifier applied
    /// </summary>
    public int FinalDamage { get; init; }

    /// <summary>
    /// Counter damage dealt to attacker (if reaction enabled counter)
    /// </summary>
    public int? CounterDamage { get; init; }

    /// <summary>
    /// Stamina gained from skilled defense (0-1 normalized)
    /// </summary>
    public float StaminaGained { get; init; }

    /// <summary>
    /// Whether this was the optimal defense for the attack
    /// </summary>
    public bool WasOptimal { get; init; }

    /// <summary>
    /// Whether the reaction timed out (forced to None)
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// Player health after damage applied
    /// </summary>
    public float PlayerHealthAfter { get; init; }

    /// <summary>
    /// Player energy after stamina effects applied
    /// </summary>
    public float PlayerEnergyAfter { get; init; }

    /// <summary>
    /// Enemy health after counter damage (if any)
    /// </summary>
    public float EnemyHealthAfter { get; init; }

    #endregion
}
