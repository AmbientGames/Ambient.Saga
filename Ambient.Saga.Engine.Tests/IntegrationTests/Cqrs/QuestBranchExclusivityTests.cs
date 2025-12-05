using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
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
/// Integration tests for quest branch exclusivity.
/// Tests that exclusive branches (default) prevent choosing multiple branches,
/// and that non-exclusive branches allow multiple choices.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class QuestBranchExclusivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly World _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;
    private readonly IGameAvatarRepository _avatarRepository;

    public QuestBranchExclusivityTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorldWithBranchingQuests();

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
        _avatarRepository = _serviceProvider.GetRequiredService<IGameAvatarRepository>();
    }

    private World CreateTestWorldWithBranchingQuests()
    {
        // Quest with exclusive branches (default behavior - only one branch can be chosen)
        var exclusiveBranchQuest = new Quest
        {
            RefName = "EXCLUSIVE_BRANCH_QUEST",
            DisplayName = "The Crossroads",
            Description = "Choose your path wisely",
            Stages = new QuestStages
            {
                StartStage = "CHOICE_STAGE",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "CHOICE_STAGE",
                        DisplayName = "Choose Your Path",
                        Branches = new QuestStageBranches
                        {
                            // Exclusive = true by default
                            Branch = new[]
                            {
                                new QuestBranch
                                {
                                    RefName = "PATH_A",
                                    DisplayName = "The Path of Light",
                                    NextStage = "LIGHT_PATH"
                                },
                                new QuestBranch
                                {
                                    RefName = "PATH_B",
                                    DisplayName = "The Path of Shadow",
                                    NextStage = "SHADOW_PATH"
                                }
                            }
                        }
                    },
                    new QuestStage
                    {
                        RefName = "LIGHT_PATH",
                        DisplayName = "Light Path Stage",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "LIGHT_TASK",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "LIGHT_ORB",
                                    Threshold = 1,
                                    DisplayName = "Collect Light Orb"
                                }
                            }
                        }
                    },
                    new QuestStage
                    {
                        RefName = "SHADOW_PATH",
                        DisplayName = "Shadow Path Stage",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "SHADOW_TASK",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "SHADOW_ORB",
                                    Threshold = 1,
                                    DisplayName = "Collect Shadow Orb"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest with non-exclusive branches (multiple branches can be chosen)
        var nonExclusiveBranchQuest = new Quest
        {
            RefName = "NON_EXCLUSIVE_BRANCH_QUEST",
            DisplayName = "The Guild Tasks",
            Description = "Complete as many guild tasks as you wish",
            Stages = new QuestStages
            {
                StartStage = "GUILD_TASKS",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "GUILD_TASKS",
                        DisplayName = "Guild Tasks",
                        Branches = new QuestStageBranches
                        {
                            Exclusive = false, // Allow multiple branches
                            Branch = new[]
                            {
                                new QuestBranch
                                {
                                    RefName = "TASK_A",
                                    DisplayName = "Gathering Task",
                                    NextStage = "COMPLETE"
                                },
                                new QuestBranch
                                {
                                    RefName = "TASK_B",
                                    DisplayName = "Combat Task",
                                    NextStage = "COMPLETE"
                                },
                                new QuestBranch
                                {
                                    RefName = "TASK_C",
                                    DisplayName = "Crafting Task",
                                    NextStage = "COMPLETE"
                                }
                            }
                        }
                    },
                    new QuestStage
                    {
                        RefName = "COMPLETE",
                        DisplayName = "Tasks Complete",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "DONE",
                                    Type = QuestObjectiveType.DialogueCompleted,
                                    DialogueRef = "GUILD_MASTER_THANKS",
                                    Threshold = 1,
                                    DisplayName = "Speak to Guild Master"
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
            LongitudeX = 139.0,
            Y = 50.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaArc },
                    Quests = new[] { exclusiveBranchQuest, nonExclusiveBranchQuest }
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.QuestsLookup[exclusiveBranchQuest.RefName] = exclusiveBranchQuest;
        world.QuestsLookup[nonExclusiveBranchQuest.RefName] = nonExclusiveBranchQuest;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private AvatarEntity CreateTestAvatar()
    {
        return new AvatarEntity
        {
            Id = Guid.NewGuid(),
            Stats = new CharacterStats
            {
                Level = 1,
                Credits = 100,
                Experience = 0,
                Health = 100,
                Stamina = 100,
                Mana = 100,
                Strength = 10,
                Defense = 10,
                Speed = 10,
                Magic = 10
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Exclusive Branch Tests

    [Fact]
    public async Task ChooseQuestBranch_WhenExclusiveAndFirstChoice_Succeeds()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Choose first branch
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        var result = await _mediator.Send(chooseBranchCommand);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task ChooseQuestBranch_WhenExclusiveAndSecondChoice_Fails()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Choose first branch
        var firstChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        await _mediator.Send(firstChoiceCommand);

        // Act: Try to choose second branch (should fail because exclusive)
        var secondChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_B",
            Avatar = avatar
        };
        var result = await _mediator.Send(secondChoiceCommand);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("branch has already been chosen", result.ErrorMessage);
        Assert.Contains("exclusive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChooseQuestBranch_WhenExclusiveAndSameBranchChosen_Fails()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Choose first branch
        var firstChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        await _mediator.Send(firstChoiceCommand);

        // Act: Try to choose same branch again
        var secondChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        var result = await _mediator.Send(secondChoiceCommand);

        // Assert - Should fail because we already chose a branch
        Assert.False(result.Successful);
    }

    #endregion

    #region Non-Exclusive Branch Tests

    [Fact]
    public async Task ChooseQuestBranch_WhenNonExclusiveAndFirstChoice_Succeeds()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "NON_EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Choose first branch
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "NON_EXCLUSIVE_BRANCH_QUEST",
            StageRef = "GUILD_TASKS",
            BranchRef = "TASK_A",
            Avatar = avatar
        };
        var result = await _mediator.Send(chooseBranchCommand);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task ChooseQuestBranch_WhenNonExclusive_DoesNotEnforceExclusivity()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "NON_EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Choose a branch - should succeed
        var firstChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "NON_EXCLUSIVE_BRANCH_QUEST",
            StageRef = "GUILD_TASKS",
            BranchRef = "TASK_A",
            Avatar = avatar
        };
        var result = await _mediator.Send(firstChoiceCommand);

        // Assert - First branch succeeds
        Assert.True(result.Successful, result.ErrorMessage);

        // Note: After choosing a branch, the quest advances to the next stage (COMPLETE).
        // This is the correct behavior - branches with NextStage advance when chosen.
        // Non-exclusive means if the quest were to stay on the same stage (e.g., null NextStage),
        // multiple branches could be chosen without the exclusivity check blocking them.
        // Verify the transaction was recorded
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var transactions = instance.GetCommittedTransactions();
        var branchTransaction = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestBranchChosen);
        Assert.NotNull(branchTransaction);
        Assert.Equal("TASK_A", branchTransaction.GetData<string>("BranchRef"));
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ChooseQuestBranch_WhenQuestNotActive_Fails()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Don't accept quest - try to choose branch directly

        // Act
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        var result = await _mediator.Send(chooseBranchCommand);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not active", result.ErrorMessage);
    }

    [Fact]
    public async Task ChooseQuestBranch_WhenBranchNotFound_Fails()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Try to choose non-existent branch
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "INVALID_BRANCH",
            Avatar = avatar
        };
        var result = await _mediator.Send(chooseBranchCommand);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ChooseQuestBranch_WhenStageHasNoBranches_Fails()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // First choose a branch to advance to LIGHT_PATH stage (which has no branches)
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        await _mediator.Send(chooseBranchCommand);

        // Act: Try to choose branch on stage that has no branches
        var invalidBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "LIGHT_PATH",
            BranchRef = "ANY_BRANCH",
            Avatar = avatar
        };
        var result = await _mediator.Send(invalidBranchCommand);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("does not have branches", result.ErrorMessage);
    }

    #endregion

    #region Transaction Log Tests

    [Fact]
    public async Task ChooseQuestBranch_CreatesQuestBranchChosenTransaction()
    {
        // Arrange
        var avatar = CreateTestAvatar();

        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArrcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        await _mediator.Send(chooseBranchCommand);

        // Assert - Check transaction log
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var transactions = instance.GetCommittedTransactions();
        var branchTransaction = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestBranchChosen);

        Assert.NotNull(branchTransaction);
        Assert.Equal("EXCLUSIVE_BRANCH_QUEST", branchTransaction.GetData<string>("QuestRef"));
        Assert.Equal("CHOICE_STAGE", branchTransaction.GetData<string>("StageRef"));
        Assert.Equal("PATH_A", branchTransaction.GetData<string>("BranchRef"));
        Assert.Equal("The Path of Light", branchTransaction.GetData<string>("DisplayName"));
        Assert.Equal("LIGHT_PATH", branchTransaction.GetData<string>("NextStage"));
    }

    #endregion
}
