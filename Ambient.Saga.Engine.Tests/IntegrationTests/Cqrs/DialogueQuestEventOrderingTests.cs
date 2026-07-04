using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Tests.Helpers;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

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
    private readonly ISagaInstanceRepository _repository;

    public DialogueQuestEventOrderingTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithTurnInDialogue();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SelectDialogueChoiceCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        var sagaRepo = new SagaInstanceRepository(_database);
        var progressRepo = new AvatarProgressRepository(_database);
        sagaRepo.SetAvatarProgressRepository(progressRepo);
        services.AddSingleton<ISagaInstanceRepository>(sagaRepo);
        services.AddSingleton<IAvatarProgressRepository>(progressRepo);
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
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

        var sagaArc = new SagaArc
        {
            RefName = "QUEST_SAGA",
            DisplayName = "Quest Saga",
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
                    Characters = new[] { questGiver },
                    DialogueTrees = new[] { dialogueTree },
                    Quests = new[] { questA, questB }
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.CharactersLookup[questGiver.RefName] = questGiver;
        world.DialogueTreesLookup[dialogueTree.RefName] = dialogueTree;
        world.QuestsLookup[questA.RefName] = questA;
        world.QuestsLookup[questB.RefName] = questB;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private async Task<Guid> SpawnQuestGiver(Guid avatarId, string sagaRef)
    {
        var characterInstanceId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        var spawnTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "QuestGiver",
                ["CharacterInstanceId"] = characterInstanceId.ToString()
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { spawnTx });
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
            SagaArcRef = "QUEST_SAGA",
            QuestRef = "QUEST_A",
            QuestGiverRef = "QuestGiver",
            Avatar = avatar
        });
        Assert.True(acceptA.Successful, acceptA.ErrorMessage);

        var characterInstanceId = await SpawnQuestGiver(avatar.Id, "QUEST_SAGA");

        var startResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "QUEST_SAGA",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, startResult.ErrorMessage);

        // Act: select the choice leading to the [CompleteQuest A, AcceptQuest B] node
        var result = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "QUEST_SAGA",
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
        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, "QUEST_SAGA");
        var committed = instance.GetCommittedTransactions();

        var completedA = committed.FirstOrDefault(t =>
            t.Type == SagaTransactionType.QuestCompleted &&
            t.GetData<string>(TransactionDataKeys.QuestRef) == "QUEST_A");
        Assert.NotNull(completedA);

        var acceptedB = committed.FirstOrDefault(t =>
            t.Type == SagaTransactionType.QuestAccepted &&
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
        var characterInstanceId = await SpawnQuestGiver(avatar.Id, "QUEST_SAGA");

        var startResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "QUEST_SAGA",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, startResult.ErrorMessage);

        // Act
        var result = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            SagaArcRef = "QUEST_SAGA",
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
