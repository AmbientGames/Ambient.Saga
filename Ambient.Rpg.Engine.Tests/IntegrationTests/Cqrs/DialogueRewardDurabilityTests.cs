using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Services;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using Ambient.Rpg.Engine.Tests.Helpers;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Avatar repository wrapper for testing the B6 persistence-failure compensation:
/// delegates to <see cref="FakeAvatarRepository"/> but can be armed to fail the
/// next save, simulating a crash/IO failure between ledger commit and avatar save.
/// </summary>
internal sealed class FailableAvatarRepository : IGameAvatarRepository
{
    private readonly FakeAvatarRepository _inner = new();

    public bool FailNextSave { get; set; }

    public Task<TAvatar?> LoadAvatarAsync<TAvatar>() where TAvatar : class
        => _inner.LoadAvatarAsync<TAvatar>();

    public Task SaveAvatarAsync<TAvatar>(TAvatar avatar) where TAvatar : class
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("Simulated avatar persistence failure");
        }
        return _inner.SaveAvatarAsync(avatar);
    }

    public Task DeleteAvatarsAsync() => _inner.DeleteAvatarsAsync();
}

/// <summary>
/// Integration tests for:
/// - Audit B6: dialogue rewards are persisted to the avatar repository in the same
///   pass that commits the DialogueNodeVisited ledger (and the ledger is reversed
///   when the avatar save fails, so the reward is never orphaned).
/// - NodeVisited / AvatarVisitCount dialogue conditions reading the committed
///   transaction log, so they work across conversations and survive reload.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class DialogueRewardDurabilityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;
    private readonly FailableAvatarRepository _avatarRepository;

    private const string ArcRef = "SAGE_ARC";
    private const string CharacterRef = "Sage";
    private const string TreeRef = "SageDialogue";

    public DialogueRewardDurabilityTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithSage();
        _avatarRepository = new FailableAvatarRepository();

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
        // Real AvatarUpdateService over the fake repository so PersistAvatarAsync is observable
        services.AddSingleton<IAvatarUpdateService>(new AvatarUpdateService(() => _avatarRepository, () => _world));
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<IArcInstanceRepository>();
    }

    private World CreateWorldWithSage()
    {
        var sage = new Character
        {
            RefName = CharacterRef,
            DisplayName = "Old Sage",
            Interactable = new Interactable
            {
                DialogueTreeRef = TreeRef
            }
        };

        var dialogueTree = new DialogueTree
        {
            RefName = TreeRef,
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Welcome, traveler." },
                    Choice = new[]
                    {
                        new DialogueChoice { Text = "Just passing through.", NextNodeId = "chat" },
                        new DialogueChoice { Text = "Any gift for me?", NextNodeId = "gift" },
                        new DialogueChoice { Text = "Tell me a secret.", NextNodeId = "secret" },
                        new DialogueChoice { Text = "Good to see you again, friend.", NextNodeId = "friend" }
                    }
                },
                // Plain node — no actions, so navigation here must not persist the avatar
                new DialogueNode
                {
                    NodeId = "chat",
                    Text = new[] { "Safe travels." },
                    Choice = Array.Empty<DialogueChoice>()
                },
                // First-visit reward node (terminal, so the session completes)
                new DialogueNode
                {
                    NodeId = "gift",
                    Text = new[] { "Take this potion." },
                    Action = new[]
                    {
                        new DialogueAction { Type = DialogueActionType.GiveConsumable, RefName = "HEALTH_POTION", Amount = 1 }
                    },
                    Choice = Array.Empty<DialogueChoice>()
                },
                // Gated on having visited the gift node in ANY conversation
                new DialogueNode
                {
                    NodeId = "secret",
                    Text = new[] { "You accepted my gift, so you may hear this..." },
                    Condition = new[]
                    {
                        new DialogueCondition
                        {
                            Type = DialogueConditionType.NodeVisited,
                            RefName = TreeRef,
                            Value = "gift"
                        }
                    },
                    Choice = Array.Empty<DialogueChoice>()
                },
                // Gated on this being at least the avatar's second conversation
                new DialogueNode
                {
                    NodeId = "friend",
                    Text = new[] { "Ah, a returning visitor!" },
                    Condition = new[]
                    {
                        new DialogueCondition
                        {
                            Type = DialogueConditionType.AvatarVisitCount,
                            RefName = TreeRef,
                            Operator = ComparisonOperator.GreaterThanOrEqual,
                            Value = "2"
                        }
                    },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var arc = new Arc
        {
            RefName = ArcRef,
            DisplayName = "Sage Arc",
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
                    Characters = new[] { sage },
                    DialogueTrees = new[] { dialogueTree }
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.CharactersLookup[sage.RefName] = sage;
        world.DialogueTreesLookup[dialogueTree.RefName] = dialogueTree;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();

        return world;
    }

    private async Task<Guid> SpawnSage(Guid avatarId)
    {
        var characterInstanceId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, ArcRef);

        var spawnTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.CharacterSpawned,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = CharacterRef,
                ["CharacterInstanceId"] = characterInstanceId.ToString()
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { spawnTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { spawnTx.TransactionId });

        return characterInstanceId;
    }

    private static AvatarEntity CreateTestAvatar(Guid? id = null)
    {
        return new AvatarEntity
        {
            Id = id ?? Guid.NewGuid(),
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

    private async Task StartConversation(AvatarEntity avatar, Guid characterInstanceId)
    {
        var startResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, startResult.ErrorMessage);
    }

    private async Task<List<string>> GetOfferedChoiceIds(AvatarEntity avatar, Guid characterInstanceId)
    {
        var state = await _mediator.Send(new GetDialogueStateQuery
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });
        return state.Choices.Select(c => c.ChoiceId).ToList();
    }

    // ===== B6: reward durability =====

    [Fact]
    public async Task DialogueRewardGrant_PersistsAvatarInSamePass()
    {
        var avatar = CreateTestAvatar();
        var characterInstanceId = await SpawnSage(avatar.Id);
        await StartConversation(avatar, characterInstanceId);

        var result = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "gift",
            Avatar = avatar
        });

        Assert.True(result.Successful, result.ErrorMessage);
        Assert.Equal(1, avatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "HEALTH_POTION")?.Quantity ?? 0);

        // The reward must be durable the moment the visit ledger committed —
        // not at the host's next periodic save (audit B6)
        var saved = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        Assert.NotNull(saved);
        Assert.Equal(avatar.Id, saved!.Id);
        Assert.Equal(1, saved.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "HEALTH_POTION")?.Quantity ?? 0);
    }

    [Fact]
    public async Task DialogueNavigationWithoutAward_DoesNotPersistAvatar()
    {
        var avatar = CreateTestAvatar();
        var characterInstanceId = await SpawnSage(avatar.Id);
        await StartConversation(avatar, characterInstanceId);

        var result = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "chat",
            Avatar = avatar
        });

        Assert.True(result.Successful, result.ErrorMessage);

        // No avatar-mutating action ran, so no gratuitous write
        var saved = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        Assert.Null(saved);
    }

    [Fact]
    public async Task DialogueRewardGrant_PersistFails_LedgerReversedAndRewardRegrantable()
    {
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var characterInstanceId = await SpawnSage(avatarId);
        await StartConversation(avatar, characterInstanceId);

        // Conversation 1: the avatar save fails after the ledger committed
        _avatarRepository.FailNextSave = true;
        var failedResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatarId,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "gift",
            Avatar = avatar
        });

        Assert.False(failedResult.Successful);
        Assert.Contains("avatar update failed", failedResult.ErrorMessage);

        // The visit records were reversed so the first-visit block cannot orphan the reward
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, ArcRef);
        var committed = instance.GetCommittedTransactions();
        var visitTxIds = committed
            .Where(t => t.Type == ArcTransactionType.DialogueNodeVisited &&
                        t.GetData<string>(TransactionDataKeys.DialogueNodeId) == "gift")
            .Select(t => t.TransactionId.ToString())
            .ToList();
        Assert.NotEmpty(visitTxIds);
        Assert.Contains(committed, t =>
            t.Type == ArcTransactionType.TransactionReversed &&
            visitTxIds.Contains(t.GetData<string>(TransactionDataKeys.ReversedTransactionId)));

        // Conversation 2: fresh avatar object (the unpersisted one died with the "crash")
        var reloadedAvatar = CreateTestAvatar(avatarId);
        await StartConversation(reloadedAvatar, characterInstanceId);

        var retryResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatarId,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "gift",
            Avatar = reloadedAvatar
        });

        Assert.True(retryResult.Successful, retryResult.ErrorMessage);
        Assert.Equal(1, reloadedAvatar.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "HEALTH_POTION")?.Quantity ?? 0);

        var saved = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        Assert.NotNull(saved);
        Assert.Equal(1, saved!.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == "HEALTH_POTION")?.Quantity ?? 0);
    }

    // ===== NodeVisited / AvatarVisitCount conditions =====

    [Fact]
    public async Task NodeVisitedCondition_FailsFirstConversation_PassesSecond()
    {
        var avatar = CreateTestAvatar();
        var characterInstanceId = await SpawnSage(avatar.Id);

        // Conversation 1: gift node never visited, so the secret is not offered
        await StartConversation(avatar, characterInstanceId);
        var firstChoices = await GetOfferedChoiceIds(avatar, characterInstanceId);
        Assert.DoesNotContain("secret", firstChoices);

        // Visit the gift node (terminal — completes the conversation)
        var giftResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "gift",
            Avatar = avatar
        });
        Assert.True(giftResult.Successful, giftResult.ErrorMessage);

        // Conversation 2: the committed log remembers the gift visit
        await StartConversation(avatar, characterInstanceId);
        var secondChoices = await GetOfferedChoiceIds(avatar, characterInstanceId);
        Assert.Contains("secret", secondChoices);

        // And the command path agrees with the query path
        var secretResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "secret",
            Avatar = avatar
        });
        Assert.True(secretResult.Successful, secretResult.ErrorMessage);

        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, ArcRef);
        Assert.Contains(instance.GetCommittedTransactions(), t =>
            t.Type == ArcTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == "secret");
    }

    [Fact]
    public async Task AvatarVisitCountCondition_ThresholdTwo_PassesOnlyOnSecondConversation()
    {
        var avatar = CreateTestAvatar();
        var characterInstanceId = await SpawnSage(avatar.Id);

        // Conversation 1: visit count is 1, threshold is >= 2 — not offered
        await StartConversation(avatar, characterInstanceId);
        var firstChoices = await GetOfferedChoiceIds(avatar, characterInstanceId);
        Assert.DoesNotContain("friend", firstChoices);

        // Complete conversation 1 without touching gift
        var chatResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "chat",
            Avatar = avatar
        });
        Assert.True(chatResult.Successful, chatResult.ErrorMessage);

        // Conversation 2: second visit, threshold met — offered now
        await StartConversation(avatar, characterInstanceId);
        var secondChoices = await GetOfferedChoiceIds(avatar, characterInstanceId);
        Assert.Contains("friend", secondChoices);

        // NodeVisited gating stays independent: gift was never visited
        Assert.DoesNotContain("secret", secondChoices);

        // And the command path agrees with the query path
        var friendResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = avatar.Id,
            ArcRef = ArcRef,
            CharacterInstanceId = characterInstanceId,
            ChoiceId = "friend",
            Avatar = avatar
        });
        Assert.True(friendResult.Successful, friendResult.ErrorMessage);

        var instance = await _repository.GetOrCreateInstanceAsync(avatar.Id, ArcRef);
        Assert.Contains(instance.GetCommittedTransactions(), t =>
            t.Type == ArcTransactionType.DialogueNodeVisited &&
            t.GetData<string>(TransactionDataKeys.DialogueNodeId) == "friend");
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
