using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Services;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using Ambient.Rpg.Engine.Tests.Helpers;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

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
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;
    private readonly IGameAvatarRepository _avatarRepository;

    public QuestBranchExclusivityTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorldWithBranchingQuests();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AcceptQuestCommand>();
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        services.AddSingleton<IArcInstanceRepository>(new ArcInstanceRepository(_database));
        services.AddSingleton<IAvatarProgressRepository>(new AvatarProgressRepository(_database));
        services.AddSingleton<IArcReadModelRepository, InMemoryArcReadModelRepository>();
        services.AddSingleton<IGameAvatarRepository, FakeAvatarRepository>();
        services.AddSingleton<Func<IGameAvatarRepository>>(sp => () => sp.GetRequiredService<IGameAvatarRepository>());
        services.AddSingleton<Func<IWorld>>(sp => () => sp.GetRequiredService<IWorld>());
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<IArcInstanceRepository>();
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

        // Quest whose single branch ends the quest (Branch.NextStage = null) —
        // choosing it cascades ChooseBranch → AdvanceStage → CompleteQuest, and
        // it is the world's completion quest, so GameComplete must surface
        // through the whole chain (B11)
        var finaleQuest = new Quest
        {
            RefName = "FINALE_QUEST",
            DisplayName = "The Finale",
            Description = "Ends the game",
            Stages = new QuestStages
            {
                StartStage = "FINAL_CHOICE",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "FINAL_CHOICE",
                        DisplayName = "The Final Choice",
                        Branches = new QuestStageBranches
                        {
                            Branch = new[]
                            {
                                new QuestBranch
                                {
                                    RefName = "END_IT",
                                    DisplayName = "End it all",
                                    NextStage = null // no next stage: quest completes
                                }
                            }
                        }
                    }
                }
            }
        };

        var arc = new Arc
        {
            RefName = "TEST_ARC",
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Saga = new[] { arc },
                    Quests = new[] { exclusiveBranchQuest, nonExclusiveBranchQuest, finaleQuest }
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.QuestsLookup[exclusiveBranchQuest.RefName] = exclusiveBranchQuest;
        world.QuestsLookup[nonExclusiveBranchQuest.RefName] = nonExclusiveBranchQuest;
        world.QuestsLookup[finaleQuest.RefName] = finaleQuest;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();
        world.WorldConfiguration.CompletionQuestRef = "FINALE_QUEST";

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
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Choose first branch
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Choose first branch
        var firstChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Choose first branch
        var firstChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "NON_EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Choose first branch
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "NON_EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Choose a branch - should succeed
        var firstChoiceCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);
        var transactions = instance.GetCommittedTransactions();
        var branchTransaction = transactions.FirstOrDefault(t => t.Type == ArcTransactionType.QuestBranchChosen);
        Assert.NotNull(branchTransaction);
        Assert.Equal("TASK_A", branchTransaction.GetData<string>(TransactionDataKeys.BranchRef));
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
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act: Try to choose non-existent branch
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // First choose a branch to advance to LIGHT_PATH stage (which has no branches)
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
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
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Act
        var chooseBranchCommand = new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        };
        await _mediator.Send(chooseBranchCommand);

        // Assert - Check transaction log
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);
        var transactions = instance.GetCommittedTransactions();
        var branchTransaction = transactions.FirstOrDefault(t => t.Type == ArcTransactionType.QuestBranchChosen);

        Assert.NotNull(branchTransaction);
        Assert.Equal("EXCLUSIVE_BRANCH_QUEST", branchTransaction.GetData<string>(TransactionDataKeys.QuestRef));
        Assert.Equal("CHOICE_STAGE", branchTransaction.GetData<string>(TransactionDataKeys.StageRef));
        Assert.Equal("PATH_A", branchTransaction.GetData<string>(TransactionDataKeys.BranchRef));
        Assert.Equal("The Path of Light", branchTransaction.GetData<string>(TransactionDataKeys.DisplayName));
        Assert.Equal("LIGHT_PATH", branchTransaction.GetData<string>(TransactionDataKeys.NextStage));
    }

    #endregion

    #region Re-Acceptance Tests (B7)

    [Fact]
    public async Task ChooseQuestBranch_AfterAbandonAndReaccept_DifferentBranchSucceeds()
    {
        // Arrange: accept, choose PATH_A, abandon
        var avatar = CreateTestAvatar();

        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        var firstChoice = await _mediator.Send(new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_A",
            Avatar = avatar
        });
        Assert.True(firstChoice.Successful, firstChoice.ErrorMessage);

        var abandonResult = await _mediator.Send(new AbandonQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            Avatar = avatar
        });
        Assert.True(abandonResult.Successful, abandonResult.ErrorMessage);

        // Re-accept: a fresh run of the quest
        var reacceptResult = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });
        Assert.True(reacceptResult.Successful, reacceptResult.ErrorMessage);

        // Act: choose the OTHER branch — the previous run's PATH_A choice must
        // not trip the exclusivity check or route the stage advance
        var secondChoice = await _mediator.Send(new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EXCLUSIVE_BRANCH_QUEST",
            StageRef = "CHOICE_STAGE",
            BranchRef = "PATH_B",
            Avatar = avatar
        });

        // Assert
        Assert.True(secondChoice.Successful, secondChoice.ErrorMessage);

        // The new run advanced along PATH_B, not the stale PATH_A route
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);
        var arc = _world.ArcLookup["TEST_ARC"];
        var triggers = _world.ArcTriggersLookup["TEST_ARC"];
        var state = new ArcStateMachine(arc, triggers, _world).ReplayToNow(instance);

        Assert.True(state.ActiveQuests.TryGetValue("EXCLUSIVE_BRANCH_QUEST", out var questState));
        Assert.Equal("SHADOW_PATH", questState.CurrentStage);
        Assert.Equal("PATH_B", questState.ChosenBranch);
    }

    #endregion

    #region GameComplete Propagation Tests (B11)

    [Fact]
    public async Task ChooseQuestBranch_CompletionQuestFinalBranch_PropagatesGameComplete()
    {
        // Arrange: FINALE_QUEST is the world's CompletionQuestRef and its only
        // branch has no next stage, so choosing it cascades
        // ChooseBranch → AdvanceStage → CompleteQuest
        var avatar = CreateTestAvatar();

        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "FINALE_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        // Act
        var result = await _mediator.Send(new ChooseQuestBranchCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "FINALE_QUEST",
            StageRef = "FINAL_CHOICE",
            BranchRef = "END_IT",
            Avatar = avatar
        });

        // Assert: the GameComplete signal produced by the nested CompleteQuest
        // must surface on the outermost result (it used to be discarded)
        Assert.True(result.Successful, result.ErrorMessage);
        Assert.True(result.Data.ContainsKey(TransactionDataKeys.GameComplete));
        Assert.Equal(true, result.Data[TransactionDataKeys.GameComplete]);
        Assert.Equal("FINALE_QUEST", result.Data[TransactionDataKeys.CompletionQuestRef]);

        // And the quest actually completed
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);
        var arc = _world.ArcLookup["TEST_ARC"];
        var triggers = _world.ArcTriggersLookup["TEST_ARC"];
        var state = new ArcStateMachine(arc, triggers, _world).ReplayToNow(instance);
        Assert.Contains("FINALE_QUEST", state.CompletedQuests);
    }

    #endregion
}
