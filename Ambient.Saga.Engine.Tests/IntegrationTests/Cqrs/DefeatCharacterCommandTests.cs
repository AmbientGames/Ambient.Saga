using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
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
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();

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

        var sagaArc = new SagaArc
        {
            RefName = "DragonLair",
            DisplayName = "Dragon's Lair",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaArc },
                    Characters = new[] { boss }
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.CharactersLookup[boss.RefName] = boss;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private async Task<Guid> SpawnBossCharacter(Guid avatarId, string sagaRef)
    {
        // Spawn character by creating CharacterSpawned transaction
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
                ["CharacterRef"] = "DragonBoss",
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

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
