using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using LiteDB;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

public class AvatarProgressRepository : IAvatarProgressRepository
{
    private readonly ILiteCollection<AvatarQuestToken> _questTokens;
    private readonly ILiteCollection<AvatarQuestProgress> _questProgress;
    private readonly ILiteCollection<AvatarBossDefeat> _bossDefeats;
    private readonly ILiteCollection<AvatarFactionReputation> _factionReputation;
    private readonly ILiteCollection<AvatarCharacterTrait> _characterTraits;

    public AvatarProgressRepository(ILiteDatabase database)
    {
        _questTokens = database.GetCollection<AvatarQuestToken>("AvatarQuestTokens");
        _questProgress = database.GetCollection<AvatarQuestProgress>("AvatarQuestProgress");
        _bossDefeats = database.GetCollection<AvatarBossDefeat>("AvatarBossDefeats");
        _factionReputation = database.GetCollection<AvatarFactionReputation>("AvatarFactionReputation");
        _characterTraits = database.GetCollection<AvatarCharacterTrait>("AvatarCharacterTraits");

        _questTokens.EnsureIndex(x => x.AvatarId);
        _questTokens.EnsureIndex(x => x.QuestTokenRef);
        _questProgress.EnsureIndex(x => x.AvatarId);
        _questProgress.EnsureIndex(x => x.QuestRef);
        _bossDefeats.EnsureIndex(x => x.AvatarId);
        _factionReputation.EnsureIndex(x => x.AvatarId);
        _characterTraits.EnsureIndex(x => x.AvatarId);
    }

    // ===== PROJECTION =====

    public void ProjectTransactions(Guid avatarId, string sagaRef, IReadOnlyList<SagaTransaction> transactions)
    {
        foreach (var tx in transactions)
        {
            switch (tx.Type)
            {
                case SagaTransactionType.QuestTokenAwarded:
                    ProjectQuestToken(avatarId, sagaRef, tx);
                    break;

                case SagaTransactionType.QuestAccepted:
                    ProjectQuestAccepted(avatarId, sagaRef, tx);
                    break;
                case SagaTransactionType.QuestCompleted:
                    ProjectQuestCompleted(avatarId, tx);
                    break;
                case SagaTransactionType.QuestFailed:
                    ProjectQuestFailed(avatarId, tx);
                    break;
                case SagaTransactionType.QuestAbandoned:
                    ProjectQuestAbandoned(avatarId, tx);
                    break;
                case SagaTransactionType.QuestStageAdvanced:
                    ProjectQuestStageAdvanced(avatarId, tx);
                    break;

                case SagaTransactionType.CharacterDefeated:
                    ProjectCharacterDefeated(avatarId, tx);
                    break;

                case SagaTransactionType.ReputationChanged:
                    ProjectReputationChanged(avatarId, tx);
                    break;

                case SagaTransactionType.TraitAssigned:
                    ProjectTraitAssigned(avatarId, tx);
                    break;
                case SagaTransactionType.TraitRemoved:
                    ProjectTraitRemoved(avatarId, tx);
                    break;
            }
        }
    }

    // ===== READERS =====

    public bool HasQuestToken(Guid avatarId, string questTokenRef)
        => _questTokens.Exists(x => x.AvatarId == avatarId && x.QuestTokenRef == questTokenRef);

    public HashSet<string> GetAllQuestTokens(Guid avatarId)
        => _questTokens.Find(x => x.AvatarId == avatarId).Select(x => x.QuestTokenRef).ToHashSet();

    public QuestProgressStatus? GetQuestStatus(Guid avatarId, string questRef)
        => _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef)?.Status;

    public AvatarQuestProgress? GetQuestProgress(Guid avatarId, string questRef)
        => _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef);

    public int GetBossDefeatedCount(Guid avatarId, string characterRef)
        => _bossDefeats.FindOne(x => x.AvatarId == avatarId && x.CharacterRef == characterRef)?.DefeatedCount ?? 0;

    public int GetFactionReputation(Guid avatarId, string factionRef)
        => _factionReputation.FindOne(x => x.AvatarId == avatarId && x.FactionRef == factionRef)?.ReputationValue ?? 0;

    public int? GetCharacterTraitValue(Guid avatarId, string characterRef, string traitType)
        => _characterTraits.FindOne(x => x.AvatarId == avatarId && x.CharacterRef == characterRef && x.TraitType == traitType)?.TraitValue;

    // ===== PROJECTION METHODS =====

    private void ProjectQuestToken(Guid avatarId, string sagaRef, SagaTransaction tx)
    {
        var tokenRef = tx.Data.GetValueOrDefault(TransactionDataKeys.QuestTokenRef);
        if (string.IsNullOrEmpty(tokenRef)) return;
        if (HasQuestToken(avatarId, tokenRef)) return;

        _questTokens.Insert(new AvatarQuestToken
        {
            AvatarId = avatarId,
            QuestTokenRef = tokenRef,
            AwardedAt = tx.LocalTimestamp,
            SourceSagaRef = sagaRef
        });
    }

    private void ProjectQuestAccepted(Guid avatarId, string sagaRef, SagaTransaction tx)
    {
        var questRef = tx.Data.GetValueOrDefault(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef)) return;

        var existing = _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef);
        if (existing != null)
        {
            existing.Status = QuestProgressStatus.Active;
            existing.CurrentStage = null;
            existing.AcceptedAt = tx.LocalTimestamp;
            existing.CompletedAt = null;
            existing.OwnerSagaRef = sagaRef;
            _questProgress.Update(existing);
        }
        else
        {
            _questProgress.Insert(new AvatarQuestProgress
            {
                AvatarId = avatarId,
                QuestRef = questRef,
                Status = QuestProgressStatus.Active,
                AcceptedAt = tx.LocalTimestamp,
                OwnerSagaRef = sagaRef
            });
        }
    }

    private void ProjectQuestCompleted(Guid avatarId, SagaTransaction tx)
    {
        var questRef = tx.Data.GetValueOrDefault(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef)) return;

        var existing = _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef);
        if (existing != null)
        {
            existing.Status = QuestProgressStatus.Completed;
            existing.CompletedAt = tx.LocalTimestamp;
            _questProgress.Update(existing);
        }
    }

    private void ProjectQuestFailed(Guid avatarId, SagaTransaction tx)
    {
        var questRef = tx.Data.GetValueOrDefault(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef)) return;

        var existing = _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef);
        if (existing != null)
        {
            existing.Status = QuestProgressStatus.Failed;
            existing.CompletedAt = tx.LocalTimestamp;
            _questProgress.Update(existing);
        }
    }

    private void ProjectQuestAbandoned(Guid avatarId, SagaTransaction tx)
    {
        var questRef = tx.Data.GetValueOrDefault(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef)) return;

        var existing = _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef);
        if (existing != null)
        {
            existing.Status = QuestProgressStatus.Abandoned;
            existing.CompletedAt = tx.LocalTimestamp;
            _questProgress.Update(existing);
        }
    }

    private void ProjectQuestStageAdvanced(Guid avatarId, SagaTransaction tx)
    {
        var questRef = tx.Data.GetValueOrDefault(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef)) return;

        var nextStage = tx.Data.GetValueOrDefault(TransactionDataKeys.NextStage);
        var existing = _questProgress.FindOne(x => x.AvatarId == avatarId && x.QuestRef == questRef);
        if (existing != null)
        {
            existing.CurrentStage = nextStage;
            _questProgress.Update(existing);
        }
    }

    private void ProjectCharacterDefeated(Guid avatarId, SagaTransaction tx)
    {
        var characterRef = tx.Data.GetValueOrDefault(TransactionDataKeys.CharacterRef);
        if (string.IsNullOrEmpty(characterRef)) return;

        var existing = _bossDefeats.FindOne(x => x.AvatarId == avatarId && x.CharacterRef == characterRef);
        if (existing != null)
        {
            existing.DefeatedCount++;
            existing.LastDefeatedAt = tx.LocalTimestamp;
            _bossDefeats.Update(existing);
        }
        else
        {
            _bossDefeats.Insert(new AvatarBossDefeat
            {
                AvatarId = avatarId,
                CharacterRef = characterRef,
                DefeatedCount = 1,
                LastDefeatedAt = tx.LocalTimestamp
            });
        }
    }

    private void ProjectReputationChanged(Guid avatarId, SagaTransaction tx)
    {
        var factionRef = tx.Data.GetValueOrDefault(TransactionDataKeys.FactionRef);
        if (string.IsNullOrEmpty(factionRef)) return;
        if (!int.TryParse(tx.Data.GetValueOrDefault(TransactionDataKeys.Amount), out var amount)) return;

        var existing = _factionReputation.FindOne(x => x.AvatarId == avatarId && x.FactionRef == factionRef);
        if (existing != null)
        {
            existing.ReputationValue += amount;
            existing.LastChangedAt = tx.LocalTimestamp;
            _factionReputation.Update(existing);
        }
        else
        {
            _factionReputation.Insert(new AvatarFactionReputation
            {
                AvatarId = avatarId,
                FactionRef = factionRef,
                ReputationValue = amount,
                LastChangedAt = tx.LocalTimestamp
            });
        }
    }

    private void ProjectTraitAssigned(Guid avatarId, SagaTransaction tx)
    {
        var characterRef = tx.Data.GetValueOrDefault(TransactionDataKeys.CharacterRef);
        var traitType = tx.Data.GetValueOrDefault(TransactionDataKeys.TraitType);
        if (string.IsNullOrEmpty(characterRef) || string.IsNullOrEmpty(traitType)) return;

        int? traitValue = tx.Data.TryGetValue(TransactionDataKeys.TraitValue, out var val) && int.TryParse(val, out var v) ? v : null;

        var existing = _characterTraits.FindOne(x => x.AvatarId == avatarId && x.CharacterRef == characterRef && x.TraitType == traitType);
        if (existing != null)
        {
            existing.TraitValue = traitValue;
            existing.AssignedAt = tx.LocalTimestamp;
            _characterTraits.Update(existing);
        }
        else
        {
            _characterTraits.Insert(new AvatarCharacterTrait
            {
                AvatarId = avatarId,
                CharacterRef = characterRef,
                TraitType = traitType,
                TraitValue = traitValue,
                AssignedAt = tx.LocalTimestamp
            });
        }
    }

    private void ProjectTraitRemoved(Guid avatarId, SagaTransaction tx)
    {
        var characterRef = tx.Data.GetValueOrDefault(TransactionDataKeys.CharacterRef);
        var traitType = tx.Data.GetValueOrDefault(TransactionDataKeys.TraitType);
        if (string.IsNullOrEmpty(characterRef) || string.IsNullOrEmpty(traitType)) return;

        var existing = _characterTraits.FindOne(x => x.AvatarId == avatarId && x.CharacterRef == characterRef && x.TraitType == traitType);
        if (existing != null)
        {
            _characterTraits.Delete(existing.Id);
        }
    }
}
