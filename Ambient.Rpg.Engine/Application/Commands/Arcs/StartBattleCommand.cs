using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Domain.Battle;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to start an interactive battle with a character.
///
/// Side Effects:
/// - Creates BattleStarted transaction (with equipment/affinity snapshot)
/// - Initializes battle state for turn-by-turn execution
/// - Does NOT execute any turns (use ExecuteBattleTurnCommand for that)
/// </summary>
public record StartBattleCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar participating in battle
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the battle
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Character instance being fought
    /// </summary>
    public required Guid EnemyCharacterInstanceId { get; init; }

    /// <summary>
    /// Avatar's combatant configuration
    /// </summary>
    public required Combatant AvatarCombatant { get; init; }

    /// <summary>
    /// Enemy's combatant configuration
    /// </summary>
    public required Combatant EnemyCombatant { get; init; }

    /// <summary>
    /// Avatar's selected affinities (for switching during battle)
    /// </summary>
    public required List<string> AvatarAffinityRefs { get; init; }

    /// <summary>
    /// Enemy AI (must be created with same random seed for determinism)
    /// </summary>
    public required ICombatAI EnemyMind { get; init; }

    /// <summary>
    /// Random seed for deterministic battle replay
    /// </summary>
    public required int RandomSeed { get; init; }

    /// <summary>
    /// Avatar entity participating in battle (for state updates)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }

    /// <summary>
    /// Party companion combatants (optional, for party battles)
    /// </summary>
    public List<Combatant>? CompanionCombatants { get; init; }
}
