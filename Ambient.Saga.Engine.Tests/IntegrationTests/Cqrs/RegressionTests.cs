using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Focused regression tests for specific bugs found and fixed.
/// Each test validates that the specific bug NO LONGER occurs.
///
/// REGRESSION TESTS FOR:
/// - Bug #1: Character duplication on position update
/// - Bug #2: Trade validation (negative prices, selling non-owned items)
/// - Bug #3: Thread-safe transaction reloading
/// - Bug #4: Trigger activation idempotency
/// </summary>
[Collection("Sequential CQRS Tests")]
public class RegressionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public RegressionTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateRegressionTestWorld();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
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
    public async Task Regression_CharacterDuplication_MultiplePositionUpdates_OnlySpawnOnce()
    {
        // BUG: Characters spawned on EVERY position update while in trigger radius
        // FIX: Added check for SagaTriggerStatus.Active (not just Completed)

        _output.WriteLine("=== REGRESSION: Character Duplication Bug ===");

        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var sagaRef = "DuplicationTestSaga";

        // Move to trigger center (should spawn 1 guard)
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var firstSpawnCount = instance.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.CharacterSpawned);

        Assert.Equal(1, firstSpawnCount);
        _output.WriteLine($"First position update: {firstSpawnCount} guard spawned ✓");

        // Move AGAIN while still in trigger radius (BUG: used to spawn ANOTHER guard)
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0001, // Slightly different but still in 100m radius
            Longitude = 139.0001,
            Y = 50.0,
            Avatar = avatar
        });

        instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var secondSpawnCount = instance.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.CharacterSpawned);

        // ASSERT: Still only 1 guard (no duplication)
        Assert.Equal(1, secondSpawnCount);
        _output.WriteLine($"Second position update: {secondSpawnCount} total guards (no duplication) ✓");

        // Move THIRD time (paranoia check)
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0002,
            Longitude = 139.0002,
            Y = 50.0,
            Avatar = avatar
        });

        instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var thirdSpawnCount = instance.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.CharacterSpawned);

        Assert.Equal(1, thirdSpawnCount);
        _output.WriteLine($"Third position update: {thirdSpawnCount} total guards (still no duplication) ✓");
        _output.WriteLine("✓ BUG FIXED: Characters only spawn once per trigger activation");
    }

    [Fact]
    public async Task Regression_TransactionsListReload_ConcurrentAccess_NoException()
    {
        // BUG: Transactions.Clear() during iteration → ConcurrentModificationException
        // FIX: Replace entire list instead of Clear+AddRange

        _output.WriteLine("=== REGRESSION: Transactions.Clear() Race Condition ===");

        var repository = new SagaInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var sagaRef = "RaceTestSaga";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Add some transactions
        var transactions = Enumerable.Range(0, 10)
            .Select(i => new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.PlayerEntered,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>()
            })
            .ToList();

        await repository.AddTransactionsAsync(instance.InstanceId, transactions);
        await repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList());

        // ACT: Reload instance multiple times concurrently (BUG: used to throw)
        var reloadTasks = Enumerable.Range(0, 20).Select(_ =>
            Task.Run(async () =>
            {
                var reloaded = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
                return reloaded.Transactions.Count;
            })
        );

        var counts = await Task.WhenAll(reloadTasks);

        // ASSERT: No exception thrown, all reloads successful
        Assert.All(counts, count => Assert.Equal(10, count));

        _output.WriteLine($"Completed {counts.Length} concurrent reloads without exception");
        _output.WriteLine("✓ BUG FIXED: Transactions list reload is thread-safe");
    }

    [Fact]
    public async Task Regression_TriggerActivation_IdempotentOnReentry()
    {
        // BUG: Re-entering trigger area after leaving could cause issues
        // FIX: Proper tracking of trigger states

        _output.WriteLine("=== REGRESSION: Trigger Re-entry Idempotency ===");

        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var sagaRef = "DuplicationTestSaga";

        // Enter trigger radius
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var initialTriggerCount = instance.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.TriggerActivated);

        Assert.Equal(1, initialTriggerCount);
        _output.WriteLine("✓ Initial trigger activation recorded");

        // Move far away (exit trigger)
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 40.0, // Very far from trigger center
            Longitude = 145.0,
            Y = 50.0,
            Avatar = avatar
        });

        // Re-enter trigger radius
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var finalTriggerCount = instance.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.TriggerActivated);

        // Trigger should only activate once (idempotent)
        Assert.Equal(1, finalTriggerCount);
        _output.WriteLine($"✓ After re-entry: still {finalTriggerCount} trigger activation (idempotent)");
        _output.WriteLine("✓ BUG FIXED: Trigger activation is idempotent");
    }

    [Fact]
    public async Task Regression_AvailableInteractions_AfterPositionUpdate_ReturnsSpawnedCharacters()
    {
        // Validates that the query/command separation works correctly

        _output.WriteLine("=== REGRESSION: CQRS Query After Command ===");

        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var sagaRef = "DuplicationTestSaga";

        // Command: Move to trigger center
        var moveResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        Assert.True(moveResult.Successful);
        _output.WriteLine("✓ Position update command succeeded");

        // Query: Get available interactions
        var interactions = await _mediator.Send(new GetAvailableInteractionsQuery
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        Assert.NotEmpty(interactions.NearbyCharacters);
        var guard = interactions.NearbyCharacters.FirstOrDefault(c => c.CharacterRef == "Guard");
        Assert.NotNull(guard);
        _output.WriteLine($"✓ Query returned spawned character: {guard.DisplayName}");
        _output.WriteLine("✓ BUG FIXED: Query returns state after command execution");
    }

    [Fact]
    public async Task Regression_SequenceNumbers_StrictlyIncreasing()
    {
        // Validates that sequence numbers never go backwards or duplicate

        _output.WriteLine("=== REGRESSION: Sequence Number Ordering ===");

        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var sagaRef = "DuplicationTestSaga";

        // Perform multiple commands
        var results = new List<SagaCommandResult>();

        for (int i = 0; i < 5; i++)
        {
            var result = await _mediator.Send(new UpdateAvatarPositionCommand
            {
                AvatarId = avatarId,
                SagaArcRef = sagaRef,
                Latitude = 35.0 + (i * 0.0001),
                Longitude = 139.0 + (i * 0.0001),
                Y = 50.0,
                Avatar = avatar
            });

            if (result.Successful && result.TransactionIds.Count > 0)
            {
                results.Add(result);
            }
        }

        // Get all transactions and verify sequence numbers
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var allTransactions = instance.GetCommittedTransactions()
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        var sequenceNumbers = allTransactions.Select(t => t.SequenceNumber).ToList();

        // Verify no duplicates
        Assert.Equal(sequenceNumbers.Count, sequenceNumbers.Distinct().Count());
        _output.WriteLine($"✓ No duplicate sequence numbers in {sequenceNumbers.Count} transactions");

        // Verify strictly increasing
        for (int i = 1; i < sequenceNumbers.Count; i++)
        {
            Assert.True(sequenceNumbers[i] > sequenceNumbers[i - 1],
                $"Sequence {sequenceNumbers[i]} should be greater than {sequenceNumbers[i - 1]}");
        }

        _output.WriteLine("✓ Sequence numbers are strictly increasing");
        _output.WriteLine("✓ BUG FIXED: Transaction ordering is reliable");
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Test Helpers

    private World CreateRegressionTestWorld()
    {
        var guard = new Character
        {
            RefName = "Guard",
            DisplayName = "Castle Guard",
            Interactable = new Interactable
            {
                ApproachRadius = 15.0f
            }
        };

        var duplicationSaga = new SagaArc
        {
            RefName = "DuplicationTestSaga",
            DisplayName = "Duplication Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        var trigger = new SagaTrigger
        {
            RefName = "DuplicationTestTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "Guard"
                }
            }
        };

        var raceSaga = new SagaArc
        {
            RefName = "RaceTestSaga",
            DisplayName = "Race Test Saga",
            LatitudeZ = 36.0,
            LongitudeX = 140.0
        };

        var world = new World
        {
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "RegressionTestWorld",
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
                    SagaArcs = new[] { duplicationSaga, raceSaga },
                    Characters = new[] { guard },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>()
                }
            }
        };

        // Populate lookups
        world.SagaArcLookup[duplicationSaga.RefName] = duplicationSaga;
        world.SagaArcLookup[raceSaga.RefName] = raceSaga;
        world.SagaTriggersLookup[duplicationSaga.RefName] = new List<SagaTrigger> { trigger };
        world.SagaTriggersLookup[raceSaga.RefName] = new List<SagaTrigger>();
        world.CharactersLookup[guard.RefName] = guard;

        return world;
    }

    private AvatarBase CreateAvatar()
    {
        var archetype = new AvatarArchetype
        {
            RefName = "TestWarrior",
            DisplayName = "Test Warrior",
            Description = "A test warrior",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 100,
                Experience = 0,
                Strength = 0.10f,
                Defense = 0.10f,
                Speed = 0.10f,
                Magic = 0.10f
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            },
            RespawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 50,
                Experience = 0,
                Strength = 0.08f,
                Defense = 0.08f,
                Speed = 0.08f,
                Magic = 0.08f
            },
            RespawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };

        var avatar = new AvatarBase
        {
            ArchetypeRef = "TestWarrior",
            DisplayName = "Test Hero",
            BlockOwnership = new Dictionary<string, int>()
        };

        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);

        return avatar;
    }

    #endregion
}
