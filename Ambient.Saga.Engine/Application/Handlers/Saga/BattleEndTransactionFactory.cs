using Ambient.Domain;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Creates the transactions that conclude a battle (BattleEnded, and on victory
/// CharacterDefeated + AffinityGranted). Shared by ExecuteBattleTurnHandler and
/// SubmitReactionHandler — a battle can end on a regular turn OR on a reaction
/// (counter-kill / lethal telegraphed attack), and both must conclude identically.
/// </summary>
internal static class BattleEndTransactionFactory
{
    internal static void Create(
        Guid avatarId,
        AvatarBase avatar,
        Guid battleInstanceId,
        SagaInstance instance,
        BattleEngine battleEngine,
        int totalTurns,
        Guid enemyCharacterInstanceId,
        Character enemyCharacter,
        List<SagaTransaction> newTransactions)
    {
        var avatarVictory = battleEngine.State == BattleState.Victory;
        var victorName = avatarVictory ? battleEngine.GetAvatar().DisplayName : battleEngine.GetEnemy().DisplayName;
        var defeatedName = avatarVictory ? battleEngine.GetEnemy().DisplayName : battleEngine.GetAvatar().DisplayName;

        // Create BattleEnded transaction with the distinct outcome — Fled is neither
        // victory nor defeat (no defeat dialogue, and the enemy can be re-engaged)
        var battleEndedTx = BattleTransactionHelper.CreateBattleEndedTransaction(
            avatarId.ToString(),
            battleInstanceId,
            totalTurns,
            avatarVictory,
            victorName,
            defeatedName,
            instance.InstanceId,
            outcome: battleEngine.State);

        instance.AddTransaction(battleEndedTx);
        newTransactions.Add(battleEndedTx);

        // If avatar won, create CharacterDefeated transaction and grant affinity
        if (avatarVictory)
        {
            var data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterInstanceId] = enemyCharacterInstanceId.ToString(),
                [TransactionDataKeys.CharacterRef] = enemyCharacter.RefName,
                [TransactionDataKeys.VictorAvatarId] = avatarId.ToString(),
                [TransactionDataKeys.DefeatMethod] = "Battle",
                [TransactionDataKeys.BattleTransactionId] = battleInstanceId.ToString()
            };

            // Add character tags for quest objective tracking
            if (enemyCharacter.Tags != null && enemyCharacter.Tags.Length > 0)
            {
                data[TransactionDataKeys.CharacterTag] = string.Join(",", enemyCharacter.Tags);
            }

            var characterDefeatedTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = data
            };

            instance.AddTransaction(characterDefeatedTx);
            newTransactions.Add(characterDefeatedTx);

            // Grant enemy's affinity to avatar if they have one and avatar doesn't already have it
            if (!string.IsNullOrEmpty(enemyCharacter.AffinityRef))
            {
                var avatarHasAffinity = avatar.Affinities?
                    .Any(a => a.AffinityRef == enemyCharacter.AffinityRef) ?? false;

                if (!avatarHasAffinity)
                {
                    // Add affinity to avatar
                    var affinities = avatar.Affinities?.ToList() ?? new List<Affinity>();
                    affinities.Add(new Affinity
                    {
                        AffinityRef = enemyCharacter.AffinityRef,
                        CapturedFromCharacterRef = enemyCharacter.RefName,
                        AcquiredDate = DateTime.UtcNow.ToString("O")
                    });
                    avatar.Affinities = affinities.ToArray();

                    // Create AffinityGranted transaction
                    var affinityTx = DialogueTransactionHelper.CreateAffinityGrantedTransaction(
                        avatarId.ToString(),
                        enemyCharacter.AffinityRef,
                        enemyCharacter.RefName,
                        instance.InstanceId);

                    instance.AddTransaction(affinityTx);
                    newTransactions.Add(affinityTx);

                    System.Diagnostics.Debug.WriteLine($"[BattleEnd] Granted affinity '{enemyCharacter.AffinityRef}' from defeated '{enemyCharacter.RefName}'");
                }
            }
        }
    }
}
