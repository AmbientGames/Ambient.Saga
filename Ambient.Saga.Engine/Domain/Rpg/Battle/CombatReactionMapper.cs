using Ambient.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Maps XSD-generated domain AttackTell/DefenseOutcome types to the engine's
/// CombatReaction types. Bridges the XML content layer with the battle engine.
/// </summary>
public static class CombatReactionMapper
{
    public static AttackTell FromDomain(Ambient.Domain.AttackTell domainTell)
    {
        var outcomes = new Dictionary<AvatarDefenseType, DefenseOutcome>();

        if (domainTell.Outcome != null)
        {
            foreach (var outcome in domainTell.Outcome)
            {
                var defenseType = MapDefenseReactionType(outcome.Reaction);
                outcomes[defenseType] = new DefenseOutcome
                {
                    Reaction = defenseType,
                    DamageMultiplier = outcome.DamageMultiplier,
                    EnablesCounter = outcome.EnablesCounter,
                    CounterMultiplier = outcome.CounterMultiplier,
                    PreventsStatusEffect = outcome.PreventsStatusEffect,
                    ResponseText = outcome.ResponseText ?? string.Empty,
                    Effects = outcome.Effects
                };
            }
        }

        // Ensure a None outcome exists (fallback for timeout)
        if (!outcomes.ContainsKey(AvatarDefenseType.None))
        {
            outcomes[AvatarDefenseType.None] = new DefenseOutcome
            {
                Reaction = AvatarDefenseType.None,
                DamageMultiplier = 1.0f,
                ResponseText = "The attack connects fully!"
            };
        }

        return new AttackTell
        {
            RefName = domainTell.RefName,
            Pattern = MapAttackPatternType(domainTell.Pattern),
            TellText = domainTell.TellText ?? string.Empty,
            ReactionWindowMs = domainTell.ReactionWindowMs,
            OptimalDefense = MapDefenseReactionType(domainTell.OptimalDefense),
            SecondaryDefense = domainTell.SecondaryDefenseSpecified
                ? MapDefenseReactionType(domainTell.SecondaryDefense)
                : null,
            WeaponCategories = domainTell.WeaponCategories,
            Outcomes = outcomes
        };
    }

    private static AvatarDefenseType MapDefenseReactionType(DefenseReactionType domainType) => domainType switch
    {
        DefenseReactionType.Dodge => AvatarDefenseType.Dodge,
        DefenseReactionType.Block => AvatarDefenseType.Block,
        DefenseReactionType.Parry => AvatarDefenseType.Parry,
        DefenseReactionType.Brace => AvatarDefenseType.Brace,
        DefenseReactionType.None => AvatarDefenseType.None,
        _ => AvatarDefenseType.None
    };

    private static AttackPatternType MapAttackPatternType(Ambient.Domain.AttackPatternType domainType) => domainType switch
    {
        Ambient.Domain.AttackPatternType.Slash => AttackPatternType.Slash,
        Ambient.Domain.AttackPatternType.Thrust => AttackPatternType.Thrust,
        Ambient.Domain.AttackPatternType.Overhead => AttackPatternType.Overhead,
        Ambient.Domain.AttackPatternType.Sweep => AttackPatternType.Sweep,
        Ambient.Domain.AttackPatternType.Flurry => AttackPatternType.Flurry,
        Ambient.Domain.AttackPatternType.Projectile => AttackPatternType.Projectile,
        Ambient.Domain.AttackPatternType.Charge => AttackPatternType.Charge,
        Ambient.Domain.AttackPatternType.Bite => AttackPatternType.Bite,
        Ambient.Domain.AttackPatternType.Claw => AttackPatternType.Claw,
        Ambient.Domain.AttackPatternType.Tail => AttackPatternType.Tail,
        Ambient.Domain.AttackPatternType.Breath => AttackPatternType.Breath,
        Ambient.Domain.AttackPatternType.Spit => AttackPatternType.Spit,
        Ambient.Domain.AttackPatternType.Bolt => AttackPatternType.Bolt,
        Ambient.Domain.AttackPatternType.Wave => AttackPatternType.Wave,
        Ambient.Domain.AttackPatternType.Burst => AttackPatternType.Burst,
        Ambient.Domain.AttackPatternType.Drain => AttackPatternType.Drain,
        Ambient.Domain.AttackPatternType.Grapple => AttackPatternType.Grapple,
        Ambient.Domain.AttackPatternType.Feint => AttackPatternType.Feint,
        _ => AttackPatternType.Slash
    };
}
