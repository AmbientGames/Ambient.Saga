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

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for Quest Prerequisites system.
/// Tests ALL prerequisite types defined in Quest.xsd:
/// - QuestRef (previous quest completion)
/// - MinimumLevel
/// - RequiredItemRef
/// - RequiredAchievementRef (Tier 2)
/// - FactionRef + RequiredReputationLevel (Tier 2)
/// </summary>
[Collection("Sequential CQRS Tests")]
public class QuestPrerequisitesTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;
    private readonly IGameAvatarRepository _avatarRepository;

    public QuestPrerequisitesTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorldWithQuests();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AcceptQuestCommand>();
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        var arcInstanceRepo = new ArcInstanceRepository(_database);
        var avatarProgressRepo = new AvatarProgressRepository(_database);
        arcInstanceRepo.SetAvatarProgressRepository(avatarProgressRepo);
        services.AddSingleton<IArcInstanceRepository>(arcInstanceRepo);
        services.AddSingleton<IAvatarProgressRepository>(avatarProgressRepo);
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

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    private World CreateTestWorldWithQuests()
    {
        // Quest A: No prerequisites (starter quest)
        var questA = new Quest
        {
            RefName = "QUEST_A",
            DisplayName = "Starter Quest",
            Description = "A simple starter quest",
            Stages = new QuestStages
            {
                StartStage = "GATHER",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "GATHER",
                        DisplayName = "Gather Items",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "OBJ_1",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "FLOWER",
                                    Threshold = 1,
                                    DisplayName = "Collect 1 flower"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest B: Requires Quest A completion
        var questB = new Quest
        {
            RefName = "QUEST_B",
            DisplayName = "Follow-up Quest",
            Description = "Requires completing Quest A first",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    QuestRef = "QUEST_A"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "DELIVER",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "DELIVER",
                        DisplayName = "Deliver Flowers",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "OBJ_2",
                                    Type = QuestObjectiveType.ItemDelivered,
                                    ItemRef = "FLOWER",
                                    Threshold = 1,
                                    DisplayName = "Deliver 1 flower"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest C: Requires Level 10
        var questC = new Quest
        {
            RefName = "DRAGON_HUNT",
            DisplayName = "Dragon Hunt",
            Description = "Slay a dragon (requires level 10)",
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
                StartStage = "SLAY",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "SLAY",
                        DisplayName = "Slay Dragon",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "OBJ_3",
                                    Type = QuestObjectiveType.CharacterDefeated,
                                    CharacterRef = "DRAGON",
                                    Threshold = 1,
                                    DisplayName = "Defeat 1 dragon"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest D: Requires Ancient Key item
        var questD = new Quest
        {
            RefName = "SECRET_VAULT",
            DisplayName = "Open Secret Vault",
            Description = "Requires Ancient Key",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    RequiredItemRef = "ANCIENT_KEY"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "UNLOCK",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "UNLOCK",
                        DisplayName = "Unlock Vault",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "OBJ_4",
                                    Type = QuestObjectiveType.TriggerActivated,
                                    TriggerRef = "VAULT_OPENED",
                                    Threshold = 1,
                                    DisplayName = "Open the vault"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest E: Multiple prerequisites (Quest A + Level 5 + Item)
        var questE = new Quest
        {
            RefName = "EPIC_QUEST",
            DisplayName = "Epic Adventure",
            Description = "Requires Quest A, Level 5, and Guild Token",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    QuestRef = "QUEST_A",
                    MinimumLevel = 5,
                    MinimumLevelSpecified = true,
                    RequiredItemRef = "GUILD_TOKEN"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "BEGIN",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "BEGIN",
                        DisplayName = "Begin Epic Quest",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "OBJ_5",
                                    Type = QuestObjectiveType.ArcDiscovered,
                                    ArcRef = "EPIC_ARC",
                                    Threshold = 1,
                                    DisplayName = "Discover Epic Arc location"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest F: Requires achievement
        var questF = new Quest
        {
            RefName = "MASTER_QUEST",
            DisplayName = "Master's Challenge",
            Description = "Requires Dragon Slayer achievement",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    RequiredAchievementRef = "DRAGON_SLAYER"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "CHALLENGE",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "CHALLENGE",
                        DisplayName = "Master's Challenge",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "MASTER_OBJ",
                                    Type = QuestObjectiveType.CharacterDefeated,
                                    CharacterRef = "MASTER_BOSS",
                                    Threshold = 1,
                                    DisplayName = "Defeat the Master"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest G: Requires faction reputation
        var questG = new Quest
        {
            RefName = "GUILD_ELITE_QUEST",
            DisplayName = "Elite Guild Mission",
            Description = "Requires Honored reputation with Adventurer's Guild",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    FactionRef = "ADVENTURERS_GUILD",
                    RequiredReputationLevel = "Honored"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "ELITE_MISSION",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "ELITE_MISSION",
                        DisplayName = "Elite Mission",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "ELITE_OBJ",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "RARE_ARTIFACT",
                                    Threshold = 1,
                                    DisplayName = "Retrieve the artifact"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest H: Requires reputation with a faction whose StartingReputation
        // already meets the bar (tests the baseline is included in the check)
        var questH = new Quest
        {
            RefName = "MINERS_QUEST",
            DisplayName = "Miners' Errand",
            Description = "Requires Honored reputation with the Miners' Guild",
            Prerequisites = new[]
            {
                new QuestPrerequisite
                {
                    FactionRef = "MINERS_GUILD",
                    RequiredReputationLevel = "Honored"
                }
            },
            Stages = new QuestStages
            {
                StartStage = "DIG",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "DIG",
                        DisplayName = "Dig",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "DIG_OBJ",
                                    Type = QuestObjectiveType.ItemCollected,
                                    ItemRef = "ORE",
                                    Threshold = 1,
                                    DisplayName = "Collect ore"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Achievement for testing
        var dragonSlayerAchievement = new Achievement
        {
            RefName = "DRAGON_SLAYER",
            DisplayName = "Dragon Slayer"
        };

        // Faction for testing
        var adventurersGuild = new Faction
        {
            RefName = "ADVENTURERS_GUILD",
            DisplayName = "Adventurer's Guild"
        };

        // Faction that starts out Honored (9000+) without any earned reputation
        var minersGuild = new Faction
        {
            RefName = "MINERS_GUILD",
            DisplayName = "Miners' Guild",
            StartingReputation = 10000
        };

        var arc = new Arc
        {
            RefName = "TEST_ARC",
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 135.0
        };

        // Second arc for cross-arc prerequisite tests (a quest completed in
        // OTHER_ARC must satisfy a prerequisite when accepting in TEST_ARC)
        var otherArc = new Arc
        {
            RefName = "OTHER_ARC",
            DisplayName = "Other Arc",
            Latitude = 36.0,
            Longitude = 136.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Saga = new[] { arc, otherArc },
                    Quests = new[] { questA, questB, questC, questD, questE, questF, questG, questH },
                    Achievements = new[] { dragonSlayerAchievement },
                    Factions = new[] { adventurersGuild, minersGuild }
                }
            }
        };

        // Populate lookup dictionaries
        world.ArcLookup[arc.RefName] = arc;
        world.ArcLookup[otherArc.RefName] = otherArc;
        foreach (var quest in world.WorldTemplate.Gameplay.Quests)
        {
            world.QuestsLookup[quest.RefName] = quest;
        }
        world.AchievementsLookup[dragonSlayerAchievement.RefName] = dragonSlayerAchievement;
        world.FactionsLookup[adventurersGuild.RefName] = adventurersGuild;
        world.FactionsLookup[minersGuild.RefName] = minersGuild;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();
        world.ArcTriggersLookup[otherArc.RefName] = new List<ArcTrigger>();

        return world;
    }

    private AvatarEntity CreateTestAvatar(int level = 1, int credits = 0, int experience = 0, Guid? avatarId = null)
    {
        var avatar = new AvatarEntity
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
        return avatar;
    }

    #region Tier 1: Quest Prerequisite Tests

    [Fact]
    public async Task AcceptQuest_WithQuestPrerequisite_FailsWhenNotCompleted()
    {
        // GIVEN: Quest B requires Quest A completion
        var avatar = CreateTestAvatar();

        // WHEN: Try to accept Quest B without completing Quest A
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "QUEST_B",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        // THEN: Should fail with prerequisite error mentioning the starter quest
        Assert.False(result.Successful);
        Assert.Contains("Starter Quest", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithQuestPrerequisite_SucceedsWhenCompleted()
    {
        // GIVEN: Quest A is already completed
        var avatar = CreateTestAvatar();

        // Accept and complete Quest A
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "QUEST_A",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);

        // Manually mark Quest A as completed by adding transaction
        var completionTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.QuestCompleted,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string> { ["QuestRef"] = "QUEST_A" }
        };
        var transactions = new List<ArcTransaction> { completionTx };
        await _repository.AddTransactionsAsync(instance.InstanceId, transactions, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList(), CancellationToken.None);

        // WHEN: Try to accept Quest B
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "QUEST_B",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        // THEN: Should succeed
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithLevelPrerequisite_FailsWhenTooLow()
    {
        // GIVEN: Avatar level 5, quest requires level 10
        var avatar = CreateTestAvatar(level: 5);

        // WHEN: Try to accept Dragon Hunt quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "DRAGON_HUNT",
            QuestGiverRef = "DRAGON_HUNTER",
            Avatar = avatar
        });

        // THEN: Should fail with level requirement error
        Assert.False(result.Successful);
        Assert.Contains("level 10", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptQuest_WithLevelPrerequisite_SucceedsWhenMet()
    {
        // GIVEN: Avatar level 10, quest requires level 10
        var avatar = CreateTestAvatar(level: 10);

        // WHEN: Try to accept Dragon Hunt quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "DRAGON_HUNT",
            QuestGiverRef = "DRAGON_HUNTER",
            Avatar = avatar
        });

        // THEN: Should succeed
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithItemPrerequisite_FailsWhenMissing()
    {
        // GIVEN: Avatar without Ancient Key
        var avatar = CreateTestAvatar();

        // WHEN: Try to accept Secret Vault quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "SECRET_VAULT",
            QuestGiverRef = "VAULT_KEEPER",
            Avatar = avatar
        });

        // THEN: Should fail with item requirement error
        Assert.False(result.Successful);
        Assert.Contains("ANCIENT_KEY", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithItemPrerequisite_SucceedsWhenInEquipment()
    {
        // GIVEN: Avatar with Ancient Key in equipment
        var avatar = CreateTestAvatar();
        avatar.Capabilities.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "ANCIENT_KEY", Condition = 1.0f }
        };

        // WHEN: Try to accept Secret Vault quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "SECRET_VAULT",
            QuestGiverRef = "VAULT_KEEPER",
            Avatar = avatar
        });

        // THEN: Should succeed
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithMultiplePrerequisites_ValidatesAll()
    {
        // GIVEN: Quest requires Quest A + Level 5 + Guild Token
        var avatar = CreateTestAvatar(level: 5);

        // Has level 5 ?
        // Missing Quest A completion ?
        // Missing Guild Token ?

        // WHEN: Try to accept Epic Quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EPIC_QUEST",
            QuestGiverRef = "GUILD_MASTER",
            Avatar = avatar
        });

        // THEN: Should fail (first failure wins - Quest A not completed)
        Assert.False(result.Successful);
        Assert.Contains("Starter Quest", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithMultiplePrerequisites_SucceedsWhenAllMet()
    {
        // GIVEN: Quest requires Quest A + Level 5 + Guild Token
        var avatar = CreateTestAvatar(level: 5);

        // Complete Quest A first
        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "QUEST_A",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);
        var completionTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.QuestCompleted,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string> { ["QuestRef"] = "QUEST_A" }
        };
        await _repository.AddAndCommitTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { completionTx }, CancellationToken.None);

        // Add Guild Token via arc transaction — use AddAndCommitTransactionsAsync so it projects into the progress table
        var tokenTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.QuestTokenAwarded,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string> { ["QuestTokenRef"] = "GUILD_TOKEN", ["Reason"] = "Test setup" }
        };
        await _repository.AddAndCommitTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { tokenTx }, CancellationToken.None);

        // WHEN: Try to accept Epic Quest (now all prerequisites met)
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "EPIC_QUEST",
            QuestGiverRef = "GUILD_MASTER",
            Avatar = avatar
        });

        // THEN: Should succeed
        Assert.True(result.Successful, result.ErrorMessage);
    }

    #endregion

    #region Tier 2: Achievement Prerequisite Tests

    [Fact]
    public async Task AcceptQuest_WithAchievementPrerequisite_FailsWhenNotUnlocked()
    {
        // GIVEN: Quest requires Dragon Slayer achievement, avatar hasn't unlocked it
        var avatar = CreateTestAvatar();

        // WHEN: Try to accept Master Quest without achievement
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "MASTER_QUEST",
            QuestGiverRef = "MASTER",
            Avatar = avatar
        });

        // THEN: Should fail with achievement requirement error
        Assert.False(result.Successful);
        Assert.Contains("achievement", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dragon Slayer", result.ErrorMessage);
    }

    // Note: Testing success case requires wiring up achievement unlock tracking
    // which currently requires IWorldStateRepository integration. The CheckPrerequisites
    // method now properly checks unlockedAchievements when provided.

    #endregion

    #region Tier 2: Faction Reputation Prerequisite Tests

    [Fact]
    public async Task AcceptQuest_WithReputationPrerequisite_FailsWhenNotMet()
    {
        // GIVEN: Quest requires Honored reputation with Adventurer's Guild
        // Avatar has no reputation (defaults to Neutral)
        var avatar = CreateTestAvatar();

        // WHEN: Try to accept Elite Guild Quest without reputation
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "GUILD_ELITE_QUEST",
            QuestGiverRef = "GUILD_MASTER",
            Avatar = avatar
        });

        // THEN: Should fail with reputation requirement error
        Assert.False(result.Successful);
        Assert.Contains("reputation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Honored", result.ErrorMessage);
        Assert.Contains("Adventurer's Guild", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithReputationPrerequisite_SucceedsWhenMet()
    {
        // GIVEN: Quest requires Honored reputation (9000+ points)
        var avatar = CreateTestAvatar();

        // First create an instance and add reputation transactions
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);

        // Add reputation to reach Honored (9000+ points)
        var reputationTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.ReputationChanged,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FactionRef"] = "ADVENTURERS_GUILD",
                ["Amount"] = "15000" // Well above Honored threshold (9000)
            }
        };
        var transactions = new List<ArcTransaction> { reputationTx };
        await _repository.AddTransactionsAsync(instance.InstanceId, transactions, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList(), CancellationToken.None);

        // WHEN: Try to accept Elite Guild Quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "GUILD_ELITE_QUEST",
            QuestGiverRef = "GUILD_MASTER",
            Avatar = avatar
        });

        // THEN: Should succeed
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithReputationPrerequisite_FailsWhenBelowThreshold()
    {
        // GIVEN: Quest requires Honored reputation (9000+ points)
        var avatar = CreateTestAvatar();

        // First create an instance and add some reputation (but not enough)
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "TEST_ARC", CancellationToken.None);

        // Add reputation to Friendly (3000-9000 points) - not enough for Honored
        var reputationTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.ReputationChanged,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FactionRef"] = "ADVENTURERS_GUILD",
                ["Amount"] = "5000" // Only Friendly, not Honored
            }
        };
        var transactions = new List<ArcTransaction> { reputationTx };
        await _repository.AddTransactionsAsync(instance.InstanceId, transactions, CancellationToken.None);
        await _repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList(), CancellationToken.None);

        // WHEN: Try to accept Elite Guild Quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "GUILD_ELITE_QUEST",
            QuestGiverRef = "GUILD_MASTER",
            Avatar = avatar
        });

        // THEN: Should fail with reputation requirement error
        Assert.False(result.Successful);
        Assert.Contains("Honored", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithReputationPrerequisite_StartingReputationBaselineCounts()
    {
        // GIVEN: MINERS_QUEST requires Honored with MINERS_GUILD, whose
        // StartingReputation (10000) already clears the Honored threshold —
        // no earned reputation transactions exist anywhere
        var avatar = CreateTestAvatar();

        // WHEN: Try to accept the quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "MINERS_QUEST",
            QuestGiverRef = "FOREMAN",
            Avatar = avatar
        });

        // THEN: Should succeed off the faction baseline alone
        Assert.True(result.Successful, result.ErrorMessage);
    }

    #endregion

    #region Cross-Arc Prerequisite Tests (B1)

    [Fact]
    public async Task AcceptQuest_QuestPrerequisiteCompletedInOtherArc_Succeeds()
    {
        // GIVEN: Quest A accepted AND completed in OTHER_ARC (a different arc
        // than the one offering Quest B)
        var avatar = CreateTestAvatar();

        await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "OTHER_ARC",
            QuestRef = "QUEST_A",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        var otherInstance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "OTHER_ARC", CancellationToken.None);
        var completionTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.QuestCompleted,
            AvatarId = avatar.Id.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string> { ["QuestRef"] = "QUEST_A" }
        };
        // AddAndCommit so the completion projects into the cross-arc progress table
        await _repository.AddAndCommitTransactionsAsync(otherInstance.InstanceId, new List<ArcTransaction> { completionTx }, CancellationToken.None);

        // WHEN: Try to accept Quest B in TEST_ARC, whose arc-local log never saw Quest A
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "QUEST_B",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        // THEN: The cross-arc completion satisfies the prerequisite
        Assert.True(result.Successful, result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptQuest_WithAchievementPrerequisite_SucceedsWhenOnAvatarLedger()
    {
        // GIVEN: The avatar's persisted Achievements list (the unlock ledger)
        // contains the required achievement
        var avatar = CreateTestAvatar();
        avatar.Achievements = new[]
        {
            new AchievementEntry
            {
                AchievementRef = "DRAGON_SLAYER",
                UnlockedDate = DateTime.UtcNow.ToString("O"),
                ProgressPercentage = 1.0f
            }
        };

        // WHEN: Try to accept the Master Quest
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "TEST_ARC",
            QuestRef = "MASTER_QUEST",
            QuestGiverRef = "MASTER",
            Avatar = avatar
        });

        // THEN: Should succeed (achievements used to be passed as null, always failing)
        Assert.True(result.Successful, result.ErrorMessage);
    }

    #endregion

    #region Dev Arc Ref Tests (B10)

    [Fact]
    public async Task AcceptQuest_WithDevArcRef_Succeeds()
    {
        // GIVEN: A dev-spawned arc instance ref ("RealArcRef__DEV__uniqueid");
        // template/trigger lookups must use the stripped ref, instance ops the full ref
        var avatar = CreateTestAvatar();
        var devArcRef = "TEST_ARC__DEV__abc123";

        // WHEN: Accept a quest offered by the dev-spawned NPC's arc
        var result = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = devArcRef,
            QuestRef = "QUEST_A",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });

        // THEN: Should succeed, with the QuestAccepted on the dev instance
        Assert.True(result.Successful, result.ErrorMessage);

        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, devArcRef, CancellationToken.None);
        var acceptTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == ArcTransactionType.QuestAccepted);
        Assert.NotNull(acceptTx);
        Assert.Equal("QUEST_A", acceptTx.Data["QuestRef"]);
    }

    #endregion
}
