using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using Ambient.Saga.Engine.Tests.Helpers;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for quest rewards and prerequisites.
/// Tests stage rewards, quest completion rewards, and prerequisite validation.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class QuestRewardsAndPrerequisitesTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;
    private readonly IGameAvatarRepository _avatarRepository;
    private readonly Guid _testAvatarId = Guid.NewGuid();

    public QuestRewardsAndPrerequisitesTests()
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
        var sagaInstanceRepo = new SagaInstanceRepository(_database);
        var avatarProgressRepo = new AvatarProgressRepository(_database);
        sagaInstanceRepo.SetAvatarProgressRepository(avatarProgressRepo);
        services.AddSingleton<ISagaInstanceRepository>(sagaInstanceRepo);
        services.AddSingleton<IAvatarProgressRepository>(avatarProgressRepo);
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IGameAvatarRepository, FakeAvatarRepository>();
        services.AddSingleton<Func<IGameAvatarRepository>>(sp => () => sp.GetRequiredService<IGameAvatarRepository>());
        services.AddSingleton<Func<IWorld>>(sp => () => sp.GetRequiredService<IWorld>());
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
        _avatarRepository = _serviceProvider.GetRequiredService<IGameAvatarRepository>();
    }

    private World CreateTestWorldWithQuests()
    {
        // Quest 1: Simple quest with stage rewards and completion rewards
        var collectHerbs = new Quest
        {
            RefName = "COLLECT_HERBS",
            DisplayName = "Collect Healing Herbs",
            Description = "Collect herbs for the healer",
            Stages = new QuestStages
            {
                StartStage = "GATHER",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "GATHER",
                        DisplayName = "Gather Herbs",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "COLLECT_HERBS_OBJ",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "HEALING_HERB",
                                    Threshold = 5,
                                    DisplayName = "Collect healing herbs (0/5)"
                                }
                            }
                        },
                        // Stage reward: Small currency bonus when stage completes
                        Rewards = new[]
                        {
                            new QuestReward
                            {
                                Condition = QuestRewardCondition.OnSuccess,
                                Currency = new QuestRewardCurrency { Amount = 50 },
                                Experience = new QuestRewardExperience { Amount = 10 }
                            }
                        }
                    }
                }
            },
            // Quest completion rewards
            Rewards = new[]
            {
                new QuestReward
                {
                    Condition = QuestRewardCondition.OnSuccess,
                    Currency = new QuestRewardCurrency { Amount = 100 },
                    Experience = new QuestRewardExperience { Amount = 50 },
                    Equipment = new[]
                    {
                        new QuestRewardEquipment { EquipmentRef = "HERBALIST_GLOVES", Quantity = 1 }
                    },
                    Consumable = new[]
                    {
                        new QuestRewardConsumable { ConsumableRef = "HEALTH_POTION", Quantity = 3 }
                    }
                }
            }
        };

        // Quest 2: Quest with level prerequisite
        var dragonHunt = new Quest
        {
            RefName = "DRAGON_HUNT",
            DisplayName = "Hunt the Dragon",
            Description = "Defeat the ancient dragon",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    MinimumLevel = 10,
                    MinimumLevelSpecified = true
                }
            },
            Stages = new QuestStages
            {
                StartStage = "HUNT",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "HUNT",
                        DisplayName = "Hunt the Dragon",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "DEFEAT_DRAGON",
                                    Type = QuestObjectiveType.CharacterDefeated,
                                    CharacterRef = "ANCIENT_DRAGON",
                                    Threshold = 1,
                                    DisplayName = "Defeat the dragon"
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
                    Currency = new QuestRewardCurrency { Amount = 1000 },
                    Experience = new QuestRewardExperience { Amount = 500 }
                }
            }
        };

        // Quest 3: Quest with item prerequisite
        var secretVault = new Quest
        {
            RefName = "SECRET_VAULT",
            DisplayName = "The Secret Vault",
            Description = "Open the secret vault",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    RequiredItemRef = "ANCIENT_KEY"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "OPEN_VAULT",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "OPEN_VAULT",
                        DisplayName = "Open the Vault",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "TRIGGER_VAULT",
                                    Type = QuestObjectiveType.TriggerActivated,
                                    TriggerRef = "VAULT_DOOR",
                                    Threshold = 1,
                                    DisplayName = "Open the vault door"
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
                    Equipment = new[]
                    {
                        new QuestRewardEquipment { EquipmentRef = "LEGENDARY_SWORD", Quantity = 1 }
                    }
                }
            }
        };

        // Quest 4: Quest with OnObjective rewards
        var multiObjectiveQuest = new Quest
        {
            RefName = "MULTI_OBJECTIVE_QUEST",
            DisplayName = "Multi-Objective Quest",
            Description = "A quest with per-objective rewards",
            Stages = new QuestStages
            {
                StartStage = "OBJECTIVES",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "OBJECTIVES",
                        DisplayName = "Complete Objectives",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "OBJECTIVE_A",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "ITEM_A",
                                    Threshold = 1,
                                    DisplayName = "Collect Item A"
                                },
                                new QuestObjective
                                {
                                    RefName = "OBJECTIVE_B",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "ITEM_B",
                                    Threshold = 1,
                                    DisplayName = "Collect Item B"
                                }
                            }
                        }
                    }
                }
            },
            // OnObjective rewards - currency awarded per objective completion
            Rewards = new[]
            {
                new QuestReward
                {
                    Condition = QuestRewardCondition.OnObjective,
                    ObjectiveRef = "OBJECTIVE_A",
                    Currency = new QuestRewardCurrency { Amount = 25 },
                    Consumable = new[]
                    {
                        new QuestRewardConsumable { ConsumableRef = "REWARD_ITEM_A", Quantity = 1 }
                    }
                },
                new QuestReward
                {
                    Condition = QuestRewardCondition.OnObjective,
                    ObjectiveRef = "OBJECTIVE_B",
                    Currency = new QuestRewardCurrency { Amount = 75 },
                    Consumable = new[]
                    {
                        new QuestRewardConsumable { ConsumableRef = "REWARD_ITEM_B", Quantity = 2 }
                    }
                },
                new QuestReward
                {
                    Condition = QuestRewardCondition.OnSuccess,
                    Currency = new QuestRewardCurrency { Amount = 100 }
                }
            }
        };

        // Quest 5: Quest chain (requires completing another quest)
        var herbMaster = new Quest
        {
            RefName = "HERB_MASTER",
            DisplayName = "Become a Herb Master",
            Description = "Master the art of herbalism",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    QuestRef = "COLLECT_HERBS"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "ADVANCED_GATHERING",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "ADVANCED_GATHERING",
                        DisplayName = "Advanced Gathering",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "COLLECT_RARE_HERBS",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "RARE_HERB",
                                    Threshold = 3,
                                    DisplayName = "Collect rare herbs (0/3)"
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
                    Condition = QuestRewardCondition.OnSuccess
                }
            }
        };

        // Quest 6: Quest with a Reputation reward (recorded as a ReputationChanged
        // transaction on the saga instance, like the dialogue ChangeReputation action)
        var guildErrand = new Quest
        {
            RefName = "GUILD_ERRAND",
            DisplayName = "An Errand for the Guild",
            Description = "Deliver supplies for the merchant guild",
            Stages = new QuestStages
            {
                StartStage = "DELIVER",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "DELIVER",
                        DisplayName = "Deliver the Supplies",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "COLLECT_CRATE",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "SUPPLY_CRATE",
                                    Threshold = 1,
                                    DisplayName = "Collect the supply crate"
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
                    Reputation = new[]
                    {
                        new QuestRewardReputation { FactionRef = "MERCHANT_GUILD", Amount = 250 }
                    }
                }
            }
        };

        // Quest 7: Quest with an Achievement reward (unlocked on the avatar's
        // Achievements ledger, like the dialogue UnlockAchievement action)
        var proveWorth = new Quest
        {
            RefName = "PROVE_WORTH",
            DisplayName = "Prove Your Worth",
            Description = "Earn the guild's trust",
            Stages = new QuestStages
            {
                StartStage = "TRIAL",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "TRIAL",
                        DisplayName = "Complete the Trial",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "COLLECT_SEAL",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "GUILD_SEAL",
                                    Threshold = 1,
                                    DisplayName = "Collect the guild seal"
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
                    Achievement = new[]
                    {
                        new QuestRewardAchievement { AchievementRef = "ACH_GUILD_FRIEND" }
                    }
                }
            }
        };

        var merchantGuild = new Faction
        {
            RefName = "MERCHANT_GUILD",
            DisplayName = "Merchant Guild"
        };

        var sagaArc = new SagaArc
        {
            RefName = "TEST_SAGA",
            DisplayName = "Test Saga",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaArc },
                    Quests = new[] { collectHerbs, dragonHunt, secretVault, multiObjectiveQuest, herbMaster, guildErrand, proveWorth }
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.QuestsLookup[collectHerbs.RefName] = collectHerbs;
        world.QuestsLookup[dragonHunt.RefName] = dragonHunt;
        world.QuestsLookup[secretVault.RefName] = secretVault;
        world.QuestsLookup[multiObjectiveQuest.RefName] = multiObjectiveQuest;
        world.QuestsLookup[herbMaster.RefName] = herbMaster;
        world.QuestsLookup[guildErrand.RefName] = guildErrand;
        world.QuestsLookup[proveWorth.RefName] = proveWorth;
        world.FactionsLookup[merchantGuild.RefName] = merchantGuild;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private AvatarEntity CreateTestAvatar(int level = 1, int credits = 0, int experience = 0, Guid? avatarId = null)
    {
        return new AvatarEntity
        {
            Id = avatarId ?? Guid.NewGuid(),
            Stats = new CharacterStats
            {
                Level = level,
                Credits = credits,
                Experience = experience,
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

    #region Stage Reward Tests

    [Fact]
    public async Task AdvanceQuestStage_WithStageRewards_AwardsCurrencyAndExperience()
    {
        // Arrange
        var avatar = CreateTestAvatar(level: 1, credits: 0, experience: 0);
        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "COLLECT_HERBS",
            QuestGiverRef = "HEALER",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Simulate collecting 5 herbs
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var lootTransactions = new List<SagaTransaction>();
        for (var i = 0; i < 5; i++)
        {
            var lootTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.LootAwarded,
                AvatarId = avatar.Id.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                    ["Consumables"] = "HEALING_HERB:1"
                }
            };
            lootTransactions.Add(lootTx);
        }
        await _repository.AddTransactionsAsync(instance.InstanceId, lootTransactions, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, lootTransactions.Select(t => t.TransactionId).ToList(), CancellationToken.None);

        // Act: Advance quest stage (should award stage rewards)
        var advanceCommand = new AdvanceQuestStageCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "COLLECT_HERBS",
            Avatar = avatar
        };
        var result = await _mediator.Send(advanceCommand);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);

        // Check avatar was awarded stage rewards PLUS quest completion rewards
        // (since this is the final stage, quest auto-completes)
        // Avatar is modified in place by the handler
        // Currency: 50 (stage) + 100 (quest) = 150
        Assert.Equal(150, avatar.Stats.Credits);
        // Experience: 10 (stage) + 50 (quest) = 60
        Assert.Equal(60, avatar.Stats.Experience);
    }

    #endregion

    #region Quest Completion Reward Tests

    [Fact]
    public async Task CompleteQuest_WithRewards_AwardsAllRewardTypes()
    {
        // Arrange
        var avatar = CreateTestAvatar(level: 1, credits: 0, experience: 0);
        // Accept quest
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "COLLECT_HERBS",
            QuestGiverRef = "HEALER",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Simulate completing all objectives
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var lootTransactions = new List<SagaTransaction>();
        for (var i = 0; i < 5; i++)
        {
            var lootTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.LootAwarded,
                AvatarId = avatar.Id.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                    ["Consumables"] = "HEALING_HERB:1"
                }
            };
            lootTransactions.Add(lootTx);
        }
        await _repository.AddTransactionsAsync(instance.InstanceId, lootTransactions, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, lootTransactions.Select(t => t.TransactionId).ToList(), CancellationToken.None);

        // Act: Advance stage (which auto-completes quest and awards all rewards)
        var advanceCommand = new AdvanceQuestStageCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "COLLECT_HERBS",
            Avatar = avatar
        };
        var result = await _mediator.Send(advanceCommand);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);

        // Check avatar was awarded quest completion rewards
        // Avatar is modified in place by the handler
        // Currency: 50 (stage) + 100 (quest) = 150
        Assert.Equal(150, avatar.Stats.Credits);

        // Experience: 10 (stage) + 50 (quest) = 60
        Assert.Equal(60, avatar.Stats.Experience);

        // Equipment: HERBALIST_GLOVES
        Assert.NotNull(avatar.Capabilities.Equipment);
        Assert.Contains(avatar.Capabilities.Equipment, e => e.EquipmentRef == "HERBALIST_GLOVES");

        // Consumables: 3x HEALTH_POTION
        Assert.NotNull(avatar.Capabilities.Consumables);
        var healthPotion = avatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "HEALTH_POTION");
        Assert.NotNull(healthPotion);
        Assert.Equal(3, healthPotion.Quantity);
    }

    #endregion

    #region Prerequisite Tests

    [Fact]
    public async Task AcceptQuest_WithMinimumLevelPrerequisite_FailsWhenLevelTooLow()
    {
        // Arrange: Avatar level 5, quest requires level 10
        var avatar = CreateTestAvatar(level: 5);
        var command = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DRAGON_HUNT",
            QuestGiverRef = "DRAGON_HUNTER",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("level 10", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptQuest_WithMinimumLevelPrerequisite_SucceedsWhenLevelMet()
    {
        // Arrange: Avatar level 10, quest requires level 10
        var avatar = CreateTestAvatar(level: 10);
        var command = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "DRAGON_HUNT",
            QuestGiverRef = "DRAGON_HUNTER",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithItemPrerequisite_FailsWhenItemMissing()
    {
        // Arrange: Avatar without ANCIENT_KEY
        var avatar = CreateTestAvatar();
        var command = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "SECRET_VAULT",
            QuestGiverRef = "VAULT_KEEPER",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("ANCIENT_KEY", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithItemPrerequisite_SucceedsWhenItemInEquipment()
    {
        // Arrange: Avatar with ANCIENT_KEY in equipment
        var avatar = CreateTestAvatar();
        avatar.Capabilities.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "ANCIENT_KEY", Condition = 1.0f }
        };
        var command = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "SECRET_VAULT",
            QuestGiverRef = "VAULT_KEEPER",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithQuestPrerequisite_FailsWhenPreviousQuestNotComplete()
    {
        // Arrange: Try to accept HERB_MASTER without completing COLLECT_HERBS
        var avatar = CreateTestAvatar();
        var command = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "HERB_MASTER",
            QuestGiverRef = "MASTER_HERBALIST",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("Collect Healing Herbs", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithQuestPrerequisite_SucceedsWhenPreviousQuestComplete()
    {
        // Arrange: Complete COLLECT_HERBS first
        var avatar = CreateTestAvatar();
        // Complete COLLECT_HERBS
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var questTransactions = new List<SagaTransaction>
        {
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestAccepted,
                AvatarId = avatar.Id.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["QuestRef"] = "COLLECT_HERBS" }
            },
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestCompleted,
                AvatarId = avatar.Id.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["QuestRef"] = "COLLECT_HERBS" }
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, questTransactions, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, questTransactions.Select(t => t.TransactionId).ToList(), CancellationToken.None);

        // Now accept HERB_MASTER
        var command = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "HERB_MASTER",
            QuestGiverRef = "MASTER_HERBALIST",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);
    }

    #endregion

    #region OnObjective Reward Tests

    [Fact]
    public async Task ProgressQuestObjective_WithOnObjectiveReward_AwardsRewardForSpecificObjective()
    {
        // Arrange
        var avatar = CreateTestAvatar(level: 1, credits: 0, experience: 0);
        // Accept quest with OnObjective rewards
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "MULTI_OBJECTIVE_QUEST",
            QuestGiverRef = "QUEST_GIVER",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Simulate collecting ITEM_A (triggers OBJECTIVE_A completion)
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var lootTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LootAwarded,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                ["Consumables"] = "ITEM_A:1"
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { lootTx }, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { lootTx.TransactionId }, CancellationToken.None);

        // Act: Progress objective A (should award OnObjective reward for OBJECTIVE_A)
        var progressCommand = new ProgressQuestObjectiveCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "MULTI_OBJECTIVE_QUEST",
            StageRef = "OBJECTIVES",
            ObjectiveRef = "OBJECTIVE_A",
            Avatar = avatar
        };
        var result = await _mediator.Send(progressCommand);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);

        // Check avatar was awarded OnObjective reward for OBJECTIVE_A (25 credits, 1x REWARD_ITEM_A)
        Assert.Equal(25, avatar.Stats.Credits);
        Assert.NotNull(avatar.Capabilities.Consumables);
        var rewardItemA = avatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "REWARD_ITEM_A");
        Assert.NotNull(rewardItemA);
        Assert.Equal(1, rewardItemA.Quantity);

        // Should NOT have OBJECTIVE_B reward yet
        var rewardItemB = avatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "REWARD_ITEM_B");
        Assert.Null(rewardItemB);
    }

    [Fact]
    public async Task ProgressQuestObjective_CompletingMultipleObjectives_AwardsSeparateRewards()
    {
        // Arrange
        var avatar = CreateTestAvatar(level: 1, credits: 0, experience: 0);
        // Accept quest with OnObjective rewards
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "MULTI_OBJECTIVE_QUEST",
            QuestGiverRef = "QUEST_GIVER",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);

        // Collect ITEM_A
        var lootTxA = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LootAwarded,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                ["Consumables"] = "ITEM_A:1"
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { lootTxA }, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { lootTxA.TransactionId }, CancellationToken.None);

        // Progress OBJECTIVE_A
        var progressA = new ProgressQuestObjectiveCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "MULTI_OBJECTIVE_QUEST",
            StageRef = "OBJECTIVES",
            ObjectiveRef = "OBJECTIVE_A",
            Avatar = avatar
        };
        await _mediator.Send(progressA);

        // Collect ITEM_B
        var lootTxB = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LootAwarded,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                ["Consumables"] = "ITEM_B:1"
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { lootTxB }, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { lootTxB.TransactionId }, CancellationToken.None);

        // Act: Progress OBJECTIVE_B
        var progressB = new ProgressQuestObjectiveCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "MULTI_OBJECTIVE_QUEST",
            StageRef = "OBJECTIVES",
            ObjectiveRef = "OBJECTIVE_B",
            Avatar = avatar
        };
        var result = await _mediator.Send(progressB);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);

        // Check avatar was awarded both OnObjective rewards
        // OBJECTIVE_A: 25 credits, 1x REWARD_ITEM_A
        // OBJECTIVE_B: 75 credits, 2x REWARD_ITEM_B
        // Total: 100 credits
        Assert.Equal(100, avatar.Stats.Credits);

        Assert.NotNull(avatar.Capabilities.Consumables);
        var rewardItemA = avatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "REWARD_ITEM_A");
        Assert.NotNull(rewardItemA);
        Assert.Equal(1, rewardItemA.Quantity);

        var rewardItemB = avatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "REWARD_ITEM_B");
        Assert.NotNull(rewardItemB);
        Assert.Equal(2, rewardItemB.Quantity);
    }

    #endregion

    #region Reputation & Achievement Reward Tests

    [Fact]
    public async Task CompleteQuest_WithReputationReward_CommitsReputationChangedTransaction()
    {
        // Arrange
        var avatar = CreateTestAvatar();
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "GUILD_ERRAND",
            QuestGiverRef = "GUILD_CLERK",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Simulate collecting the supply crate
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var lootTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LootAwarded,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                ["Consumables"] = "SUPPLY_CRATE:1"
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { lootTx }, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { lootTx.TransactionId }, CancellationToken.None);

        // Act: Advance the final stage (auto-completes the quest, distributing rewards)
        var advanceCommand = new AdvanceQuestStageCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "GUILD_ERRAND",
            Avatar = avatar
        };
        var result = await _mediator.Send(advanceCommand);

        // Assert
        Assert.True(result.Successful, result.ErrorMessage);

        // The reward must exist as a committed ReputationChanged transaction on the instance
        instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var reputationTx = instance.GetCommittedTransactions()
            .SingleOrDefault(t => t.Type == SagaTransactionType.ReputationChanged);
        Assert.NotNull(reputationTx);
        Assert.Equal("MERCHANT_GUILD", reputationTx.Data["FactionRef"]);
        Assert.Equal("250", reputationTx.Data["Amount"]);

        // Replayed saga state sees the reputation
        var stateMachine = new SagaStateMachine(
            _world.SagaArcLookup["TEST_SAGA"],
            _world.SagaTriggersLookup["TEST_SAGA"],
            _world);
        var state = stateMachine.ReplayToNow(instance);
        Assert.Equal(250, state.FactionReputation["MERCHANT_GUILD"]);

        // Cross-arc progress projection sees the reputation
        var progressRepository = _serviceProvider.GetRequiredService<IAvatarProgressRepository>();
        Assert.Equal(250, progressRepository.GetFactionReputation(avatar.Id, "MERCHANT_GUILD"));
    }

    [Fact]
    public async Task CompleteQuest_WithAchievementReward_UnlocksOnAvatarLedger()
    {
        // Arrange
        var avatar = CreateTestAvatar();
        var acceptCommand = new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "PROVE_WORTH",
            QuestGiverRef = "GUILD_MASTER",
            Avatar = avatar
        };
        await _mediator.Send(acceptCommand);

        // Simulate collecting the guild seal
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_SAGA", CancellationToken.None);
        var lootTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LootAwarded,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
                ["Consumables"] = "GUILD_SEAL:1"
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { lootTx }, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { lootTx.TransactionId }, CancellationToken.None);

        // Act: Advance the final stage (auto-completes the quest, distributing rewards)
        var advanceCommand = new AdvanceQuestStageCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "TEST_SAGA",
            QuestRef = "PROVE_WORTH",
            Avatar = avatar
        };
        var result = await _mediator.Send(advanceCommand);

        // Assert: the achievement is unlocked on the avatar's ledger (single unlock store,
        // same path as the dialogue UnlockAchievement action), exactly once
        Assert.True(result.Successful, result.ErrorMessage);
        Assert.NotNull(avatar.Achievements);
        var unlocked = Assert.Single(avatar.Achievements);
        Assert.Equal("ACH_GUILD_FRIEND", unlocked.AchievementRef);
    }

    #endregion
}
