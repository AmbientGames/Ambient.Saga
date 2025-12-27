using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for SubmitReactionCommand - the active defense system.
/// Tests that player defensive reactions are properly recorded as transactions.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class SubmitReactionCommandTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public SubmitReactionCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SubmitReactionCommand>();
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

    [Fact]
    public async Task SubmitReaction_RecordsTransaction_ForActiveBattle()
    {
        // ARRANGE: Start a battle first
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var sagaRef = "TestSaga";

        // Spawn character
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var enemyInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // Start battle using BattleSetup
        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = _world.AvatarArchetypesLookup["TestArchetype"];
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = _world.CharactersLookup["TestEnemy"];
        battleSetup.OpponentCapabilities = new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();
        var playerCombatant = battleEngine.GetPlayer();
        var enemyCombatant = battleEngine.GetEnemy();

        var startResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = enemyInstanceId,
            PlayerCombatant = playerCombatant,
            EnemyCombatant = enemyCombatant,
            PlayerAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world),
            RandomSeed = 12345,
            Avatar = avatar
        });

        Assert.True(startResult.Successful, $"Battle start failed: {startResult.ErrorMessage}");
        var battleInstanceId = startResult.TransactionIds.First();
        _output.WriteLine($"Battle started: {battleInstanceId}");

        // ACT: Submit a reaction
        var reactionResult = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleInstanceId,
            Reaction = PlayerDefenseType.Parry,
            Avatar = avatar
        });

        // ASSERT
        Assert.True(reactionResult.Successful, $"Reaction failed: {reactionResult.ErrorMessage}");

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var reactionTx = finalInstance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                                 t.Data.TryGetValue("ActionType", out var action) &&
                                 action == "Reaction");

        Assert.NotNull(reactionTx);
        Assert.Equal("Parry", reactionTx.Data["ReactionType"]);
        Assert.Equal(battleInstanceId.ToString(), reactionTx.Data["BattleTransactionId"]);
        _output.WriteLine($"Reaction recorded: {reactionTx.Data["ReactionType"]}");
    }

    [Theory]
    [InlineData(PlayerDefenseType.Dodge)]
    [InlineData(PlayerDefenseType.Block)]
    [InlineData(PlayerDefenseType.Parry)]
    [InlineData(PlayerDefenseType.Brace)]
    [InlineData(PlayerDefenseType.None)]
    public async Task SubmitReaction_AllDefenseTypes_RecordedCorrectly(PlayerDefenseType defenseType)
    {
        // ARRANGE
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var sagaRef = "TestSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var enemyInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = _world.AvatarArchetypesLookup["TestArchetype"];
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = _world.CharactersLookup["TestEnemy"];
        battleSetup.OpponentCapabilities = new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();

        var startResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = enemyInstanceId,
            PlayerCombatant = battleEngine.GetPlayer(),
            EnemyCombatant = battleEngine.GetEnemy(),
            PlayerAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world),
            RandomSeed = 12345,
            Avatar = avatar
        });

        var battleInstanceId = startResult.TransactionIds.First();

        // ACT
        var reactionResult = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleInstanceId,
            Reaction = defenseType,
            Avatar = avatar
        });

        // ASSERT
        Assert.True(reactionResult.Successful);

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var reactionTx = finalInstance.GetCommittedTransactions()
            .First(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                       t.Data.TryGetValue("ActionType", out var action) &&
                       action == "Reaction");

        Assert.Equal(defenseType.ToString(), reactionTx.Data["ReactionType"]);
        _output.WriteLine($"Defense type {defenseType} recorded successfully");
    }

    [Fact]
    public async Task SubmitReaction_FailsForNonexistentBattle()
    {
        // ARRANGE
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var sagaRef = "TestSaga";
        var fakeBattleId = Guid.NewGuid();

        // Create saga instance without starting a battle
        await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // ACT
        var result = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = fakeBattleId,
            Reaction = PlayerDefenseType.Dodge,
            Avatar = avatar
        });

        // ASSERT
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage);
        _output.WriteLine($"Correctly failed: {result.ErrorMessage}");
    }

    [Fact]
    public async Task SubmitReaction_MultipleReactions_IncrementsTurnNumber()
    {
        // ARRANGE
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var sagaRef = "TestSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var enemyInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = _world.AvatarArchetypesLookup["TestArchetype"];
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = _world.CharactersLookup["TestEnemy"];
        battleSetup.OpponentCapabilities = new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();

        var startResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = enemyInstanceId,
            PlayerCombatant = battleEngine.GetPlayer(),
            EnemyCombatant = battleEngine.GetEnemy(),
            PlayerAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world),
            RandomSeed = 12345,
            Avatar = avatar
        });

        var battleInstanceId = startResult.TransactionIds.First();

        // ACT: Submit multiple reactions
        await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleInstanceId,
            Reaction = PlayerDefenseType.Dodge,
            Avatar = avatar
        });

        await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleInstanceId,
            Reaction = PlayerDefenseType.Block,
            Avatar = avatar
        });

        await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleInstanceId,
            Reaction = PlayerDefenseType.Parry,
            Avatar = avatar
        });

        // ASSERT
        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var reactionTxs = finalInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                       t.Data.TryGetValue("ActionType", out var action) &&
                       action == "Reaction")
            .OrderBy(t => int.Parse(t.Data["TurnNumber"]))
            .ToList();

        Assert.Equal(3, reactionTxs.Count);

        // Verify turn numbers are sequential (accounting for enemy turns too)
        var turnNumbers = reactionTxs.Select(t => int.Parse(t.Data["TurnNumber"])).ToList();
        for (int i = 1; i < turnNumbers.Count; i++)
        {
            Assert.True(turnNumbers[i] > turnNumbers[i - 1],
                $"Turn numbers should increase: {turnNumbers[i - 1]} vs {turnNumbers[i]}");
        }

        _output.WriteLine($"Recorded {reactionTxs.Count} reactions with turn numbers: {string.Join(", ", turnNumbers)}");
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Test World Setup

    private World CreateTestWorld()
    {
        var saga = new SagaArc
        {
            RefName = "TestSaga",
            DisplayName = "Test Arena",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        var trigger = new SagaTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    CharacterRef = "TestEnemy"
                }
            }
        };

        var enemy = new Character
        {
            RefName = "TestEnemy",
            DisplayName = "Test Enemy",
            Stats = new CharacterStats
            {
                Health = 0.5f,
                Strength = 0.1f,
                Defense = 0.1f
            }
        };

        var archetype = new AvatarArchetype
        {
            RefName = "TestArchetype",
            DisplayName = "Test Archetype",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.2f,
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.1f
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };

        var affinity = new CharacterAffinity
        {
            RefName = "Physical",
            DisplayName = "Physical"
        };

        var world = new World
        {
            IsProcedural = true,
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "TestWorld",
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
                    SagaArcs = new[] { saga },
                    Characters = new[] { enemy },
                    Equipment = Array.Empty<Equipment>(),
                    AvatarArchetypes = new[] { archetype },
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = new[] { affinity },
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Consumables = Array.Empty<Consumable>()
                }
            }
        };

        world.SagaArcLookup[saga.RefName] = saga;
        world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger> { trigger };
        world.CharactersLookup[enemy.RefName] = enemy;
        world.AvatarArchetypesLookup[archetype.RefName] = archetype;
        world.CharacterAffinitiesLookup[affinity.RefName] = affinity;

        return world;
    }

    private AvatarEntity CreateTestAvatar(Guid avatarId)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            DisplayName = "Test Avatar",
            ArchetypeRef = "TestArchetype",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.2f,
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.1f
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            },
            AffinityRef = "Physical"
        };
    }

    #endregion
}
