using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Contracts.Persistence;

/// <summary>
/// Avatar-level progress state projected from saga transaction logs.
/// Handles cross-arc state that dialogue conditions and trigger availability need.
/// </summary>
public interface IAvatarProgressRepository
{
    // ===== PROJECTION (write side) =====

    void ProjectTransactions(Guid avatarId, string sagaRef, IReadOnlyList<SagaTransaction> transactions);

    // ===== QUEST TOKENS =====

    bool HasQuestToken(Guid avatarId, string questTokenRef);
    HashSet<string> GetAllQuestTokens(Guid avatarId);

    // ===== QUEST PROGRESS =====

    QuestProgressStatus? GetQuestStatus(Guid avatarId, string questRef);
    AvatarQuestProgress? GetQuestProgress(Guid avatarId, string questRef);

    // ===== BOSS DEFEATS =====

    int GetBossDefeatedCount(Guid avatarId, string characterRef);

    // ===== FACTION REPUTATION =====

    int GetFactionReputation(Guid avatarId, string factionRef);

    // ===== CHARACTER TRAITS =====

    int? GetCharacterTraitValue(Guid avatarId, string characterRef, string traitType);
}
