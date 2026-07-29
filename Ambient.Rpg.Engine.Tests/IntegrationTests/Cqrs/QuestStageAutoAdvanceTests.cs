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
/// Regression tests for round-3 audit finding B2: the quest stage/objective machinery
/// had NO production driver — nothing ever sent AdvanceQuestStageCommand, so
/// CurrentStage never moved and quest progress sat at 0% forever.
///
/// QuestStageProgressionBehavior is the driver: after any arc command, stages whose
/// objectives are complete auto-advance, and the final stage cascades into quest
/// completion via the existing AdvanceQuestStage → CompleteQuest chain.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class QuestStageAutoAdvanceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

    public QuestStageAutoAdvanceTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AcceptQuestCommand>();
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
            cfg.AddOpenBehavior(typeof(QuestStageProgressionBehavior<,>));
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
    }

    [Fact]
    public async Task CompletedObjectives_AutoAdvanceStages_ButCompletionWaitsForTheQuestGiver()
    {
        var avatar = CreateTestAvatar();

        // Accept the quest FIRST (objective counting is scoped to the acceptance)
        var accept = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            QuestRef = "AUTO_QUEST",
            QuestGiverRef = "QuestGiver",
            Avatar = avatar
        });
        Assert.True(accept.Successful, $"Accept failed: {accept.ErrorMessage}");

        // Approach the arc: inside TARGET_TRIGGER's 100m ring but outside
        // RETURN_TRIGGER's 30m ring — only the TRAVEL objective completes
        // (this also spawns the quest giver, who rides on TARGET_TRIGGER)
        var approach = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            Latitude = 35.0005, // ≈55m from the arc center
            Longitude = 139.0,
            Avatar = avatar
        });
        Assert.True(approach.Successful, $"Approach failed: {approach.ErrorMessage}");

        var committed = (await _repository.GetOrCreateInstanceAsync(avatar.Id, "AUTO_ARC"))
            .GetCommittedTransactions();

        // The intermediate stage advanced without any client sending
        // AdvanceQuestStageCommand...
        Assert.Contains(committed, t =>
            t.Type == ArcTransactionType.QuestStageAdvanced &&
            t.Data[TransactionDataKeys.QuestRef] == "AUTO_QUEST" &&
            t.Data[TransactionDataKeys.FromStage] == "TRAVEL");

        // Walk to the arc center — RETURN_TRIGGER activates, completing the
        // FINAL stage's objective
        var reachCenter = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });
        Assert.True(reachCenter.Successful, $"Center move failed: {reachCenter.ErrorMessage}");

        committed = (await _repository.GetOrCreateInstanceAsync(avatar.Id, "AUTO_ARC"))
            .GetCommittedTransactions();

        // ...but the quest must NOT complete in the field — in this game things
        // happen through characters: completion belongs to the quest giver
        Assert.DoesNotContain(committed, t =>
            t.Type == ArcTransactionType.QuestCompleted &&
            t.Data[TransactionDataKeys.QuestRef] == "AUTO_QUEST");

        // Talk to the quest giver — the turn-in happens automatically
        var giverInstanceId = Guid.Parse(committed
            .First(t => t.Type == ArcTransactionType.CharacterSpawned &&
                        t.Data[TransactionDataKeys.CharacterRef] == "QuestGiver")
            .Data[TransactionDataKeys.CharacterInstanceId]);

        var talk = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            CharacterInstanceId = giverInstanceId,
            Avatar = avatar
        });
        Assert.True(talk.Successful, $"Dialogue failed: {talk.ErrorMessage}");

        var finalCommitted = (await _repository.GetOrCreateInstanceAsync(avatar.Id, "AUTO_ARC"))
            .GetCommittedTransactions();
        Assert.Contains(finalCommitted, t =>
            t.Type == ArcTransactionType.QuestCompleted &&
            t.Data[TransactionDataKeys.QuestRef] == "AUTO_QUEST");
    }

    [Fact]
    public async Task IncompleteStage_DoesNotAdvance()
    {
        var avatar = CreateTestAvatar();

        // Accept the two-objective quest, then trigger only ONE of its objectives
        var accept = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            QuestRef = "TWO_OBJECTIVE_QUEST",
            QuestGiverRef = "NPC",
            Avatar = avatar
        });
        Assert.True(accept.Successful, $"Accept failed: {accept.ErrorMessage}");

        var move = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });
        Assert.True(move.Successful, $"Position update failed: {move.ErrorMessage}");

        var committed = (await _repository.GetOrCreateInstanceAsync(avatar.Id, "AUTO_ARC"))
            .GetCommittedTransactions();

        // The stage also requires a CharacterDefeated — it must NOT have advanced
        Assert.DoesNotContain(committed, t =>
            t.Type == ArcTransactionType.QuestStageAdvanced &&
            t.Data[TransactionDataKeys.QuestRef] == "TWO_OBJECTIVE_QUEST");
        Assert.DoesNotContain(committed, t =>
            t.Type == ArcTransactionType.QuestCompleted &&
            t.Data[TransactionDataKeys.QuestRef] == "TWO_OBJECTIVE_QUEST");
    }

    [Fact]
    public async Task CompletedBranchObjective_AutoDerivesBranchChoice_AndAdvancesStage()
    {
        // No UI or dialogue action sends ChooseQuestBranchCommand — the player's
        // decision IS completing one branch's objective. The progression behavior
        // must derive the choice and advance the stage, or every branch stage
        // soft-locks in production.
        var avatar = CreateTestAvatar();

        var accept = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            QuestRef = "BRANCH_QUEST",
            QuestGiverRef = "QuestGiver",
            Avatar = avatar
        });
        Assert.True(accept.Successful, $"Accept failed: {accept.ErrorMessage}");

        // Walk into TARGET_TRIGGER's 100m ring (outside RETURN_TRIGGER's 30m ring):
        // completes the SCENIC branch's objective and nothing else
        var walk = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            Latitude = 35.0005,
            Longitude = 139.0,
            Avatar = avatar
        });
        Assert.True(walk.Successful, $"Walk failed: {walk.ErrorMessage}");

        var committed = (await _repository.GetOrCreateInstanceAsync(avatar.Id, "AUTO_ARC"))
            .GetCommittedTransactions();

        // The choice was derived from the completed branch objective...
        var branchChoices = committed.Where(t =>
            t.Type == ArcTransactionType.QuestBranchChosen &&
            t.Data[TransactionDataKeys.QuestRef] == "BRANCH_QUEST").ToList();
        var chosen = Assert.Single(branchChoices);
        Assert.Equal("SCENIC", chosen.Data[TransactionDataKeys.BranchRef]);

        // ...and the stage advanced along the branch's route
        Assert.Contains(committed, t =>
            t.Type == ArcTransactionType.QuestStageAdvanced &&
            t.Data[TransactionDataKeys.QuestRef] == "BRANCH_QUEST" &&
            t.Data[TransactionDataKeys.FromStage] == "DECIDE" &&
            t.Data[TransactionDataKeys.NextStage] == "ARRIVE");
    }

    [Fact]
    public async Task BranchStage_NoBranchObjectiveComplete_DoesNotChoose()
    {
        // Commands that complete no branch objective must not pick a branch for
        // the player.
        var avatar = CreateTestAvatar();

        var accept = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            QuestRef = "BRANCH_QUEST",
            QuestGiverRef = "QuestGiver",
            Avatar = avatar
        });
        Assert.True(accept.Successful, $"Accept failed: {accept.ErrorMessage}");

        // ~1.1km out: successful command, no trigger activation
        var move = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "AUTO_ARC",
            Latitude = 35.01,
            Longitude = 139.0,
            Avatar = avatar
        });
        Assert.True(move.Successful, $"Move failed: {move.ErrorMessage}");

        var committed = (await _repository.GetOrCreateInstanceAsync(avatar.Id, "AUTO_ARC"))
            .GetCommittedTransactions();

        Assert.DoesNotContain(committed, t =>
            t.Type == ArcTransactionType.QuestBranchChosen &&
            t.Data[TransactionDataKeys.QuestRef] == "BRANCH_QUEST");
        Assert.DoesNotContain(committed, t =>
            t.Type == ArcTransactionType.QuestStageAdvanced &&
            t.Data[TransactionDataKeys.QuestRef] == "BRANCH_QUEST");
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Setup

    private World CreateTestWorld()
    {
        var autoQuest = new Quest
        {
            RefName = "AUTO_QUEST",
            DisplayName = "Reach the Waypoint",
            Description = "Travel to the marked locations and report back",
            Stages = new QuestStages
            {
                StartStage = "TRAVEL",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "TRAVEL",
                        DisplayName = "Travel to the waypoint",
                        NextStage = "RETURN",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "REACH_TARGET",
                                    Type = QuestObjectiveType.TriggerActivated,
                                    TriggerRef = "TARGET_TRIGGER",
                                    Threshold = 1,
                                    DisplayName = "Reach the target"
                                }
                            }
                        }
                    },
                    new QuestStage
                    {
                        RefName = "RETURN",
                        DisplayName = "Visit the second waypoint",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "REACH_RETURN",
                                    Type = QuestObjectiveType.TriggerActivated,
                                    TriggerRef = "RETURN_TRIGGER",
                                    Threshold = 1,
                                    DisplayName = "Reach the second waypoint"
                                }
                            }
                        }
                    }
                }
            }
        };

        var twoObjectiveQuest = new Quest
        {
            RefName = "TWO_OBJECTIVE_QUEST",
            DisplayName = "Travel and Slay",
            Description = "Reach the waypoint and defeat the beast",
            Stages = new QuestStages
            {
                StartStage = "BOTH",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "BOTH",
                        DisplayName = "Do both things",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "REACH_TARGET_2",
                                    Type = QuestObjectiveType.TriggerActivated,
                                    TriggerRef = "TARGET_TRIGGER",
                                    Threshold = 1,
                                    DisplayName = "Reach the target"
                                },
                                new QuestObjective
                                {
                                    RefName = "SLAY_BEAST",
                                    Type = QuestObjectiveType.CharacterDefeated,
                                    CharacterRef = "TheBeast",
                                    Threshold = 1,
                                    DisplayName = "Defeat the beast"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Branch stage: the player "chooses" by doing — completing one branch's
        // objective must auto-derive the QuestBranchChosen (no UI sends the command)
        var branchQuest = new Quest
        {
            RefName = "BRANCH_QUEST",
            DisplayName = "Choose Your Path",
            Description = "Take the scenic route or the direct route",
            Stages = new QuestStages
            {
                StartStage = "DECIDE",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "DECIDE",
                        DisplayName = "Pick a route",
                        Branches = new QuestStageBranches
                        {
                            Exclusive = true,
                            Branch = new[]
                            {
                                new QuestBranch
                                {
                                    RefName = "SCENIC",
                                    DisplayName = "Scenic Route",
                                    NextStage = "ARRIVE",
                                    Objective = new QuestObjective
                                    {
                                        RefName = "WALK_SCENIC",
                                        Type = QuestObjectiveType.TriggerActivated,
                                        TriggerRef = "TARGET_TRIGGER",
                                        Threshold = 1,
                                        DisplayName = "Take the scenic route"
                                    }
                                },
                                new QuestBranch
                                {
                                    RefName = "DIRECT",
                                    DisplayName = "Direct Route",
                                    NextStage = "ARRIVE",
                                    Objective = new QuestObjective
                                    {
                                        RefName = "WALK_DIRECT",
                                        Type = QuestObjectiveType.TriggerActivated,
                                        TriggerRef = "RETURN_TRIGGER",
                                        Threshold = 1,
                                        DisplayName = "Take the direct route"
                                    }
                                }
                            }
                        }
                    },
                    new QuestStage
                    {
                        RefName = "ARRIVE",
                        DisplayName = "Arrive at the destination",
                        Objectives = new QuestStageObjectives
                        {
                            Objective = new[]
                            {
                                new QuestObjective
                                {
                                    RefName = "REACH_END",
                                    Type = QuestObjectiveType.TriggerActivated,
                                    TriggerRef = "RETURN_TRIGGER",
                                    Threshold = 1,
                                    DisplayName = "Reach the destination"
                                }
                            }
                        }
                    }
                }
            }
        };

        var arc = new Arc
        {
            RefName = "AUTO_ARC",
            DisplayName = "Auto Advance Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var targetTrigger = new ArcTrigger
        {
            RefName = "TARGET_TRIGGER",
            EnterRadius = 100.0f,
            Spawn = new[] { new CharacterSpawn { CharacterRef = "QuestGiver" } }
        };

        var returnTrigger = new ArcTrigger
        {
            RefName = "RETURN_TRIGGER",
            EnterRadius = 30.0f // inner ring — reached after TARGET_TRIGGER's 100m ring
        };

        var questGiver = new Character
        {
            RefName = "QuestGiver",
            DisplayName = "The Quartermaster",
            Stats = new CharacterStats { Health = 1.0f },
            Interactable = new Interactable
            {
                DialogueTreeRef = "GIVER_TALK"
            }
        };

        var giverDialogue = new DialogueTree
        {
            RefName = "GIVER_TALK",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Back already? Good work out there." },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var world = new World
        {
            IsProcedural = true,
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "AutoAdvanceTestWorld",
                SpawnLatitude = 35.0,
                SpawnLongitude = 139.0,
                ProceduralSettings = new ProceduralSettings
                {
                    LatitudeDegreesToUnits = 111320.0,
                    LongitudeDegreesToUnits = 91300.0
                }
            },
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Saga = new[] { arc },
                    Quests = new[] { autoQuest, twoObjectiveQuest, branchQuest },
                    Characters = new[] { questGiver },
                    Equipment = Array.Empty<Equipment>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = new[] { giverDialogue },
                    Consumables = Array.Empty<Consumable>()
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { targetTrigger, returnTrigger };
        world.QuestsLookup[autoQuest.RefName] = autoQuest;
        world.QuestsLookup[twoObjectiveQuest.RefName] = twoObjectiveQuest;
        world.QuestsLookup[branchQuest.RefName] = branchQuest;
        world.CharactersLookup[questGiver.RefName] = questGiver;
        world.DialogueTreesLookup[giverDialogue.RefName] = giverDialogue;

        return world;
    }

    private AvatarEntity CreateTestAvatar()
    {
        var avatarId = Guid.NewGuid();
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            DisplayName = "Auto Advance Tester",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f
            },
            Capabilities = new ItemCollection()
        };
    }

    #endregion
}
