using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Tests.Helpers;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Regression tests for dialogue-driven quest event dispatch (B4).
/// A node authored [CompleteQuest A, AcceptQuest B] where B requires A must
/// dispatch in authored order (the old reverse loop sent B first, whose
/// prerequisite check failed and was silently swallowed), and nested command
/// failures must surface in the result data instead of vanishing.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class DialogueQuestEventOrderingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

    public DialogueQuestEventOrderingTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithTurnInDialogue();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SelectDialogueChoiceCommand>();
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        var arcRepo = new ArcInstanceRepository(_database);
        var progressRepo = new AvatarProgressRepository(_database);
        arcRepo.SetAvatarProgressRepository(progressRepo);
        services.AddSingleton<IArcInstanceRepository>(arcRepo);
        services.AddSingleton<IAvatarProgressRepository>(progressRepo);
        services.AddSingleton<IArcReadModelRepository, InMemoryArcReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<IArcInstanceRepository>();
    }

    private World CreateWorldWithTurnInDialogue()
    {
        // Quest A: simple starter quest (completed via the dialogue action)
        var questA = new Quest
        {
            RefName = "QUEST_A",
            DisplayName = "Starter Quest",
            Stages = new QuestStages
            {
                StartStage = "GATHER",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "GATHER",
                        DisplayName = "Gather",
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
                                    DisplayName = "Collect a flower"
                                }
                            }
                        }
                    }
                }
            }
        };

        // Quest B: requires Quest A completion
        var questB = new Quest
        {
            RefName = "QUEST_B",
            DisplayName = "Follow-up Quest",
            Prerequisites = new[]
            {
                new QuestPrerequisite { QuestRef = "QUEST_A" }
            },
            Stages = new QuestStages
            {
                StartStage = "DELIVER",
                Stage = new[]
                {
                    new QuestStage
                    {
                        RefName = "DELIVER",
                        DisplayName = "Deliver",
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
                                    DisplayName = "Deliver a flower"
                                }
                            }
                        }
                    }
                }
            }
        };

        var questGiver = new Character
        {
            RefName = "QuestGiver",
            DisplayName = "Quest Giver",
            Interactable = new Interactable
            {
                DialogueTreeRef = "TurnInDialogue"
            }
        };

        // The "turnin" node authors [CompleteQuest A, AcceptQuest B] — B's
        // prerequisite is only satisfied if A completes FIRST
        var dialogueTree = new DialogueTree
        {
            RefName = "TurnInDialogue",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "You're back!" },
                    Choice = new[]
                    {
                        new DialogueChoice { Text = "I did it.", NextNodeId = "turnin" }
                    }
                },
                new DialogueNode
                {
                    NodeId = "turnin",
                    Text = new[] { "Well done. Here's your next task." },
                    Action = new[]
                    {
                        new DialogueAction { Type = DialogueActionType.CompleteQuest, RefName = "QUEST_A" },
                        new DialogueAction { Type = DialogueActionType.AcceptQuest, RefName = "QUEST_B" }
                    },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var arc = new Arc
        {
            RefName = "QUEST_ARC",
            DisplayName = "Quest Arc",
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
                    Characters = new[] { questGiver },
                    DialogueTrees = new[] { dialogueTree },
                    Quests = new[] { questA, questB }
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.CharactersLookup[questGiver.RefName] = questGiver;
        world.DialogueTreesLookup[dialogueTree.RefName] = dialogueTree;
        world.QuestsLookup[questA.RefName] = questA;
        world.QuestsLookup[questB.RefName] = questB;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();

        return world;
    }

    private async Task<Guid> SpawnQuestGiver(Guid avatarId, string arcRef)
    {
        var characterInstanceId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        var spawnTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.CharacterSpawned,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "QuestGiver",
                ["CharacterInstanceId"] = characterInstanceId.ToString()
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { spawnTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { spawnTx.TransactionId });

        return characterInstanceId;
    }

    private static AvatarEntity CreateTestAvatar()
    {
        return new AvatarEntity
        {
            Id = Guid.NewGuid(),
            Stats = new CharacterStats
            {
                Level = 1,
                Credits = 100,
                Health = 100,
                Stamina = 100,
                Mana = 100
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
            }
        };
    }

    [Fact]
    public async Task SelectDialogueChoice_CompleteThenAcceptNode_DispatchesInAuthoredOrder()
    {
        // Arrange: Quest A is active; NPC spawned; dialogue session started
        var avatar = CreateTestAvatar();

        var acceptA = await _mediator.Send(new AcceptQuestCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "QUEST_ARC",
            QuestRef = "QUEST_A",
            QuestGiverRef = "QuestGiver",
            Avatar = avatar
        });
        Assert.True(acceptA.Successful, acceptA.ErrorMessage);

        var characterInstanceId = await SpawnQuestGiver(avatar.Id, "QUEST_ARC");

        var startResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "QUEST_ARC",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, startResult.ErrorMessage);

        // Act: select the choice leading to the [CompleteQuest A, AcceptQuest B] node
        var result = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "QUEST_ARC",
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "turnin",
            Avatar = avatar
        });

        // Assert: dialogue advanced and no quest event failed
        Assert.True(result.Successful, result.ErrorMessage);
        Assert.False(result.Data.ContainsKey(TransactionDataKeys.QuestEventErrors),
            result.Data.TryGetValue(TransactionDataKeys.QuestEventErrors, out var errs)
                ? string.Join("; ", (List<string>)errs)
                : null);

        // Quest A completed, and Quest B — whose prerequisite is A — was accepted
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "QUEST_ARC");
        var committed = instance.GetCommittedTransactions();

        var completedA = committed.FirstOrDefault(t =>
            t.Type == ArcTransactionType.QuestCompleted &&
            t.GetData<string>(TransactionDataKeys.QuestRef) == "QUEST_A");
        Assert.NotNull(completedA);

        var acceptedB = committed.FirstOrDefault(t =>
            t.Type == ArcTransactionType.QuestAccepted &&
            t.GetData<string>(TransactionDataKeys.QuestRef) == "QUEST_B");
        Assert.NotNull(acceptedB);

        // A's completion committed before B's acceptance (authored order)
        Assert.True(completedA.SequenceNumber < acceptedB.SequenceNumber);
    }

    [Fact]
    public async Task SelectDialogueChoice_NestedQuestCommandFails_FailureSurfacedInResultData()
    {
        // Arrange: Quest A was never accepted, so BOTH nested commands must fail
        // (CompleteQuest A: not accepted; AcceptQuest B: prerequisite unmet)
        var avatar = CreateTestAvatar();
        var characterInstanceId = await SpawnQuestGiver(avatar.Id, "QUEST_ARC");

        var startResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "QUEST_ARC",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, startResult.ErrorMessage);

        // Act
        var result = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = "QUEST_ARC",
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "turnin",
            Avatar = avatar
        });

        // Assert: the dialogue advance itself still succeeds, but the swallowed
        // failures are now surfaced to the caller
        Assert.True(result.Successful, result.ErrorMessage);
        Assert.True(result.Data.ContainsKey(TransactionDataKeys.QuestEventErrors));

        var errors = Assert.IsType<List<string>>(result.Data[TransactionDataKeys.QuestEventErrors]);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("CompleteQuest 'QUEST_A'"));
        Assert.Contains(errors, e => e.Contains("AcceptQuest 'QUEST_B'"));
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
