using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Cross-arc integration tests for the avatar progress tables feature.
/// Verifies the end-to-end flow: commit a transaction in one saga arc,
/// avatar progress table is updated, readers in a different arc see the change.
/// This is the fundamental behavior that replaced the old QuestTokenFanOut system.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class CrossArcProgressTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly SagaInstanceRepository _sagaRepo;
    private readonly AvatarProgressRepository _progressRepo;
    private readonly Guid _avatarId = Guid.NewGuid();

    public CrossArcProgressTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _sagaRepo = new SagaInstanceRepository(_database);
        _progressRepo = new AvatarProgressRepository(_database);
        _sagaRepo.SetAvatarProgressRepository(_progressRepo);
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    #region Helpers

    private List<SagaTransaction> CreateTokenTransaction(string avatarId, string tokenRef) => new()
    {
        new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.QuestTokenAwarded,
            AvatarId = avatarId,
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestTokenRef] = tokenRef,
                [TransactionDataKeys.Reason] = "Test"
            }
        }
    };

    private List<SagaTransaction> CreateQuestAcceptedTransaction(string avatarId, string questRef) => new()
    {
        new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.QuestAccepted,
            AvatarId = avatarId,
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestRef] = questRef,
                [TransactionDataKeys.QuestGiverRef] = "NPC_TEST"
            }
        }
    };

    private List<SagaTransaction> CreateReputationTransaction(string avatarId, string factionRef, int amount) => new()
    {
        new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ReputationChanged,
            AvatarId = avatarId,
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = factionRef,
                [TransactionDataKeys.Amount] = amount.ToString()
            }
        }
    };

    private List<SagaTransaction> CreateCharacterDefeatedTransaction(string avatarId, string characterRef) => new()
    {
        new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterDefeated,
            AvatarId = avatarId,
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterRef] = characterRef,
                [TransactionDataKeys.DefeatMethod] = "Combat"
            }
        }
    };

    private async Task<SagaInstance> CreateArcInstance(string arcRef)
    {
        return await _sagaRepo.GetOrCreateInstanceAsync(_avatarId, arcRef);
    }

    #endregion

    #region Test 1: Token earned in Arc A, readable globally

    [Fact]
    public async Task TokenEarnedInArcA_IsReadableGlobally()
    {
        // Arrange: Create saga instance for ArcA
        var arcAInstance = await CreateArcInstance("ArcA");

        // Act: Commit QuestTokenAwarded transaction to ArcA
        var tokenTransactions = CreateTokenTransaction(_avatarId.ToString(), "ANCIENT_SEAL");
        await _sagaRepo.AddAndCommitTransactionsAsync(arcAInstance.InstanceId, tokenTransactions);

        // Assert: progressRepo.HasQuestToken returns true (projection fired on commit)
        Assert.True(_progressRepo.HasQuestToken(_avatarId, "ANCIENT_SEAL"));
    }

    #endregion

    #region Test 2: Token earned in Arc A, trigger unlocks in Arc B

    [Fact]
    public async Task TokenEarnedInArcA_UnlocksTriggerInArcB()
    {
        // Arrange: Create saga instances for ArcA and ArcB
        var arcAInstance = await CreateArcInstance("ArcA");
        var arcBInstance = await CreateArcInstance("ArcB");

        // Act: Commit QuestTokenAwarded("KEY_TOKEN") to ArcA
        var tokenTransactions = CreateTokenTransaction(_avatarId.ToString(), "KEY_TOKEN");
        await _sagaRepo.AddAndCommitTransactionsAsync(arcAInstance.InstanceId, tokenTransactions);

        // Create a SagaTrigger with RequiresQuestTokenRef = ["KEY_TOKEN"]
        var trigger = new SagaTrigger
        {
            RefName = "ARCB_TRIGGER",
            DisplayName = "Arc B Gate",
            RequiresQuestTokenRef = new[] { "KEY_TOKEN" }
        };

        // Assert: TriggerAvailabilityChecker.CanActivate returns true
        var canActivate = TriggerAvailabilityChecker.CanActivate(trigger, _progressRepo, _avatarId);
        Assert.True(canActivate, "Trigger should be activatable using token earned in a different arc");
    }

    #endregion

    #region Test 3: Quest accepted in Arc A, dialogue checks in Arc B

    [Fact]
    public async Task QuestAcceptedInArcA_DialogueChecksInArcB()
    {
        // Arrange: Create saga instance for ArcA
        var arcAInstance = await CreateArcInstance("ArcA");

        // Act: Commit QuestAccepted transaction to ArcA
        var questTransactions = CreateQuestAcceptedTransaction(_avatarId.ToString(), "MAIN_QUEST");
        await _sagaRepo.AddAndCommitTransactionsAsync(arcAInstance.InstanceId, questTransactions);

        // Assert: progressRepo.GetQuestStatus returns Active
        var status = _progressRepo.GetQuestStatus(_avatarId, "MAIN_QUEST");
        Assert.Equal(QuestProgressStatus.Active, status);

        // Create DirectDialogueStateProvider with progressRepo and verify IsQuestActive
        var world = TestWorldFactory.CreateMinimalWorld();
        var avatar = TestWorldFactory.CreateTestAvatar();

        var provider = new DirectDialogueStateProvider(
            world,
            avatar,
            _progressRepo,
            _avatarId.ToString());

        Assert.True(provider.IsQuestActive("MAIN_QUEST"),
            "Dialogue state provider should see quest as active from a different arc's transaction");
    }

    #endregion

    #region Test 4: Reputation earned across multiple arcs sums correctly

    [Fact]
    public async Task ReputationEarnedAcrossMultipleArcs_SumsCorrectly()
    {
        // Arrange: Create saga instances for ArcA and ArcB
        var arcAInstance = await CreateArcInstance("ArcA");
        var arcBInstance = await CreateArcInstance("ArcB");

        // Act: Commit ReputationChanged +100 to ArcA
        var repTransactionsA = CreateReputationTransaction(_avatarId.ToString(), "GUILD_OF_MAGES", 100);
        await _sagaRepo.AddAndCommitTransactionsAsync(arcAInstance.InstanceId, repTransactionsA);

        // Commit ReputationChanged +50 to ArcB
        var repTransactionsB = CreateReputationTransaction(_avatarId.ToString(), "GUILD_OF_MAGES", 50);
        await _sagaRepo.AddAndCommitTransactionsAsync(arcBInstance.InstanceId, repTransactionsB);

        // Assert: progressRepo.GetFactionReputation returns 150 (sum of both arcs)
        var totalReputation = _progressRepo.GetFactionReputation(_avatarId, "GUILD_OF_MAGES");
        Assert.Equal(150, totalReputation);
    }

    #endregion

    #region Test 5: Boss defeated in Arc A, dialogue checks count

    [Fact]
    public async Task BossDefeatedInArcA_DialogueChecksCount()
    {
        // Arrange: Create saga instance for ArcA
        var arcAInstance = await CreateArcInstance("ArcA");

        // Act: Commit CharacterDefeated transaction to ArcA
        var defeatTransactions = CreateCharacterDefeatedTransaction(_avatarId.ToString(), "DRAGON_LORD");
        await _sagaRepo.AddAndCommitTransactionsAsync(arcAInstance.InstanceId, defeatTransactions);

        // Assert: progressRepo.GetBossDefeatedCount returns 1
        var count = _progressRepo.GetBossDefeatedCount(_avatarId, "DRAGON_LORD");
        Assert.Equal(1, count);

        // Create DirectDialogueStateProvider with progressRepo and verify GetBossDefeatedCount
        var world = TestWorldFactory.CreateMinimalWorld();
        var avatar = TestWorldFactory.CreateTestAvatar();

        var provider = new DirectDialogueStateProvider(
            world,
            avatar,
            _progressRepo,
            _avatarId.ToString());

        Assert.Equal(1, provider.GetBossDefeatedCount("DRAGON_LORD"));
    }

    #endregion
}
