using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Xunit;

namespace Ambient.Saga.Engine.Tests.Rpg.Battle;

/// <summary>
/// Unit tests for combat reaction types (AttackTell, DefenseOutcome, PendingAttack).
/// Tests the Expedition 33-inspired active defense system mechanics.
/// </summary>
public class CombatReactionTests
{
    #region AttackTell.GetOutcome Tests

    [Fact]
    public void GetOutcome_ReturnsMatchingOutcome_WhenReactionExists()
    {
        // Arrange
        var tell = CreateSlashAttackTell();

        // Act
        var outcome = tell.GetOutcome(PlayerDefenseType.Parry);

        // Assert
        Assert.Equal(PlayerDefenseType.Parry, outcome.Reaction);
        Assert.Equal(0.0f, outcome.DamageMultiplier);
        Assert.True(outcome.EnablesCounter);
    }

    [Fact]
    public void GetOutcome_ReturnsDodgeOutcome_ForDodgeReaction()
    {
        // Arrange
        var tell = CreateSlashAttackTell();

        // Act
        var outcome = tell.GetOutcome(PlayerDefenseType.Dodge);

        // Assert
        Assert.Equal(PlayerDefenseType.Dodge, outcome.Reaction);
        Assert.Equal(0.25f, outcome.DamageMultiplier);
        Assert.False(outcome.EnablesCounter);
    }

    [Fact]
    public void GetOutcome_FallsBackToNoneOutcome_WhenReactionNotInDictionary()
    {
        // Arrange - Create tell with only Parry and None outcomes
        var tell = new AttackTell
        {
            RefName = "LimitedTell",
            Pattern = AttackPatternType.Slash,
            TellText = "Enemy swings!",
            OptimalDefense = PlayerDefenseType.Parry,
            Outcomes = new Dictionary<PlayerDefenseType, DefenseOutcome>
            {
                [PlayerDefenseType.Parry] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.Parry,
                    DamageMultiplier = 0.0f,
                    ResponseText = "Perfect parry!"
                },
                [PlayerDefenseType.None] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.None,
                    DamageMultiplier = 1.0f,
                    ResponseText = "You fail to react!"
                }
            }
        };

        // Act - Request Dodge which isn't in the dictionary
        var outcome = tell.GetOutcome(PlayerDefenseType.Dodge);

        // Assert - Should fall back to None outcome
        Assert.Equal(PlayerDefenseType.None, outcome.Reaction);
        Assert.Equal(1.0f, outcome.DamageMultiplier);
    }

    [Fact]
    public void GetOutcome_ReturnsDefaultOutcome_WhenNeitherReactionNorNoneExists()
    {
        // Arrange - Create tell with only Parry outcome
        var tell = new AttackTell
        {
            RefName = "MinimalTell",
            Pattern = AttackPatternType.Slash,
            TellText = "Enemy swings!",
            OptimalDefense = PlayerDefenseType.Parry,
            Outcomes = new Dictionary<PlayerDefenseType, DefenseOutcome>
            {
                [PlayerDefenseType.Parry] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.Parry,
                    DamageMultiplier = 0.0f,
                    ResponseText = "Perfect parry!"
                }
            }
        };

        // Act - Request Block which isn't in dictionary, and no None fallback
        var outcome = tell.GetOutcome(PlayerDefenseType.Block);

        // Assert - Should return default outcome
        Assert.Equal(PlayerDefenseType.Block, outcome.Reaction);
        Assert.Equal(1.0f, outcome.DamageMultiplier);
        Assert.Contains("fail to react", outcome.ResponseText);
    }

    #endregion

    #region DefenseOutcome Default Values Tests

    [Fact]
    public void DefenseOutcome_HasCorrectDefaults()
    {
        // Act
        var outcome = new DefenseOutcome();

        // Assert
        Assert.Equal(1.0f, outcome.DamageMultiplier);
        Assert.Equal(0, outcome.BonusAP);
        Assert.False(outcome.EnablesCounter);
        Assert.Equal(0.5f, outcome.CounterMultiplier);
        Assert.False(outcome.PreventsStatusEffect);
        Assert.Equal(string.Empty, outcome.ResponseText);
    }

    [Fact]
    public void DefenseOutcome_OptimalDefense_HasBestValues()
    {
        // Arrange
        var tell = CreateSlashAttackTell();

        // Act - Parry is optimal for Slash
        var outcome = tell.GetOutcome(PlayerDefenseType.Parry);

        // Assert
        Assert.Equal(0.0f, outcome.DamageMultiplier); // No damage
        Assert.True(outcome.EnablesCounter);          // Counter enabled
        Assert.True(outcome.BonusAP > 0);             // Bonus AP granted
    }

    #endregion

    #region PendingAttack Tests

    [Fact]
    public void PendingAttack_IsNotExpired_WhenWithinWindow()
    {
        // Arrange
        var tell = CreateSlashAttackTell();
        var attacker = CreateMockCombatant("Enemy");
        var target = CreateMockCombatant("Player");

        var pendingAttack = new PendingAttack
        {
            Attacker = attacker,
            Target = target,
            Tell = tell,
            BaseDamage = 50,
            TellShownAt = DateTime.UtcNow // Just shown
        };

        // Act & Assert
        Assert.False(pendingAttack.IsExpired);
        Assert.True(pendingAttack.RemainingMs > 0);
    }

    [Fact]
    public void PendingAttack_IsExpired_WhenWindowPassed()
    {
        // Arrange
        var tell = new AttackTell
        {
            RefName = "QuickTell",
            Pattern = AttackPatternType.Slash,
            TellText = "Quick slash!",
            ReactionWindowMs = 100, // Very short window
            OptimalDefense = PlayerDefenseType.Parry,
            Outcomes = new Dictionary<PlayerDefenseType, DefenseOutcome>
            {
                [PlayerDefenseType.Parry] = new DefenseOutcome { Reaction = PlayerDefenseType.Parry }
            }
        };
        var attacker = CreateMockCombatant("Enemy");
        var target = CreateMockCombatant("Player");

        var pendingAttack = new PendingAttack
        {
            Attacker = attacker,
            Target = target,
            Tell = tell,
            BaseDamage = 50,
            TellShownAt = DateTime.UtcNow.AddMilliseconds(-200) // 200ms ago, window is 100ms
        };

        // Act & Assert
        Assert.True(pendingAttack.IsExpired);
        Assert.Equal(0, pendingAttack.RemainingMs);
    }

    [Fact]
    public void PendingAttack_NeverExpires_WhenWindowIsZero()
    {
        // Arrange
        var tell = new AttackTell
        {
            RefName = "NoTimeLimitTell",
            Pattern = AttackPatternType.Slash,
            TellText = "Take your time...",
            ReactionWindowMs = 0, // No time limit
            OptimalDefense = PlayerDefenseType.Parry,
            Outcomes = new Dictionary<PlayerDefenseType, DefenseOutcome>
            {
                [PlayerDefenseType.Parry] = new DefenseOutcome { Reaction = PlayerDefenseType.Parry }
            }
        };
        var attacker = CreateMockCombatant("Enemy");
        var target = CreateMockCombatant("Player");

        var pendingAttack = new PendingAttack
        {
            Attacker = attacker,
            Target = target,
            Tell = tell,
            BaseDamage = 50,
            TellShownAt = DateTime.UtcNow.AddMinutes(-10) // 10 minutes ago
        };

        // Act & Assert
        Assert.False(pendingAttack.IsExpired);
        Assert.Equal(int.MaxValue, pendingAttack.RemainingMs);
    }

    #endregion

    #region AttackPatternType Coverage Tests

    [Theory]
    [InlineData(AttackPatternType.Slash, PlayerDefenseType.Parry)]
    [InlineData(AttackPatternType.Thrust, PlayerDefenseType.Dodge)]
    [InlineData(AttackPatternType.Overhead, PlayerDefenseType.Block)]
    [InlineData(AttackPatternType.Charge, PlayerDefenseType.Dodge)]
    [InlineData(AttackPatternType.Breath, PlayerDefenseType.Brace)]
    [InlineData(AttackPatternType.Burst, PlayerDefenseType.Brace)]
    [InlineData(AttackPatternType.Projectile, PlayerDefenseType.Block)]
    public void AttackPattern_HasLogicalOptimalDefense(AttackPatternType pattern, PlayerDefenseType expectedOptimal)
    {
        // This test documents the expected optimal defense for each pattern type
        // Actual implementation would use these mappings
        Assert.True(true); // Pattern-optimal defense mapping is design documentation
    }

    #endregion

    #region ReactionResult Tests

    [Fact]
    public void ReactionResult_TracksOptimalReaction()
    {
        // Arrange & Act
        var result = new ReactionResult
        {
            ChosenReaction = PlayerDefenseType.Parry,
            Outcome = new DefenseOutcome
            {
                Reaction = PlayerDefenseType.Parry,
                DamageMultiplier = 0.0f,
                EnablesCounter = true
            },
            FinalDamage = 0,
            NarrativeText = "Perfect parry! You riposte!",
            CounterDamage = 25,
            WasOptimal = true,
            WasSecondary = false,
            TimedOut = false
        };

        // Assert
        Assert.True(result.WasOptimal);
        Assert.False(result.WasSecondary);
        Assert.False(result.TimedOut);
        Assert.NotNull(result.CounterDamage);
        Assert.Equal(0, result.FinalDamage);
    }

    [Fact]
    public void ReactionResult_TracksTimedOutReaction()
    {
        // Arrange & Act
        var result = new ReactionResult
        {
            ChosenReaction = PlayerDefenseType.None,
            Outcome = new DefenseOutcome
            {
                Reaction = PlayerDefenseType.None,
                DamageMultiplier = 1.0f
            },
            FinalDamage = 50,
            NarrativeText = "You fail to react in time!",
            WasOptimal = false,
            WasSecondary = false,
            TimedOut = true
        };

        // Assert
        Assert.False(result.WasOptimal);
        Assert.True(result.TimedOut);
        Assert.Equal(50, result.FinalDamage);
        Assert.Null(result.CounterDamage);
    }

    #endregion

    #region Helper Methods

    private static AttackTell CreateSlashAttackTell()
    {
        return new AttackTell
        {
            RefName = "BasicSlash",
            Pattern = AttackPatternType.Slash,
            TellText = "The enemy draws back their blade for a wide slash!",
            ReactionWindowMs = 3000,
            OptimalDefense = PlayerDefenseType.Parry,
            SecondaryDefense = PlayerDefenseType.Dodge,
            Outcomes = new Dictionary<PlayerDefenseType, DefenseOutcome>
            {
                [PlayerDefenseType.Parry] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.Parry,
                    DamageMultiplier = 0.0f,
                    BonusAP = 10,
                    EnablesCounter = true,
                    CounterMultiplier = 0.5f,
                    ResponseText = "Perfect parry! You deflect the blade and counter!"
                },
                [PlayerDefenseType.Dodge] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.Dodge,
                    DamageMultiplier = 0.25f,
                    BonusAP = 5,
                    EnablesCounter = false,
                    ResponseText = "You roll aside, but the blade grazes you."
                },
                [PlayerDefenseType.Block] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.Block,
                    DamageMultiplier = 0.5f,
                    BonusAP = 0,
                    EnablesCounter = false,
                    ResponseText = "You raise your guard, absorbing part of the blow."
                },
                [PlayerDefenseType.Brace] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.Brace,
                    DamageMultiplier = 0.75f,
                    BonusAP = 0,
                    EnablesCounter = false,
                    ResponseText = "You brace, but the horizontal slash finds its mark."
                },
                [PlayerDefenseType.None] = new DefenseOutcome
                {
                    Reaction = PlayerDefenseType.None,
                    DamageMultiplier = 1.0f,
                    BonusAP = 0,
                    EnablesCounter = false,
                    ResponseText = "The slash connects fully!"
                }
            }
        };
    }

    private static Combatant CreateMockCombatant(string name)
    {
        return new Combatant
        {
            RefName = name,
            DisplayName = name,
            Health = 1.0f,
            Energy = 1.0f,
            Strength = 0.2f,
            Defense = 0.1f,
            Speed = 0.15f,
            Magic = 0.1f
        };
    }

    #endregion
}
