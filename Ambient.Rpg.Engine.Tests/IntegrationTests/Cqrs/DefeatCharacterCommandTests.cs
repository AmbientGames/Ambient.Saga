using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
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

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for DefeatCharacterCommand via CQRS pipeline.
/// Tests boss battle completion, transaction logging, and state updates.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class DefeatCharacterCommandTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

    public DefeatCharacterCommandTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithBoss();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<DefeatCharacterCommand>();
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

    private World CreateWorldWithBoss()
    {
        var boss = new Character
        {
            RefName = "DragonBoss",
            DisplayName = "Ancient Dragon"
        };

        var guardian = new Character
        {
            RefName = "FlameGuardian",
            DisplayName = "Flame Guardian",
            GivesQuestTokenOnDefeat = new[] { "GUARDIAN_DEFEATED" }
        };

        var multiBoss = new Character
        {
            RefName = "MultiBoss",
            DisplayName = "Multi-Token Boss",
            GivesQuestTokenOnDefeat = new[] { "TOKEN_A", "TOKEN_B" }
        };

        var arc = new Arc
        {
            RefName = "DragonLair",
            DisplayName = "Dragon's Lair",
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
                    Characters = new[] { boss, guardian, multiBoss }
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.CharactersLookup[boss.RefName] = boss;
        world.CharactersLookup[guardian.RefName] = guardian;
        world.CharactersLookup[multiBoss.RefName] = multiBoss;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();

        return world;
    }

    private Task<Guid> SpawnBossCharacter(Guid avatarId, string arcRef)
        => SpawnCharacter(avatarId, arcRef, "DragonBoss");

    private async Task<Guid> SpawnCharacter(Guid avatarId, string arcRef, string characterRef)
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
                ["CharacterRef"] = characterRef,
                ["CharacterInstanceId"] = characterInstanceId.ToString(),
                ["InitialHealth"] = "1.0"
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { spawnTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { spawnTx.TransactionId });

        return characterInstanceId;
    }

    [Fact]
    public async Task DefeatCharacter_ValidBoss_CreatesCharacterDefeatedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnBossCharacter(avatarId, "DragonLair");

        var command = new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.TransactionIds);

        // Verify CharacterDefeated transaction was created
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var defeatTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == ArcTransactionType.CharacterDefeated);

        Assert.NotNull(defeatTx);
        Assert.Equal(characterInstanceId.ToString(), defeatTx.Data["CharacterInstanceId"]);
        Assert.Equal(avatarId.ToString(), defeatTx.Data["VictorAvatarId"]);
    }

    [Fact]
    public async Task DefeatCharacter_BossDefeat_CharacterMarkedNotAlive()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnBossCharacter(avatarId, "DragonLair");

        var command = new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Replay transactions to verify character state
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var arc = _world.ArcLookup["DragonLair"];
        var triggers = _world.ArcTriggersLookup["DragonLair"];

        var stateMachine = new ArcStateMachine(arc, triggers, _world);
        var state = stateMachine.ReplayToNow(instance);

        // Character should exist but not be alive
        var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == characterInstanceId);
        Assert.NotNull(characterState);
        Assert.False(characterState.IsAlive);
        Assert.Equal(0.0f, characterState.CurrentHealth);
    }

    [Fact]
    public async Task DefeatCharacter_NonExistentCharacter_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var fakeCharacterInstanceId = Guid.NewGuid();

        var command = new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = fakeCharacterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefeatCharacter_InvalidArcRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var characterInstanceId = Guid.NewGuid();

        var command = new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "NonExistentArc",
            CharacterInstanceId = characterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefeatCharacter_TransactionsCommitted_ProperlyPersisted()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnBossCharacter(avatarId, "DragonLair");

        var command = new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify transactions are committed, not pending
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var allTransactions = instance.Transactions;

        Assert.All(allTransactions, tx =>
            Assert.Equal(TransactionStatus.Committed, tx.Status));

        Assert.All(allTransactions, tx =>
            Assert.NotNull(tx.ServerTimestamp));
    }

    [Fact]
    public async Task DefeatCharacter_WithGivesQuestTokenOnDefeat_AwardsToken()
    {
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnCharacter(avatarId, "DragonLair", "FlameGuardian");

        var result = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        Assert.True(result.Successful);

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var tokenTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == ArcTransactionType.QuestTokenAwarded);

        Assert.NotNull(tokenTx);
        Assert.Equal("GUARDIAN_DEFEATED", tokenTx.Data["QuestTokenRef"]);
    }

    [Fact]
    public async Task DefeatCharacter_WithGivesQuestTokenOnDefeat_ProjectsToAvatarProgress()
    {
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnCharacter(avatarId, "DragonLair", "FlameGuardian");

        var result = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        Assert.True(result.Successful);

        var progressRepo = _serviceProvider.GetRequiredService<IAvatarProgressRepository>();
        Assert.True(progressRepo.HasQuestToken(avatarId, "GUARDIAN_DEFEATED"));
    }

    [Fact]
    public async Task DefeatCharacter_WithMultipleTokens_AwardsAll()
    {
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnCharacter(avatarId, "DragonLair", "MultiBoss");

        var result = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        Assert.True(result.Successful);

        var progressRepo = _serviceProvider.GetRequiredService<IAvatarProgressRepository>();
        Assert.True(progressRepo.HasQuestToken(avatarId, "TOKEN_A"));
        Assert.True(progressRepo.HasQuestToken(avatarId, "TOKEN_B"));
    }

    [Fact]
    public async Task DefeatCharacter_WithoutGivesQuestTokenOnDefeat_NoTokenAwarded()
    {
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnBossCharacter(avatarId, "DragonLair");

        var result = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        Assert.True(result.Successful);

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var tokenTxs = instance.GetCommittedTransactions()
            .Where(t => t.Type == ArcTransactionType.QuestTokenAwarded)
            .ToList();

        Assert.Empty(tokenTxs);
    }

    [Fact]
    public async Task DefeatCharacter_SecondDefeat_NoDuplicateKillCreditOrTokens()
    {
        // Arrange: a token-granting character, defeated once already
        var avatarId = Guid.NewGuid();
        var characterInstanceId = await SpawnCharacter(avatarId, "DragonLair", "FlameGuardian");

        var firstResult = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });
        Assert.True(firstResult.Successful);

        // Act: report the same defeat again (double-dispatched battle end, replayed client, ...)
        var secondResult = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            ArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        // Assert: no-op success — no new transactions written
        Assert.True(secondResult.Successful, secondResult.ErrorMessage);
        Assert.Empty(secondResult.TransactionIds);

        // Exactly one kill credit and one token award in the log
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var committed = instance.GetCommittedTransactions();
        Assert.Single(committed.Where(t => t.Type == ArcTransactionType.CharacterDefeated));
        Assert.Single(committed.Where(t => t.Type == ArcTransactionType.QuestTokenAwarded));
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
