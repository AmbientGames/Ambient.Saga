using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Engine.Tests.Infrastructure;

public class AvatarProgressRepositoryTests : IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly LiteDatabase _db;
    private readonly AvatarProgressRepository _repo;
    private readonly Guid _avatarId = Guid.NewGuid();
    private const string SagaRef = "saga-test-001";

    public AvatarProgressRepositoryTests()
    {
        _db = new LiteDatabase(_stream);
        _repo = new AvatarProgressRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _stream.Dispose();
    }

    private static SagaTransaction CreateTransaction(SagaTransactionType type, Dictionary<string, string> data, string? avatarId = null)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = avatarId,
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow,
            Data = data
        };
    }

    // ===== Quest Tokens =====

    [Fact]
    public void QuestTokenAwarded_HasQuestToken_ReturnsTrue()
    {
        var tx = CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-dragonslayer"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.True(_repo.HasQuestToken(_avatarId, "token-dragonslayer"));
    }

    [Fact]
    public void GetAllQuestTokens_ReturnsAllTokens()
    {
        var tx1 = CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-a"
        });
        var tx2 = CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-b"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        var tokens = _repo.GetAllQuestTokens(_avatarId);
        Assert.Equal(2, tokens.Count);
        Assert.Contains("token-a", tokens);
        Assert.Contains("token-b", tokens);
    }

    [Fact]
    public void QuestTokenAwarded_Idempotent_NoDuplicates()
    {
        var tx1 = CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-unique"
        });
        var tx2 = CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-unique"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1]);
        _repo.ProjectTransactions(_avatarId, SagaRef, [tx2]);

        var tokens = _repo.GetAllQuestTokens(_avatarId);
        Assert.Single(tokens);
        Assert.True(_repo.HasQuestToken(_avatarId, "token-unique"));
    }

    [Fact]
    public void QuestTokens_DifferentAvatars_Independent()
    {
        var avatar2 = Guid.NewGuid();
        var tx = CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-shared"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.True(_repo.HasQuestToken(_avatarId, "token-shared"));
        Assert.False(_repo.HasQuestToken(avatar2, "token-shared"));
    }

    // ===== Quest Progress =====

    [Fact]
    public void QuestAccepted_StatusIsActive()
    {
        var tx = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-01"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.Equal(QuestProgressStatus.Active, _repo.GetQuestStatus(_avatarId, "quest-01"));
    }

    [Fact]
    public void QuestCompleted_StatusIsCompleted()
    {
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-02"
        });
        var complete = CreateTransaction(SagaTransactionType.QuestCompleted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-02"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [accept, complete]);

        Assert.Equal(QuestProgressStatus.Completed, _repo.GetQuestStatus(_avatarId, "quest-02"));
    }

    [Fact]
    public void QuestAbandoned_StatusIsAbandoned()
    {
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-03"
        });
        var abandon = CreateTransaction(SagaTransactionType.QuestAbandoned, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-03"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [accept, abandon]);

        Assert.Equal(QuestProgressStatus.Abandoned, _repo.GetQuestStatus(_avatarId, "quest-03"));
    }

    [Fact]
    public void QuestStageAdvanced_CurrentStageUpdated()
    {
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-05"
        });
        var advance = CreateTransaction(SagaTransactionType.QuestStageAdvanced, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-05",
            [TransactionDataKeys.NextStage] = "stage-2"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [accept, advance]);

        var progress = _repo.GetQuestProgress(_avatarId, "quest-05");
        Assert.NotNull(progress);
        Assert.Equal("stage-2", progress.CurrentStage);
    }

    [Fact]
    public void QuestFullLifecycle_AcceptAdvanceComplete()
    {
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-lifecycle"
        });
        var advance = CreateTransaction(SagaTransactionType.QuestStageAdvanced, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-lifecycle",
            [TransactionDataKeys.NextStage] = "stage-final"
        });
        var complete = CreateTransaction(SagaTransactionType.QuestCompleted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-lifecycle"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [accept, advance, complete]);

        var progress = _repo.GetQuestProgress(_avatarId, "quest-lifecycle");
        Assert.NotNull(progress);
        Assert.Equal(QuestProgressStatus.Completed, progress.Status);
        Assert.Equal("stage-final", progress.CurrentStage);
        Assert.NotNull(progress.CompletedAt);
    }

    [Fact]
    public void QuestReacceptAfterAbandon_StatusReturnsToActive()
    {
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-retry"
        });
        var abandon = CreateTransaction(SagaTransactionType.QuestAbandoned, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-retry"
        });
        var reaccept = CreateTransaction(SagaTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "quest-retry"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [accept, abandon, reaccept]);

        var progress = _repo.GetQuestProgress(_avatarId, "quest-retry");
        Assert.NotNull(progress);
        Assert.Equal(QuestProgressStatus.Active, progress.Status);
        Assert.Null(progress.CurrentStage);
        Assert.Null(progress.CompletedAt);
    }

    // ===== Boss Defeats =====

    [Fact]
    public void CharacterDefeated_CountReturnsOne()
    {
        var tx = CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "boss-lich-king"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.Equal(1, _repo.GetBossDefeatedCount(_avatarId, "boss-lich-king"));
    }

    [Fact]
    public void CharacterDefeatedTwice_CountReturnsTwo()
    {
        var tx1 = CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "boss-dragon"
        });
        var tx2 = CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "boss-dragon"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        Assert.Equal(2, _repo.GetBossDefeatedCount(_avatarId, "boss-dragon"));
    }

    [Fact]
    public void CharacterDefeated_DifferentCharacters_IndependentCounts()
    {
        var tx1 = CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "boss-a"
        });
        var tx2 = CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "boss-b"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        Assert.Equal(1, _repo.GetBossDefeatedCount(_avatarId, "boss-a"));
        Assert.Equal(1, _repo.GetBossDefeatedCount(_avatarId, "boss-b"));
    }

    // ===== Faction Reputation =====

    [Fact]
    public void ReputationChanged_SingleDelta_ReturnsValue()
    {
        var tx = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-elves",
            [TransactionDataKeys.Amount] = "100"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.Equal(100, _repo.GetFactionReputation(_avatarId, "faction-elves"));
    }

    [Fact]
    public void ReputationChanged_TwoPositiveDeltas_Accumulates()
    {
        var tx1 = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-dwarves",
            [TransactionDataKeys.Amount] = "100"
        });
        var tx2 = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-dwarves",
            [TransactionDataKeys.Amount] = "50"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        Assert.Equal(150, _repo.GetFactionReputation(_avatarId, "faction-dwarves"));
    }

    [Fact]
    public void ReputationChanged_NegativeDelta_Subtracts()
    {
        var tx1 = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-orcs",
            [TransactionDataKeys.Amount] = "100"
        });
        var tx2 = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-orcs",
            [TransactionDataKeys.Amount] = "-30"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        Assert.Equal(70, _repo.GetFactionReputation(_avatarId, "faction-orcs"));
    }

    [Fact]
    public void ReputationChanged_DifferentFactions_Independent()
    {
        var tx1 = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-x",
            [TransactionDataKeys.Amount] = "200"
        });
        var tx2 = CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "faction-y",
            [TransactionDataKeys.Amount] = "50"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        Assert.Equal(200, _repo.GetFactionReputation(_avatarId, "faction-x"));
        Assert.Equal(50, _repo.GetFactionReputation(_avatarId, "faction-y"));
    }

    // ===== Character Traits =====

    [Fact]
    public void TraitAssigned_GetTraitValue_ReturnsValue()
    {
        var tx = CreateTransaction(SagaTransactionType.TraitAssigned, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "npc-merchant",
            [TransactionDataKeys.TraitType] = "Loyalty",
            [TransactionDataKeys.TraitValue] = "75"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.Equal(75, _repo.GetCharacterTraitValue(_avatarId, "npc-merchant", "Loyalty"));
    }

    [Fact]
    public void TraitAssigned_NullValue_ReturnsNull()
    {
        var tx = CreateTransaction(SagaTransactionType.TraitAssigned, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "npc-guard",
            [TransactionDataKeys.TraitType] = "Suspicious"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx]);

        Assert.Null(_repo.GetCharacterTraitValue(_avatarId, "npc-guard", "Suspicious"));
    }

    [Fact]
    public void TraitRemoved_GetTraitValue_ReturnsNull()
    {
        var assign = CreateTransaction(SagaTransactionType.TraitAssigned, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "npc-thief",
            [TransactionDataKeys.TraitType] = "Trust",
            [TransactionDataKeys.TraitValue] = "50"
        });
        var remove = CreateTransaction(SagaTransactionType.TraitRemoved, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "npc-thief",
            [TransactionDataKeys.TraitType] = "Trust"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [assign, remove]);

        Assert.Null(_repo.GetCharacterTraitValue(_avatarId, "npc-thief", "Trust"));
    }

    [Fact]
    public void TraitAssigned_MultipleTraits_IndependentlyTracked()
    {
        var tx1 = CreateTransaction(SagaTransactionType.TraitAssigned, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "npc-wizard",
            [TransactionDataKeys.TraitType] = "Wisdom",
            [TransactionDataKeys.TraitValue] = "90"
        });
        var tx2 = CreateTransaction(SagaTransactionType.TraitAssigned, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "npc-wizard",
            [TransactionDataKeys.TraitType] = "Patience",
            [TransactionDataKeys.TraitValue] = "30"
        });

        _repo.ProjectTransactions(_avatarId, SagaRef, [tx1, tx2]);

        Assert.Equal(90, _repo.GetCharacterTraitValue(_avatarId, "npc-wizard", "Wisdom"));
        Assert.Equal(30, _repo.GetCharacterTraitValue(_avatarId, "npc-wizard", "Patience"));
    }
}
