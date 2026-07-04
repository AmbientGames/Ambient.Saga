using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
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

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

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
    private readonly ISagaInstanceRepository _repository;

    public DefeatCharacterCommandTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithBoss();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<DefeatCharacterCommand>();
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

        var sagaArc = new SagaArc
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
                    SagaArcs = new[] { sagaArc },
                    Characters = new[] { boss, guardian, multiBoss }
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.CharactersLookup[boss.RefName] = boss;
        world.CharactersLookup[guardian.RefName] = guardian;
        world.CharactersLookup[multiBoss.RefName] = multiBoss;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private Task<Guid> SpawnBossCharacter(Guid avatarId, string sagaRef)
        => SpawnCharacter(avatarId, sagaRef, "DragonBoss");

    private async Task<Guid> SpawnCharacter(Guid avatarId, string sagaRef, string characterRef)
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
                ["CharacterRef"] = characterRef,
                ["CharacterInstanceId"] = characterInstanceId.ToString(),
                ["InitialHealth"] = "1.0"
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { spawnTx });
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
            SagaArcRef = "DragonLair",
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
            .FirstOrDefault(t => t.Type == SagaTransactionType.CharacterDefeated);

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
            SagaArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Replay transactions to verify character state
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var saga = _world.SagaArcLookup["DragonLair"];
        var triggers = _world.SagaTriggersLookup["DragonLair"];

        var stateMachine = new SagaStateMachine(saga, triggers, _world);
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
            SagaArcRef = "DragonLair",
            CharacterInstanceId = fakeCharacterInstanceId
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefeatCharacter_InvalidSagaRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var characterInstanceId = Guid.NewGuid();

        var command = new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "NonExistentSaga",
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
            SagaArcRef = "DragonLair",
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
            SagaArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        Assert.True(result.Successful);

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var tokenTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.QuestTokenAwarded);

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
            SagaArcRef = "DragonLair",
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
            SagaArcRef = "DragonLair",
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
            SagaArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        Assert.True(result.Successful);

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var tokenTxs = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.QuestTokenAwarded)
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
            SagaArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });
        Assert.True(firstResult.Successful);

        // Act: report the same defeat again (double-dispatched battle end, replayed client, ...)
        var secondResult = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "DragonLair",
            CharacterInstanceId = characterInstanceId
        });

        // Assert: no-op success — no new transactions written
        Assert.True(secondResult.Successful, secondResult.ErrorMessage);
        Assert.Empty(secondResult.TransactionIds);

        // Exactly one kill credit and one token award in the log
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "DragonLair");
        var committed = instance.GetCommittedTransactions();
        Assert.Single(committed.Where(t => t.Type == SagaTransactionType.CharacterDefeated));
        Assert.Single(committed.Where(t => t.Type == SagaTransactionType.QuestTokenAwarded));
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
