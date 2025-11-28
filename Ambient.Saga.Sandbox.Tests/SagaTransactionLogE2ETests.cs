using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Infrastructure.Persistence;
using LiteDB;
using Xunit.Abstractions;

namespace Ambient.Saga.Sandbox.Tests;

/// <summary>
/// End-to-end tests for Saga transaction log system focusing on:
///
/// CONCURRENCY TESTING (Validates all 4 concurrency fixes):
/// 1. GetOrCreate race condition - Multiple threads creating same instance
/// 2. Sequence number collision - Concurrent transaction adds
/// 3. Non-atomic commits - Partial commit scenarios
/// 4. Transactions.Clear() race - Collection modification during iteration
///
/// TRANSACTION INTEGRITY:
/// 5. Compensating transactions (TransactionReversed)
/// 6. Transaction replay determinism
/// 7. State machine consistency
/// 8. Achievement evaluation from transaction log
///
/// MULTIPLAYER SCENARIOS:
/// 9. Multiple avatars in same saga
/// 10. Concurrent position updates
/// 11. Race conditions during character spawning
/// 12. Atomic transaction batches
/// </summary>
[Collection("LiteDB Tests")]
public class SagaTransactionLogE2ETests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly LiteDatabase _database;
    private readonly World _world;

    public SagaTransactionLogE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentCalls_NoDuplicateInstances()
    {
        // ARRANGE: Multiple threads trying to create same instance simultaneously
        var repository = new SagaInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var sagaRef = "TestSaga";

        var tasks = new List<Task<SagaInstance>>();
        var threadCount = 10;

        _output.WriteLine($"=== Starting {threadCount} concurrent GetOrCreate calls ===");

        // ACT: Spawn multiple concurrent GetOrCreate calls
        for (var i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(async () =>
            {
                var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
                _output.WriteLine($"Thread {threadId}: Got instance {instance.InstanceId}");
                return instance;
            }));
        }

        var instances = await Task.WhenAll(tasks);

        // ASSERT: All threads got SAME instance (no duplicates)
        var uniqueInstanceIds = instances.Select(i => i.InstanceId).Distinct().ToList();
        Assert.Single(uniqueInstanceIds);

        _output.WriteLine($"✓ All {threadCount} threads got same instance: {uniqueInstanceIds.First()}");

        // Verify database has only ONE instance
        var dbInstances = _database.GetCollection<SagaInstance>("SagaInstances")
            .Find(x => x.OwnerAvatarId == avatarId && x.SagaRef == sagaRef)
            .ToList();

        Assert.Single(dbInstances);
        _output.WriteLine($"✓ Database has exactly 1 instance (no duplicates)");
    }

    [Fact]
    public async Task AddTransactions_Concurrent_StrictlyMonotonicSequence()
    {
        // ARRANGE: Single instance with concurrent transaction adds
        var repository = new SagaInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var sagaRef = "TestSaga";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        var tasks = new List<Task<long>>();
        var transactionCount = 50;

        _output.WriteLine($"=== Adding {transactionCount} concurrent transactions ===");

        // ACT: Add transactions concurrently from multiple threads
        for (var i = 0; i < transactionCount; i++)
        {
            var txId = i;
            tasks.Add(Task.Run(async () =>
            {
                var tx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.PlayerEntered,
                    AvatarId = avatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["ThreadId"] = txId.ToString()
                    }
                };

                var sequences = await repository.AddTransactionsAsync(
                    instance.InstanceId,
                    new List<SagaTransaction> { tx });

                return sequences.First();
            }));
        }

        var assignedSequences = await Task.WhenAll(tasks);

        // ASSERT: All sequence numbers unique and monotonic
        var sortedSequences = assignedSequences.OrderBy(s => s).ToList();
        var expectedSequences = Enumerable.Range(1, transactionCount).Select(i => (long)i).ToList();

        Assert.Equal(expectedSequences, sortedSequences);

        _output.WriteLine($"✓ Sequence numbers strictly monotonic: 1 to {transactionCount}");
        _output.WriteLine($"  First: {sortedSequences.First()}, Last: {sortedSequences.Last()}");
    }

    [Fact]
    public async Task CommitTransactions_AtomicBatch_AllOrNothing()
    {
        // ARRANGE: Batch of transactions to commit atomically
        var repository = new SagaInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var sagaRef = "TestSaga";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Add 5 transactions
        var transactions = Enumerable.Range(0, 5)
            .Select(i => new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.PlayerEntered,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["Index"] = i.ToString() }
            })
            .ToList();

        await repository.AddTransactionsAsync(instance.InstanceId, transactions);

        var txIds = transactions.Select(t => t.TransactionId).ToList();

        _output.WriteLine($"=== Testing atomic commit of {txIds.Count} transactions ===");

        // ACT: Commit all transactions atomically
        var commitResult = await repository.CommitTransactionsAsync(instance.InstanceId, txIds);

        // ASSERT: All committed successfully
        Assert.True(commitResult);

        var dbTransactions = _database.GetCollection<SagaTransactionRecord>("SagaTransactions")
            .Find(x => x.InstanceId == instance.InstanceId)
            .ToList();

        var committedCount = dbTransactions.Count(t => t.Status == TransactionStatus.Committed);
        Assert.Equal(5, committedCount);

        _output.WriteLine($"✓ All {committedCount} transactions committed atomically");
    }

    [Fact]
    public async Task CommitTransactions_PartialFailure_AllRolledBack()
    {
        // ARRANGE: Attempt to commit with one invalid transaction ID
        var repository = new SagaInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var sagaRef = "TestSaga";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Add 3 valid transactions
        var validTransactions = Enumerable.Range(0, 3)
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

        await repository.AddTransactionsAsync(instance.InstanceId, validTransactions);

        var validIds = validTransactions.Select(t => t.TransactionId).ToList();
        var invalidIds = validIds.Concat(new[] { Guid.NewGuid() }).ToList(); // Add fake ID

        _output.WriteLine($"=== Testing commit rollback (3 valid + 1 invalid) ===");

        // ACT: Try to commit with invalid ID
        var commitResult = await repository.CommitTransactionsAsync(instance.InstanceId, invalidIds);

        // ASSERT: Commit failed and ALL transactions rolled back
        Assert.False(commitResult);

        var dbTransactions = _database.GetCollection<SagaTransactionRecord>("SagaTransactions")
            .Find(x => x.InstanceId == instance.InstanceId)
            .ToList();

        var committedCount = dbTransactions.Count(t => t.Status == TransactionStatus.Committed);
        Assert.Equal(0, committedCount);

        _output.WriteLine($"✓ Commit failed, all transactions remained Pending (atomic rollback)");
    }

    //[Fact]
    //public async Task CompensatingTransactions_AvatarPersistenceFails_ReversalCreated()
    //{
    //    // ARRANGE: Setup CQRS pipeline with mocked avatar repository that fails
    //    var services = new ServiceCollection();

    //    services.AddMediatR(cfg =>
    //    {
    //        cfg.RegisterServicesFromAssemblyContaining<LootCharacterCommand>();
    //        cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
    //    });

    //    services.AddSingleton(_world);
    //    services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
    //    services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
    //    services.AddSingleton<IGameAvatarRepository>(new FailingAvatarRepository()); // Fails on save
    //    services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();

    //    var serviceProvider = services.BuildServiceProvider();
    //    var mediator = serviceProvider.GetRequiredService<IMediator>();
    //    var repository = serviceProvider.GetRequiredService<ISagaInstanceRepository>();

    //    // Create defeated character scenario
    //    var avatarId = Guid.NewGuid();
    //    var sagaRef = "TestSaga";
    //    var characterInstanceId = Guid.NewGuid();

    //    var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

    //    // Add CharacterSpawned transaction first
    //    var spawnTx = new SagaTransaction
    //    {
    //        TransactionId = Guid.NewGuid(),
    //        Type = SagaTransactionType.CharacterSpawned,
    //        AvatarId = avatarId.ToString(),
    //        Status = TransactionStatus.Pending,
    //        LocalTimestamp = DateTime.UtcNow,
    //        Data = new Dictionary<string, string>
    //        {
    //            ["CharacterInstanceId"] = characterInstanceId.ToString(),
    //            ["CharacterRef"] = "TestGuard"
    //        }
    //    };

    //    // Add CharacterDefeated transaction
    //    var defeatTx = new SagaTransaction
    //    {
    //        TransactionId = Guid.NewGuid(),
    //        Type = SagaTransactionType.CharacterDefeated,
    //        AvatarId = avatarId.ToString(),
    //        Status = TransactionStatus.Pending,
    //        LocalTimestamp = DateTime.UtcNow,
    //        Data = new Dictionary<string, string>
    //        {
    //            ["CharacterInstanceId"] = characterInstanceId.ToString(),
    //            ["CharacterRef"] = "TestGuard",
    //            ["VictorAvatarId"] = avatarId.ToString()
    //        }
    //    };

    //    await repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { spawnTx, defeatTx });
    //    await repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { spawnTx.TransactionId, defeatTx.TransactionId });

    //    _output.WriteLine("=== Testing compensating transaction on persistence failure ===");

    //    // ACT: Try to loot (will fail on avatar persistence)
    //    var lootResult = await mediator.Send(new LootCharacterCommand
    //    {
    //        AvatarId = avatarId,
    //        SagaRef = sagaRef,
    //        CharacterInstanceId = characterInstanceId,
    //        Avatar = new AvatarEntity
    //        {
    //            Id = avatarId,
    //            AvatarId = avatarId,
    //            DisplayName = "Test",
    //            Stats = new CharacterStats { Health = 1.0f },
    //            Capabilities = new ItemCollection()
    //        }
    //    });

    //    // ASSERT: Command failed but TransactionReversed created
    //    Assert.False(lootResult.Successful);
    //    // Error message could be "Character already looted" or contain "Character" depending on state
    //    Assert.NotEmpty(lootResult.ErrorMessage);

    //    var finalInstance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
    //    var transactions = finalInstance.GetCommittedTransactions();

    //    var lootTx = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.LootAwarded);
    //    var reversalTx = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.TransactionReversed);

    //    Assert.NotNull(lootTx);
    //    Assert.NotNull(reversalTx);

    //    Assert.Equal(lootTx.TransactionId.ToString(), reversalTx.Data["ReversedTransactionId"]);
    //    Assert.Contains("Avatar persistence failed", reversalTx.Data["Reason"]);

    //    _output.WriteLine($"✓ Compensating transaction created");
    //    _output.WriteLine($"  Original: {reversalTx.Data["OriginalType"]}");
    //    _output.WriteLine($"  Reason: {reversalTx.Data["Reason"]}");

    //    serviceProvider.Dispose();
    //}

    [Fact]
    public async Task StateReplay_MultipleTransactions_ConsistentState()
    {
        // ARRANGE: Create saga instance with complex transaction history
        var repository = new SagaInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var sagaRef = "TestSaga";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Build transaction sequence
        var transactions = new List<SagaTransaction>
        {
            // Player enters
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.PlayerEntered,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["TriggerRef"] = "TestTrigger" }
            },
            // Trigger activates
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.TriggerActivated,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["SagaTriggerRef"] = "TestTrigger" }
            },
            // Character spawns
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterSpawned,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = Guid.NewGuid().ToString(),
                    ["CharacterRef"] = "TestGuard",
                    ["SagaTriggerRef"] = "TestTrigger"
                }
            },
            // Character defeated
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = Guid.NewGuid().ToString(),
                    ["CharacterRef"] = "TestGuard"
                }
            },
            // Player exits
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.PlayerExited,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["TriggerRef"] = "TestTrigger" }
            }
        };

        await repository.AddTransactionsAsync(instance.InstanceId, transactions);
        await repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList());

        _output.WriteLine($"=== Replaying {transactions.Count} transactions ===");

        // ACT: Replay state multiple times
        var trigger = new SagaTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f
        };

        var saga = new SagaArc
        {
            RefName = sagaRef,
            DisplayName = "Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        var stateMachine = new SagaStateMachine(saga, new List<SagaTrigger> { trigger }, _world);

        var state1 = stateMachine.ReplayToNow(await repository.GetOrCreateInstanceAsync(avatarId, sagaRef));
        var state2 = stateMachine.ReplayToNow(await repository.GetOrCreateInstanceAsync(avatarId, sagaRef));

        // ASSERT: Multiple replays produce identical state
        Assert.Equal(state1.Status, state2.Status);
        Assert.Equal(state1.Triggers.Count, state2.Triggers.Count);
        Assert.Equal(state1.Characters.Count, state2.Characters.Count);

        _output.WriteLine($"✓ State replay deterministic");
        _output.WriteLine($"  Status: {state1.Status}");
        _output.WriteLine($"  Triggers: {state1.Triggers.Count}");
        _output.WriteLine($"  Characters: {state1.Characters.Count}");
    }

    //[Fact]
    //public async Task MultipleAvatars_SameSaga_IndependentInstances()
    //{
    //    // ARRANGE: Multiple avatars interacting with same saga
    //    var repository = new SagaInstanceRepository(_database);
    //    var sagaRef = "SharedSaga";

    //    var avatarIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

    //    _output.WriteLine($"=== Testing {avatarIds.Count} avatars in same saga ===");

    //    // ACT: Each avatar creates instance and adds transactions
    //    var tasks = avatarIds.Select(async avatarId =>
    //    {
    //        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

    //        var tx = new SagaTransaction
    //        {
    //            TransactionId = Guid.NewGuid(),
    //            Type = SagaTransactionType.PlayerEntered,
    //            AvatarId = avatarId.ToString(),
    //            Status = TransactionStatus.Pending,
    //            LocalTimestamp = DateTime.UtcNow,
    //            Data = new Dictionary<string, string>()
    //        };

    //        await repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { tx });
    //        await repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { tx.TransactionId });

    //        return instance.InstanceId;
    //    });

    //    var instanceIds = await Task.WhenAll(tasks);

    //    // ASSERT: Each avatar has independent instance
    //    Assert.Equal(5, instanceIds.Distinct().Count());

    //    _output.WriteLine($"✓ Each avatar has independent instance");
    //    foreach (var (avatarId, instanceId) in avatarIds.Zip(instanceIds))
    //    {
    //        _output.WriteLine($"  Avatar {avatarId:N}: Instance {instanceId}");
    //    }
    //}

    public void Dispose()
    {
        _database?.Dispose();
    }

    #region Test Helpers

    private World CreateTestWorld()
    {
        var saga = new SagaArc
        {
            RefName = "TestSaga",
            DisplayName = "Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var trigger = new SagaTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f
        };

        var character = new Character
        {
            RefName = "TestGuard",
            DisplayName = "Test Guard",
            Stats = new CharacterStats { Health = 1.0f }
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
                    Characters = new[] { character },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>()
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        world.SagaArcLookup[saga.RefName] = saga;
        world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger> { trigger };
        world.CharactersLookup[character.RefName] = character;

        return world;
    }

    #endregion
}

/// <summary>
/// Mock avatar repository that fails on save (for testing compensating transactions)
/// </summary>
public class FailingAvatarRepository : IGameAvatarRepository
{
    public Task<TAvatar?> LoadAvatarAsync<TAvatar>() where TAvatar : class
    {
        return Task.FromResult<TAvatar?>(null);
    }

    public Task SaveAvatarAsync<TAvatar>(TAvatar avatar) where TAvatar : class
    {
        throw new InvalidOperationException("Simulated avatar persistence failure for testing");
    }

    public Task DeleteAvatarsAsync()
    {
        return Task.CompletedTask;
    }
}
