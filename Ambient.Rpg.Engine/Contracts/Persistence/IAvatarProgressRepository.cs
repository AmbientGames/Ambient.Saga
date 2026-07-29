using Ambient.Rpg.Engine.Domain.AvatarProgress;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Contracts.Persistence;

/// <summary>
/// Avatar-level progress state projected from arc transaction logs.
/// Handles cross-arc state that dialogue conditions and trigger availability need.
/// </summary>
public interface IAvatarProgressRepository
{
    // ===== PROJECTION (write side) =====

    void ProjectTransactions(Guid avatarId, string arcRef, IReadOnlyList<ArcTransaction> transactions);

    /// <summary>
    /// Best-effort inverse of <see cref="ProjectTransactions"/> for transactions
    /// that were projected and later reversed (server rejection compensation).
    /// Pass the ORIGINAL transactions being reversed, not the TransactionReversed
    /// compensations. Non-invertible projections (QuestStageAdvanced's previous
    /// stage, TraitRemoved's previous value) are skipped.
    /// </summary>
    void ReverseTransactions(Guid avatarId, string arcRef, IReadOnlyList<ArcTransaction> reversedOriginals);

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

    /// <summary>
    /// Highest value of a trait across all characters for this avatar, or null if never assigned.
    /// Skill-style traits (e.g. BargainSkill) are earned at one NPC and gated at another, so
    /// dialogue conditions read the avatar's best earned value regardless of source character.
    /// </summary>
    int? GetMaxTraitValue(Guid avatarId, string traitType);
}
