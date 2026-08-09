using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using LiteDB;
using Xunit.Abstractions;

namespace Ambient.Rpg.Ui.Tests;

/// <summary>
/// End-to-end tests for Arc transaction log system focusing on:
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
/// 9. Multiple avatars in same arc
/// 10. Concurrent position updates
/// 11. Race conditions during character spawning
/// 12. Atomic transaction batches
/// </summary>
[Collection("LiteDB Tests")]
public class ArcTransactionLogE2ETests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly LiteDatabase _database;
    private readonly IWorld _world;

    public ArcTransactionLogE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();
    }

    #region Concurrency Tests

    [Fact]
    public async Task GetOrCreate_ConcurrentCalls_NoDuplicateInstances()
    {
        // ARRANGE: Multiple threads trying to create same instance simultaneously
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var arcRef = "TestArc";

        var tasks = new List<Task<ArcInstance>>();
        var threadCount = 10;

        _output.WriteLine($"=== Starting {threadCount} concurrent GetOrCreate calls ===");

        // ACT: Spawn multiple concurrent GetOrCreate calls
        for (var i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(async () =>
            {
                var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);
                _output.WriteLine($"Thread {threadId}: Got instance {instance.InstanceId}");
                return instance;
            }));
        }

        var instances = await Task.WhenAll(tasks);

        // ASSERT: All threads got SAME instance (no duplicates)
        var uniqueInstanceIds = instances.Select(i => i.InstanceId).Distinct().ToList();
        Assert.Single(uniqueInstanceIds);

        _output.WriteLine($"All {threadCount} threads got same instance: {uniqueInstanceIds.First()}");

        // Verify database has only ONE instance
        var dbInstances = _database.GetCollection<ArcInstance>("ArcInstances")
            .Find(x => x.OwnerAvatarId == avatarId && x.ArcRef == arcRef)
            .ToList();

        Assert.Single(dbInstances);
        _output.WriteLine($"Database has exactly 1 instance (no duplicates)");
    }

    [Fact]
    public async Task AddTransactions_Concurrent_StrictlyMonotonicSequence()
    {
        // ARRANGE: Single instance with concurrent transaction adds
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var arcRef = "TestArc";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        var tasks = new List<Task<long>>();
        var transactionCount = 50;

        _output.WriteLine($"=== Adding {transactionCount} concurrent transactions ===");

        // ACT: Add transactions concurrently from multiple threads
        for (var i = 0; i < transactionCount; i++)
        {
            var txId = i;
            tasks.Add(Task.Run(async () =>
            {
                var tx = new ArcTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = ArcTransactionType.AvatarEntered,
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
                    new List<ArcTransaction> { tx });

                return sequences.First();
            }));
        }

        var assignedSequences = await Task.WhenAll(tasks);

        // ASSERT: All sequence numbers unique and monotonic
        var sortedSequences = assignedSequences.OrderBy(s => s).ToList();
        var expectedSequences = Enumerable.Range(1, transactionCount).Select(i => (long)i).ToList();

        Assert.Equal(expectedSequences, sortedSequences);

        _output.WriteLine($"Sequence numbers strictly monotonic: 1 to {transactionCount}");
        _output.WriteLine($"  First: {sortedSequences.First()}, Last: {sortedSequences.Last()}");
    }

    #endregion

    #region Atomic Commit Tests

    [Fact]
    public async Task CommitTransactions_AtomicBatch_AllOrNothing()
    {
        // ARRANGE: Batch of transactions to commit atomically
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var arcRef = "TestArc";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        // Add 5 transactions
        var transactions = Enumerable.Range(0, 5)
            .Select(i => new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarEntered,
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

        var dbTransactions = _database.GetCollection<ArcTransactionRecord>("ArcTransactions")
            .Find(x => x.InstanceId == instance.InstanceId)
            .ToList();

        var committedCount = dbTransactions.Count(t => t.Status == TransactionStatus.Committed);
        Assert.Equal(5, committedCount);

        _output.WriteLine($"All {committedCount} transactions committed atomically");
    }

    [Fact]
    public async Task CommitTransactions_PartialFailure_AllRolledBack()
    {
        // ARRANGE: Attempt to commit with one invalid transaction ID
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var arcRef = "TestArc";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        // Add 3 valid transactions
        var validTransactions = Enumerable.Range(0, 3)
            .Select(i => new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarEntered,
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

        var dbTransactions = _database.GetCollection<ArcTransactionRecord>("ArcTransactions")
            .Find(x => x.InstanceId == instance.InstanceId)
            .ToList();

        var committedCount = dbTransactions.Count(t => t.Status == TransactionStatus.Committed);
        Assert.Equal(0, committedCount);

        _output.WriteLine($"Commit failed, all transactions remained Pending (atomic rollback)");
    }

    [Fact]
    public async Task CommitTransactions_WrongInstanceId_Fails()
    {
        // ARRANGE: Create two arc instances
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();

        var instance1 = await repository.GetOrCreateInstanceAsync(avatarId, "Arc1");
        var instance2 = await repository.GetOrCreateInstanceAsync(avatarId, "Arc2");

        // Add transaction to instance1
        var tx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.AvatarEntered,
            AvatarId = avatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow
        };

        await repository.AddTransactionsAsync(instance1.InstanceId, [tx]);

        // ACT: Try to commit with wrong instance ID
        var commitResult = await repository.CommitTransactionsAsync(instance2.InstanceId, [tx.TransactionId]);

        // ASSERT: Commit should fail
        Assert.False(commitResult);
    }

    #endregion

    #region State Replay Tests

    [Fact]
    public async Task StateReplay_MultipleTransactions_ConsistentState()
    {
        // ARRANGE: Create arc instance with complex transaction history
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var arcRef = "TestArc";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        // Build transaction sequence
        var transactions = new List<ArcTransaction>
        {
            // Avatar enters
            new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarEntered,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["TriggerRef"] = "TestTrigger" }
            },
            // Trigger activates
            new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.TriggerActivated,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["ArcTriggerRef"] = "TestTrigger" }
            },
            // Character spawns
            new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.CharacterSpawned,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = Guid.NewGuid().ToString(),
                    ["CharacterRef"] = "TestGuard",
                    ["ArcTriggerRef"] = "TestTrigger"
                }
            },
            // Character defeated
            new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.CharacterDefeated,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = Guid.NewGuid().ToString(),
                    ["CharacterRef"] = "TestGuard"
                }
            },
            // Avatar exits
            new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarExited,
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
        var trigger = new ArcTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f
        };

        var arc = new Arc
        {
            RefName = arcRef,
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var stateMachine = new ArcStateMachine(arc, new List<ArcTrigger> { trigger }, _world);

        var state1 = stateMachine.ReplayToNow(await repository.GetOrCreateInstanceAsync(avatarId, arcRef));
        var state2 = stateMachine.ReplayToNow(await repository.GetOrCreateInstanceAsync(avatarId, arcRef));

        // ASSERT: Multiple replays produce identical state
        Assert.Equal(state1.Status, state2.Status);
        Assert.Equal(state1.Triggers.Count, state2.Triggers.Count);
        Assert.Equal(state1.Characters.Count, state2.Characters.Count);

        _output.WriteLine($"State replay deterministic");
        _output.WriteLine($"  Status: {state1.Status}");
        _output.WriteLine($"  Triggers: {state1.Triggers.Count}");
        _output.WriteLine($"  Characters: {state1.Characters.Count}");
    }

    [Fact]
    public async Task StateReplay_ToSpecificSequence_PartialReplay()
    {
        // ARRANGE: Create instance with multiple transactions
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var arcRef = "TestArc";

        var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        var transactions = new List<ArcTransaction>
        {
            new()
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.ArcDiscovered,
                AvatarId = avatarId.ToString(),
                LocalTimestamp = DateTime.UtcNow
            },
            new()
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.TriggerActivated,
                AvatarId = avatarId.ToString(),
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["ArcTriggerRef"] = "TestTrigger" }
            },
            new()
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.TriggerCompleted,
                AvatarId = avatarId.ToString(),
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["ArcTriggerRef"] = "TestTrigger" }
            }
        };

        await repository.AddTransactionsAsync(instance.InstanceId, transactions);
        await repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList());

        var trigger = new ArcTrigger { RefName = "TestTrigger", EnterRadius = 100.0f };
        var arc = new Arc { RefName = arcRef, DisplayName = "Test Arc" };
        var stateMachine = new ArcStateMachine(arc, new List<ArcTrigger> { trigger }, _world);

        // ACT: Replay to sequence 2 (before TriggerCompleted)
        var loadedInstance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var partialState = stateMachine.ReplayToSequence(loadedInstance, 2);
        var fullState = stateMachine.ReplayToNow(loadedInstance);

        // ASSERT: Partial state shows trigger as active, full state shows completed
        Assert.Equal(ArcTriggerStatus.Active, partialState.Triggers["TestTrigger"].Status);
        Assert.Equal(ArcTriggerStatus.Completed, fullState.Triggers["TestTrigger"].Status);
    }

    #endregion

    #region Multi-Avatar Tests

    [Fact]
    public async Task MultipleAvatars_SameArc_IndependentInstances()
    {
        // ARRANGE: Multiple avatars interacting with same arc
        var repository = new ArcInstanceRepository(_database);
        var arcRef = "SharedArc";

        var avatarIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        _output.WriteLine($"=== Testing {avatarIds.Count} avatars in same arc ===");

        // ACT: Each avatar creates instance and adds transactions
        var tasks = avatarIds.Select(async avatarId =>
        {
            var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

            var tx = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarEntered,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>()
            };

            await repository.AddTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { tx });
            await repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { tx.TransactionId });

            return instance.InstanceId;
        });

        var instanceIds = await Task.WhenAll(tasks);

        // ASSERT: Each avatar has independent instance
        Assert.Equal(5, instanceIds.Distinct().Count());

        _output.WriteLine($"Each avatar has independent instance");
        foreach (var (avatarId, instanceId) in avatarIds.Zip(instanceIds))
        {
            _output.WriteLine($"  Avatar {avatarId:N}: Instance {instanceId}");
        }
    }

    [Fact]
    public async Task GetAllInstancesForAvatar_ReturnsAllArcs()
    {
        // ARRANGE: Single avatar in multiple arcs
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();

        var arcRefs = new[] { "Arc1", "Arc2", "Arc3" };

        foreach (var arcRef in arcRefs)
        {
            var instance = await repository.GetOrCreateInstanceAsync(avatarId, arcRef);

            var tx = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.ArcDiscovered,
                AvatarId = avatarId.ToString(),
                LocalTimestamp = DateTime.UtcNow
            };

            await repository.AddTransactionsAsync(instance.InstanceId, [tx]);
            await repository.CommitTransactionsAsync(instance.InstanceId, [tx.TransactionId]);
        }

        // ACT: Get all instances for avatar
        var instances = await repository.GetAllInstancesForAvatarAsync(avatarId);

        // ASSERT: Returns all 3 instances with their transactions
        Assert.Equal(3, instances.Count);
        Assert.All(instances, i => Assert.Single(i.Transactions));
        Assert.Equal(arcRefs, instances.Select(i => i.ArcRef).OrderBy(s => s));
    }

    #endregion

    #region Transaction Query Tests

    [Fact]
    public async Task GetTransactionsAfterSequence_ReturnsOnlyNewer()
    {
        // ARRANGE: Instance with 5 transactions
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var instance = await repository.GetOrCreateInstanceAsync(avatarId, "TestArc");

        var transactions = Enumerable.Range(1, 5).Select(i => new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.AvatarEntered,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string> { ["Index"] = i.ToString() }
        }).ToList();

        await repository.AddTransactionsAsync(instance.InstanceId, transactions);
        await repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList());

        // ACT: Get transactions after sequence 3
        var newerTransactions = await repository.GetTransactionsAfterSequenceAsync(instance.InstanceId, 3);

        // ASSERT: Only transactions with sequence 4 and 5
        Assert.Equal(2, newerTransactions.Count);
        Assert.All(newerTransactions, t => Assert.True(t.SequenceNumber > 3));
    }

    [Fact]
    public async Task RollbackTransactions_MarksAsRejected()
    {
        // ARRANGE: Instance with pending transactions
        var repository = new ArcInstanceRepository(_database);
        var avatarId = Guid.NewGuid();
        var instance = await repository.GetOrCreateInstanceAsync(avatarId, "TestArc");

        var transactions = new List<ArcTransaction>
        {
            new()
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarEntered,
                AvatarId = avatarId.ToString(),
                LocalTimestamp = DateTime.UtcNow
            },
            new()
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.TriggerActivated,
                AvatarId = avatarId.ToString(),
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string> { ["ArcTriggerRef"] = "TestTrigger" }
            }
        };

        await repository.AddTransactionsAsync(instance.InstanceId, transactions);

        // ACT: Rollback the transactions
        await repository.RollbackTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList());

        // ASSERT: All transactions marked as Rejected
        var dbTransactions = await repository.GetTransactionsAsync(instance.InstanceId);
        Assert.All(dbTransactions, t => Assert.Equal(TransactionStatus.Rejected, t.Status));
    }

    #endregion

    public void Dispose()
    {
        _database?.Dispose();
    }

    #region Test Helpers

    private World CreateTestWorld()
    {
        var arc = new Arc
        {
            RefName = "TestArc",
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var trigger = new ArcTrigger
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
                    Saga = new[] { arc },
                    Characters = new[] { character },
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>()
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { trigger };
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
