using Ambient.Domain;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Engine.Domain.Dialogue;
using Ambient.Rpg.Engine.Domain.Progression;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

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
        ArcInstance instance,
        BattleEngine battleEngine,
        int totalTurns,
        Guid enemyCharacterInstanceId,
        Character enemyCharacter,
        List<ArcTransaction> newTransactions)
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

        // The enemy won: record its "Victorious" trait (Dialogue.xsd
        // CharacterTraitType) so dialogue conditions can react to having lost to
        // this character. Fled is deliberately excluded here — with enemy
        // FleeThreshold behavior a Fled outcome can also mean the ENEMY ran, and
        // the factory cannot tell who fled; the avatar-fled case belongs with the
        // Disengaged assignment in BattleEngine.ExecuteFlee's CombatEvent.
        if (battleEngine.State == BattleState.Defeat)
        {
            var victoriousTx = DialogueTransactionHelper.CreateTraitAssignedTransaction(
                avatarId.ToString(),
                enemyCharacter.RefName,
                "Victorious",
                null,
                instance.InstanceId);

            instance.AddTransaction(victoriousTx);
            newTransactions.Add(victoriousTx);
        }

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

            var characterDefeatedTx = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.CharacterDefeated,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = data
            };

            instance.AddTransaction(characterDefeatedTx);
            newTransactions.Add(characterDefeatedTx);

            // Award quest tokens declared on the character template
            // (GivesQuestTokenOnDefeat) — the schema feature only DefeatCharacterHandler
            // used to honor, while this factory (the REAL battle-victory producer of
            // CharacterDefeated) silently dropped it.
            AppendDefeatTokenGrants(avatarId, enemyCharacterInstanceId, enemyCharacter, instance, newTransactions);

            // Grant combat-victory experience (RPG audit: winning a battle used to yield
            // ZERO XP — only quest defeat-objectives did — so killing a boss or any
            // off-quest enemy advanced neither XP nor level and felt weightless). Scaled
            // by the defeated enemy's difficulty (see CombatExperience) and folded through
            // LevelCurve exactly like an authored quest Experience reward
            // (QuestRewardDistributor.DistributeReward), so the level is re-derived and
            // never demoted. This lives in the avatarVictory block, so flee and defeat
            // grant nothing, and runs once because the battle-end path runs once per
            // battle (re-entry is blocked once BattleEnded is recorded). The avatar is
            // persisted by the caller (ExecuteBattleTurnHandler.CommitAndFinish /
            // SubmitReactionHandler) — the same path that persists the health/stamina
            // outcome above.
            GrantVictoryExperience(avatar, enemyCharacter);

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

    /// <summary>
    /// Adds the combat-victory experience for the defeated enemy to the avatar and
    /// re-derives its level from accumulated XP (never demoting). Mirrors the quest
    /// Experience-reward application in QuestRewardDistributor.DistributeReward so both
    /// producers feed <see cref="LevelCurve"/> identically. Caller-agnostic: it only
    /// mutates the in-memory avatar (just like the affinity grant above); the two battle
    /// handlers persist it afterward.
    /// </summary>
    private static void GrantVictoryExperience(AvatarBase avatar, Character enemyCharacter)
    {
        avatar.Stats ??= new CharacterStats();

        var xp = CombatExperience.GetVictoryExperience(enemyCharacter.Stats);
        if (xp <= 0f)
            return;

        avatar.Stats.Experience += xp;
        avatar.Stats.Level = Math.Max(avatar.Stats.Level,
            LevelCurve.GetLevelForExperience(avatar.Stats.Experience));

        System.Diagnostics.Debug.WriteLine(
            $"[BattleEnd] Granted {xp:F1} XP for defeating '{enemyCharacter.RefName}' " +
            $"(total {avatar.Stats.Experience:F1}, level {avatar.Stats.Level})");
    }

    /// <summary>
    /// Awards the quest tokens a character template declares via
    /// GivesQuestTokenOnDefeat. Shared by every real CharacterDefeated producer
    /// (battle victory here, scripted dialogue victory in
    /// ApplyBattleDialogueEffectsHandler) and mirrors DefeatCharacterHandler's
    /// semantics INCLUDING its already-defeated idempotency guard: if the committed
    /// log already holds a CharacterDefeated for this character instance, the tokens
    /// were awarded then — a duplicate defeat report must not re-award them.
    /// </summary>
    internal static void AppendDefeatTokenGrants(
        Guid avatarId,
        Guid defeatedCharacterInstanceId,
        Character defeatedCharacter,
        ArcInstance instance,
        List<ArcTransaction> newTransactions)
    {
        if (defeatedCharacter.GivesQuestTokenOnDefeat == null ||
            defeatedCharacter.GivesQuestTokenOnDefeat.Length == 0)
        {
            return;
        }

        // Already-defeated guard (mirrors DefeatCharacterHandler.cs). The
        // CharacterDefeated created for THIS victory is still Pending, so it never
        // trips its own guard.
        var alreadyDefeated = instance.GetCommittedTransactions()
            .Any(t => t.Type == ArcTransactionType.CharacterDefeated &&
                      t.Data.TryGetValue(TransactionDataKeys.CharacterInstanceId, out var defeatedId) &&
                      defeatedId == defeatedCharacterInstanceId.ToString());

        if (alreadyDefeated)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleEnd] Character {defeatedCharacterInstanceId} already defeated - skipping token re-award");
            return;
        }

        foreach (var tokenRef in defeatedCharacter.GivesQuestTokenOnDefeat)
        {
            if (string.IsNullOrEmpty(tokenRef)) continue;

            var tokenTx = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.QuestTokenAwarded,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestTokenRef] = tokenRef,
                    [TransactionDataKeys.Reason] = $"Defeated {defeatedCharacter.RefName}"
                }
            };

            instance.AddTransaction(tokenTx);
            newTransactions.Add(tokenTx);
        }
    }
}
