using System.Globalization;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Handlers.Saga;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Regression tests for round-3 audit finding C1: battle transaction data and
/// SagaTransaction.SetData/GetData were partially culture-sensitive. On comma-decimal
/// locales (nb-NO, de-DE, ...) the comma-joined LoadoutSlotSnapshot mis-split (zeroing
/// equipment condition after every battle), and cross-locale saves crashed battle
/// reconstruction with FormatException.
/// </summary>
public class CultureInvarianceTests
{
    /// <summary>Runs an action under a specific culture, restoring the original after.</summary>
    private static void RunUnderCulture(string cultureName, Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BattleTransaction_WrittenUnderCommaDecimalCulture_RoundTripsExactly()
    {
        RunUnderCulture("nb-NO", () =>
        {
            var avatar = new Combatant
            {
                RefName = "AvatarRef",
                DisplayName = "Avatar",
                Health = 0.875f,
                Stamina = 0.625f,
                Strength = 0.15f,
                Defense = 0.125f,
                Speed = 0.1f,
                Magic = 0.05f,
                Capabilities = new ItemCollection
                {
                    Equipment = new[] { new EquipmentEntry { EquipmentRef = "Sword", Condition = 0.85f } }
                },
                CombatProfile = new Dictionary<string, string> { ["MainHand"] = "Sword" }
            };
            var enemy = new Combatant
            {
                RefName = "EnemyRef",
                DisplayName = "Enemy",
                Health = 0.5f,
                Stamina = 0.75f,
                Strength = 0.1f,
                Defense = 0.05f,
                Speed = 0.1f,
                Magic = 0.0f,
                CombatProfile = new Dictionary<string, string>()
            };

            var instanceId = Guid.NewGuid();
            var battleStartedTx = BattleTransactionHelper.CreateBattleStartedTransaction(
                Guid.NewGuid().ToString(), "TestSaga", Guid.NewGuid(), Guid.NewGuid(),
                enemy.RefName, 42, avatar, enemy, new List<string>(), instanceId);

            // Values must be written with '.' decimals regardless of the machine culture
            Assert.Equal("0.875", battleStartedTx.Data[TransactionDataKeys.AvatarHealth]);
            Assert.DoesNotContain(",", battleStartedTx.Data[TransactionDataKeys.AvatarEquipment].Replace("Sword:", ""));

            var turnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                Guid.NewGuid().ToString(), battleStartedTx.TransactionId, 1,
                avatar.DisplayName, true, ActionType.Attack, null,
                0.123f, 0f, enemy.DisplayName, enemy.Health, avatar.Stamina,
                avatar, new World(), instanceId, avatarState: avatar, enemyState: enemy);

            // The loadout snapshot is comma-JOINED — a comma decimal inside an entry
            // corrupted the split ("MainHand:Sword:0,85" -> condition 0, phantom "85")
            var snapshot = turnTx.Data[TransactionDataKeys.LoadoutSlotSnapshot];
            Assert.Equal("MainHand:Sword:0.85", snapshot);

            // And reconstruction must read everything back exactly, still under nb-NO
            var instance = new SagaInstance { InstanceId = instanceId, SagaRef = "TestSaga" };
            instance.AddTransaction(battleStartedTx);
            instance.AddTransaction(turnTx);

            var world = new World();
            world.CharactersLookup["EnemyRef"] = new Character { RefName = "EnemyRef", DisplayName = "Enemy" };
            var (rAvatar, rEnemy, _, _, _, _) = BattleStateReconstructor.Reconstruct(battleStartedTx, instance, world);
            Assert.Equal(0.875f, rAvatar.Health, 3);
            Assert.Equal(0.5f, rEnemy.Health, 3);
        });
    }

    [Fact]
    public void SagaTransaction_SetData_WritesInvariant_AndReadsBackUnderAnyCulture()
    {
        var tx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            AvatarId = Guid.NewGuid().ToString(),
            Data = new Dictionary<string, string>()
        };

        // Write under a comma-decimal culture...
        RunUnderCulture("de-DE", () =>
        {
            tx.SetData("X", 123.456);
            tx.SetData("Z", -7.25f);
        });

        Assert.Equal("123.456", tx.Data["X"]);
        Assert.Equal("-7.25", tx.Data["Z"]);

        // ...and read back under a different one — spawn positions used to collapse
        // to 0 (characters relocating to saga origin) when this failed
        RunUnderCulture("fr-FR", () =>
        {
            Assert.Equal(123.456, tx.GetData<double>("X"), 3);
            Assert.Equal(-7.25f, tx.GetData<float>("Z"), 3);
        });
    }

    [Fact]
    public void SagaTransaction_GetData_StillReadsLegacyCurrentCultureValues()
    {
        // Saves written BEFORE the invariant-culture fix carry culture-formatted values;
        // they must keep parsing on the machine that wrote them
        var tx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            AvatarId = Guid.NewGuid().ToString(),
            Data = new Dictionary<string, string> { ["X"] = "123,456" } // legacy de-DE format
        };

        RunUnderCulture("de-DE", () =>
        {
            Assert.Equal(123.456, tx.GetData<double>("X"), 3);
        });
    }
}
