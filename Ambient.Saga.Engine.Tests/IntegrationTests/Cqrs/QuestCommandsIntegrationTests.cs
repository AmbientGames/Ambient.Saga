using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using Ambient.Saga.Engine.Tests.Helpers;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for quest system via CQRS pipeline.
/// Tests quest acceptance, objective progression, stage advancement, and completion.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class QuestCommandsIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;
    private readonly Guid _testAvatarId = Guid.NewGuid();

    public QuestCommandsIntegrationTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorldWithQuests();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AcceptQuestCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IGameAvatarRepository, FakeAvatarRepository>();
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    private World CreateTestWorldWithQuests()
    {
        // Create a simple quest: Defeat 3 bandits
        var quest = new Quest
        {
            RefName = "DEFEAT_BANDITS",
            DisplayName = "Defeat the Bandits",
            Description = "Defeat 3 bandits threatening the village",
            Stages = new QuestStages
            {
                StartStage = "HUNT",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "HUNT",
                        DisplayName = "Hunt the Bandits",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "DEFEAT_BANDITS_OBJ",
                                    Type = QuestObjectiveType.CharactersDefeatedByTag,
                                    CharacterTag = "bandit",
                                    Threshold = 3,
                                    DisplayName = "Defeat bandits (0/3)"
                                }
                            }
                        }
                    }
                }
            },
            Rewards = new[]
            {
                new QuestReward
                {
                    Condition = QuestRewardCondition.OnSuccess,
                    Currency = new QuestRewardCurrency { Amount = 100 }
                }
            }
        };

        // Create a multi-stage quest with branches
        var murderMystery = new Quest
        {
            RefName = "MURDER_MYSTERY",
            DisplayName = "The Murder Mystery",
            Description = "Solve the murder case",
            Stages = new QuestStages
            {
                StartStage = "INVESTIGATION",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "INVESTIGATION",
                        DisplayName = "Investigate the Crime",
                        NextStage = "ACCUSATION",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "TALK_WITNESSES",
                                    Type = QuestObjectiveType.DialogueCompleted,
                                    CharacterTag = "witness",
                                    Threshold = 2,
                                    DisplayName = "Interview witnesses (0/2)"
                                },
                                new QuestObjective
                                {
                                    RefName = "FIND_WEAPON",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "BLOODY_KNIFE",
                                    Threshold = 1,
                                    DisplayName = "Find the murder weapon"
                                }
                            }
                        }
                    },
                    new QuestStage
                    {
                        RefName = "ACCUSATION",
                        DisplayName = "Make Your Accusation",
                        Branches = new QuestStageBranches
                        {
                            Exclusive = true,
                            Branch = new[]
                            {
                                new QuestBranch
                                {
                                    RefName = "ACCUSE_BUTLER",
                                    DisplayName = "Accuse the Butler",
                                    Objective = new QuestObjective
                                    {
                                        RefName = "BUTLER_CHOICE",
                                        Type = QuestObjectiveType.DialogueChoiceSelected,
                                        DialogueRef = "FINAL_ACCUSATION",
                                        ChoiceRef = "BUTLER",
                                        Threshold = 1
                                    }
                                },
                                new QuestBranch
                                {
                                    RefName = "ACCUSE_GARDENER",
                                    DisplayName = "Accuse the Gardener",
                                    Objective = new QuestObjective
                                    {
                                        RefName = "GARDENER_CHOICE",
                                        Type = QuestObjectiveType.DialogueChoiceSelected,
                                        DialogueRef = "FINAL_ACCUSATION",
                                        ChoiceRef = "GARDENER",
                                        Threshold = 1
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var sagaArc = new SagaArc
        {
            RefName = "TEST_SAGA",
            DisplayName = "Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaArc },
                    Quests = new[] { quest, murderMystery }
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.QuestsLookup[quest.RefName] = quest;
        world.QuestsLookup[murderMystery.RefName] = murderMystery;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private AvatarEntity CreateTestAvatar()
    {
        return new AvatarEntity
        {
            Id = _testAvatarId
        };
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region AcceptQuest Tests

    [Fact]
    public async Task AcceptQuest_Success_CreatesQuestAcceptedTransaction()
    {
        // Arrange
        var avatar = CreateTestAvatar();
        var command = new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotEmpty(result.TransactionIds);

        // Verify transaction was logged
        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        var transactions = instance.GetCommittedTransactions();
        var questAccepted = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestAccepted);

        Assert.NotNull(questAccepted);
        Assert.Equal("DEFEAT_BANDITS", questAccepted.GetData<string>("QuestRef"));
    }

    [Fact]
    public async Task AcceptQuest_AlreadyAccepted_ReturnsFail()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest first time
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        });

        // Try to accept again
        var command = new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("already accepted", result.ErrorMessage);
    }

    #endregion

    #region ProgressQuestObjective Tests

    [Fact]
    public async Task ProgressQuestObjective_ThresholdMet_CreatesObjectiveCompletedTransaction()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        });

        // Simulate defeating 3 bandits
        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        for (var i = 0; i < 3; i++)
        {
            var defeatTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = _testAvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterRef"] = $"BANDIT_{i}",
                    ["CharacterTag"] = "bandit"
                }
            };

            instance.AddTransaction(defeatTx);
            await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { defeatTx });
            await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });
        }

        // Act - Check objective progress
        var command = new ProgressQuestObjectiveCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            StageRef = "HUNT",
            ObjectiveRef = "DEFEAT_BANDITS_OBJ",
            Avatar = avatar
        };

        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify QuestObjectiveCompleted transaction was created
        instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        var transactions = instance.GetCommittedTransactions();
        var objectiveCompleted = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestObjectiveCompleted);

        Assert.NotNull(objectiveCompleted);
        Assert.Equal("DEFEAT_BANDITS_OBJ", objectiveCompleted.GetData<string>("ObjectiveRef"));
        Assert.Equal("3", objectiveCompleted.GetData<string>("CurrentValue"));
    }

    [Fact]
    public async Task ProgressQuestObjective_ThresholdNotMet_DoesNotCreateTransaction()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        });

        // Simulate defeating only 1 bandit (need 3)
        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        var defeatTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterDefeated,
            AvatarId = _testAvatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "BANDIT_1",
                ["CharacterTag"] = "bandit"
            }
        };

        instance.AddTransaction(defeatTx);
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { defeatTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });

        // Act
        var command = new ProgressQuestObjectiveCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            StageRef = "HUNT",
            ObjectiveRef = "DEFEAT_BANDITS_OBJ",
            Avatar = avatar
        };

        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful); // Command succeeds but no transaction created

        // Verify NO QuestObjectiveCompleted transaction was created
        instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        var transactions = instance.GetCommittedTransactions();
        var objectiveCompleted = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestObjectiveCompleted);

        Assert.Null(objectiveCompleted);
    }

    #endregion

    #region GetQuestProgress Tests

    [Fact]
    public async Task GetQuestProgress_ActiveQuest_ReturnsProgress()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        });

        // Defeat 1 bandit
        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        var defeatTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterDefeated,
            AvatarId = _testAvatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "BANDIT_1",
                ["CharacterTag"] = "bandit"
            }
        };

        instance.AddTransaction(defeatTx);
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { defeatTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });

        // Act
        var query = new GetQuestProgressQuery
        {
            AvatarId = _testAvatarId,
            SagaRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS"
        };

        var progress = await _mediator.Send(query);

        // Assert
        Assert.NotNull(progress);
        Assert.Equal("DEFEAT_BANDITS", progress.QuestRef);
        Assert.Equal("Defeat the Bandits", progress.DisplayName);
        Assert.Equal("Hunt the Bandits", progress.CurrentStageDisplayName);
        Assert.False(progress.IsComplete);

        var objective = progress.Objectives.FirstOrDefault();
        Assert.NotNull(objective);
        Assert.Equal(1, objective.CurrentValue);
        Assert.Equal(3, objective.TargetValue);
        Assert.False(objective.IsComplete);
    }

    [Fact]
    public async Task GetActiveQuests_ReturnsAllActiveQuests()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept multiple quests
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        });

        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "MURDER_MYSTERY",
            QuestGiverRef = "DETECTIVE",
            Avatar = avatar
        });

        // Act
        var query = new GetActiveQuestsQuery
        {
            AvatarId = _testAvatarId
        };

        var activeQuests = await _mediator.Send(query);

        // Assert
        Assert.Equal(2, activeQuests.Count);
        Assert.Contains(activeQuests, q => q.QuestRef == "DEFEAT_BANDITS");
        Assert.Contains(activeQuests, q => q.QuestRef == "MURDER_MYSTERY");
    }

    #endregion

    #region Full Quest Flow Test

    [Fact]
    public async Task FullQuestFlow_AcceptToCompletion_Success()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Step 1: Accept quest
        var acceptResult = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            QuestGiverRef = "QUEST_BOARD",
            Avatar = avatar
        });
        Assert.True(acceptResult.Successful);

        // Step 2: Defeat 3 bandits
        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        for (var i = 0; i < 3; i++)
        {
            var defeatTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = _testAvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterRef"] = $"BANDIT_{i}",
                    ["CharacterTag"] = "bandit"
                }
            };

            instance.AddTransaction(defeatTx);
            await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { defeatTx });
            await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });
        }

        // Step 3: Progress objective
        var progressResult = await _mediator.Send(new ProgressQuestObjectiveCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            StageRef = "HUNT",
            ObjectiveRef = "DEFEAT_BANDITS_OBJ",
            Avatar = avatar
        });
        Assert.True(progressResult.Successful);

        // Step 4: Advance stage (will auto-complete since only 1 stage)
        var advanceResult = await _mediator.Send(new AdvanceQuestStageCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS",
            Avatar = avatar
        });
        Assert.True(advanceResult.Successful);

        // Step 5: Verify quest is completed
        instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, "TEST_SAGA");
        var transactions = instance.GetCommittedTransactions();

        // Should have: QuestAccepted, CharacterDefeated (x3), QuestObjectiveCompleted, QuestStageAdvanced, QuestCompleted
        Assert.Contains(transactions, t => t.Type == SagaTransactionType.QuestAccepted);
        Assert.Equal(3, transactions.Count(t => t.Type == SagaTransactionType.CharacterDefeated));
        Assert.Contains(transactions, t => t.Type == SagaTransactionType.QuestObjectiveCompleted);
        Assert.Contains(transactions, t => t.Type == SagaTransactionType.QuestStageAdvanced);
        Assert.Contains(transactions, t => t.Type == SagaTransactionType.QuestCompleted);

        // Step 6: Verify quest shows as completed in query
        var progress = await _mediator.Send(new GetQuestProgressQuery
        {
            AvatarId = _testAvatarId,
            SagaRef = "TEST_SAGA",
            QuestRef = "DEFEAT_BANDITS"
        });

        Assert.NotNull(progress);
        Assert.True(progress.IsComplete);
        Assert.True(progress.IsSuccess);
    }

    #endregion
}
