using System.Linq;
using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine;
using Ambient.Rpg.Engine.Domain.Battle;
using Xunit;

namespace Ambient.Rpg.Engine.Tests.Rpg.Battle;

/// <summary>
/// CombatAI reads battle-behavior traits (FleeThreshold, LastStand, Aggression) off the
/// character template in the world's CharactersLookup (M8). Before the fix the AI read
/// ZERO traits: a "Coward" with an authored FleeThreshold fought to the death and the
/// trait did nothing. Health is on a 0..1 scale (HealthPercent = Health / MAX_STAT(1.0)
/// * 100), and trait thresholds are authored as percentages (0-100).
///
/// Only a character facing the NON-templated avatar can flee — companions share this AI
/// but a companion flee would end the whole battle, so the flee branch is suppressed
/// whenever the opponent is itself a templated character.
/// </summary>
public class CombatAiTraitTests
{
    [Fact]
    public void FleeThreshold_LowHealthFacingAvatar_Flees()
    {
        var world = WorldWithCharacter("Coward", (CharacterTraitType.FleeThreshold, 50));
        var ai = new CombatAI(world, randomSeed: 1);

        // 30% health, below the 50% flee threshold, facing the avatar (an archetype ref
        // that is deliberately NOT in CharactersLookup)
        var view = View(self: Fighter("Coward", healthPct: 0.30f),
                        opponent: Fighter("HeroArchetype", healthPct: 1.0f));

        Assert.Equal(ActionType.Flee, ai.DecideTurn(view).ActionType);
    }

    [Fact]
    public void FleeThreshold_HealthAboveThreshold_DoesNotFlee()
    {
        var world = WorldWithCharacter("Coward", (CharacterTraitType.FleeThreshold, 50));
        var ai = new CombatAI(world, randomSeed: 1);

        var view = View(self: Fighter("Coward", healthPct: 0.80f),
                        opponent: Fighter("HeroArchetype", healthPct: 1.0f));

        Assert.NotEqual(ActionType.Flee, ai.DecideTurn(view).ActionType);
    }

    [Fact]
    public void NoFleeTrait_LowHealth_DoesNotFlee()
    {
        // Identical low health, but no FleeThreshold authored: historical never-flee.
        var world = WorldWithCharacter("Stoic");
        var ai = new CombatAI(world, randomSeed: 1);

        var view = View(self: Fighter("Stoic", healthPct: 0.10f),
                        opponent: Fighter("HeroArchetype", healthPct: 1.0f));

        Assert.NotEqual(ActionType.Flee, ai.DecideTurn(view).ActionType);
    }

    [Fact]
    public void LastStand_TakesPriorityOverFlee_AttacksAllOut()
    {
        // LastStand is checked before flee/defensive logic: a cornered fighter that would
        // otherwise flee (threshold 90%) instead goes all-out.
        var world = WorldWithCharacter("Berserker",
            (CharacterTraitType.LastStand, 40),
            (CharacterTraitType.FleeThreshold, 90));
        var ai = new CombatAI(world, randomSeed: 1);

        var view = View(self: Fighter("Berserker", healthPct: 0.30f),
                        opponent: Fighter("HeroArchetype", healthPct: 1.0f));

        Assert.Equal(ActionType.Attack, ai.DecideTurn(view).ActionType);
    }

    // ----- helpers -----

    private static World WorldWithCharacter(string refName, params (CharacterTraitType name, int value)[] traits)
    {
        var world = new World();
        world.CharactersLookup[refName] = new Character
        {
            RefName = refName,
            DisplayName = refName,
            Traits = traits
                .Select(t => new CharacterTrait { Name = t.name, Value = t.value, ValueSpecified = true })
                .ToArray()
        };
        return world;
    }

    private static BattleView View(Combatant self, Combatant opponent) =>
        new BattleView { Self = self, Opponent = opponent };

    private static Combatant Fighter(string refName, float healthPct) => new Combatant
    {
        RefName = refName,
        DisplayName = refName,
        Health = healthPct,   // 0..1 scale
        Stamina = 1.0f,
        Strength = 0.2f,
        Defense = 0.1f,
        Speed = 0.1f,
        Magic = 0.1f,
        AffinityRef = "IRON",
        Capabilities = new ItemCollection()
    };
}
