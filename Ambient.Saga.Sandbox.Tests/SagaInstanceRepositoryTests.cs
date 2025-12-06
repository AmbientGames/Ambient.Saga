using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Sandbox.Tests;

/// <summary>
/// Integration tests for SagaInstanceRepository.
/// Tests actual LiteDB persistence, CRUD operations, and transaction management.
/// </summary>
[Collection("LiteDB Tests")]
public class SagaInstanceRepositoryTests : IDisposable
{
    private readonly ILiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public SagaInstanceRepositoryTests()
    {
        _database = new LiteDatabase(":memory:");
        _repository = new SagaInstanceRepository(_database);
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    #region GetOrCreateInstanceAsync Tests

    [Fact]
    public async Task GetOrCreateInstanceAsync_NewInstance_CreatesAndReturnsInstance()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        const string sagaRef = "TestSaga";

        // Act
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Assert
        Assert.NotNull(instance);
        Assert.NotEqual(Guid.Empty, instance.InstanceId);
        Assert.Equal(sagaRef, instance.SagaRef);
        Assert.Equal(avatarId, instance.OwnerAvatarId);
        Assert.Empty(instance.Transactions);
    }

    [Fact]
    public async Task GetOrCreateInstanceAsync_ExistingInstance_ReturnsSameInstance()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        const string sagaRef = "TestSaga";

        // Act
        var instance1 = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var instance2 = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Assert
        Assert.Equal(instance1.InstanceId, instance2.InstanceId);
    }

    [Fact]
    public async Task GetOrCreateInstanceAsync_DifferentAvatars_CreatesSeparateInstances()
    {
        // Arrange
        var avatarId1 = Guid.NewGuid();
        var avatarId2 = Guid.NewGuid();
        const string sagaRef = "TestSaga";

        // Act
        var instance1 = await _repository.GetOrCreateInstanceAsync(avatarId1, sagaRef);
        var instance2 = await _repository.GetOrCreateInstanceAsync(avatarId2, sagaRef);

        // Assert
        Assert.NotEqual(instance1.InstanceId, instance2.InstanceId);
        Assert.Equal(avatarId1, instance1.OwnerAvatarId);
        Assert.Equal(avatarId2, instance2.OwnerAvatarId);
    }

    [Fact]
    public async Task GetOrCreateInstanceAsync_DifferentSagas_CreatesSeparateInstances()
    {
        // Arrange
        var avatarId = Guid.NewGuid();

        // Act
        var instance1 = await _repository.GetOrCreateInstanceAsync(avatarId, "Saga1");
        var instance2 = await _repository.GetOrCreateInstanceAsync(avatarId, "Saga2");

        // Assert
        Assert.NotEqual(instance1.InstanceId, instance2.InstanceId);
    }

    [Fact]
    public async Task GetOrCreateInstanceAsync_LoadsExistingTransactions()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        const string sagaRef = "TestSaga";

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string> { ["TriggerRef"] = "TestTrigger" }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);
        await _repository.CommitTransactionsAsync(instance.InstanceId, [transaction.TransactionId]);

        // Act - Get instance again
        var reloadedInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        // Assert
        Assert.Single(reloadedInstance.Transactions);
        Assert.Equal(transaction.TransactionId, reloadedInstance.Transactions[0].TransactionId);
    }

    #endregion

    #region GetInstanceByIdAsync Tests

    [Fact]
    public async Task GetInstanceByIdAsync_ExistingInstance_ReturnsInstance()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        // Act
        var retrieved = await _repository.GetInstanceByIdAsync(instance.InstanceId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(instance.InstanceId, retrieved.InstanceId);
    }

    [Fact]
    public async Task GetInstanceByIdAsync_NonExistentInstance_ReturnsNull()
    {
        // Act
        var result = await _repository.GetInstanceByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region AddTransactionsAsync Tests

    [Fact]
    public async Task AddTransactionsAsync_SingleTransaction_AssignsSequenceNumber()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            Data = new Dictionary<string, string> { ["CharacterRef"] = "TestCharacter" }
        };

        // Act
        var sequenceNumbers = await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);

        // Assert
        Assert.Single(sequenceNumbers);
        Assert.Equal(1, sequenceNumbers[0]);
    }

    [Fact]
    public async Task AddTransactionsAsync_MultipleTransactions_AssignsIncrementingSequenceNumbers()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transactions = new List<SagaTransaction>
        {
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.CharacterSpawned },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.TriggerActivated },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.DialogueStarted }
        };

        // Act
        var sequenceNumbers = await _repository.AddTransactionsAsync(instance.InstanceId, transactions);

        // Assert
        Assert.Equal(3, sequenceNumbers.Count);
        Assert.Equal(1, sequenceNumbers[0]);
        Assert.Equal(2, sequenceNumbers[1]);
        Assert.Equal(3, sequenceNumbers[2]);
    }

    [Fact]
    public async Task AddTransactionsAsync_ContinuesSequenceAfterExisting()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        // Add first batch
        var firstBatch = new List<SagaTransaction>
        {
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.CharacterSpawned },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.TriggerActivated }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, firstBatch);

        // Add second batch
        var secondBatch = new List<SagaTransaction>
        {
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.DialogueStarted }
        };

        // Act
        var sequenceNumbers = await _repository.AddTransactionsAsync(instance.InstanceId, secondBatch);

        // Assert
        Assert.Single(sequenceNumbers);
        Assert.Equal(3, sequenceNumbers[0]); // Should continue from 2
    }

    [Fact]
    public async Task AddTransactionsAsync_SetsStatusToPending()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };

        // Act
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);
        var transactions = await _repository.GetTransactionsAsync(instance.InstanceId);

        // Assert
        Assert.Single(transactions);
        Assert.Equal(TransactionStatus.Pending, transactions[0].Status);
    }

    [Fact]
    public async Task AddTransactionsAsync_NonExistentInstance_ThrowsException()
    {
        // Arrange
        var fakeInstanceId = Guid.NewGuid();
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _repository.AddTransactionsAsync(fakeInstanceId, [transaction]));
    }

    #endregion

    #region GetTransactionsAsync Tests

    [Fact]
    public async Task GetTransactionsAsync_ReturnsAllTransactionsInOrder()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transactions = new List<SagaTransaction>
        {
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.CharacterSpawned },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.TriggerActivated },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.DialogueStarted }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, transactions);

        // Act
        var retrieved = await _repository.GetTransactionsAsync(instance.InstanceId);

        // Assert
        Assert.Equal(3, retrieved.Count);
        Assert.True(retrieved[0].SequenceNumber < retrieved[1].SequenceNumber);
        Assert.True(retrieved[1].SequenceNumber < retrieved[2].SequenceNumber);
    }

    [Fact]
    public async Task GetTransactionsAsync_PreservesTransactionData()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            AvatarId = avatarId.ToString(),
            ClientId = "TestClient",
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "Merchant",
                ["CharacterInstanceId"] = Guid.NewGuid().ToString()
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);

        // Act
        var retrieved = await _repository.GetTransactionsAsync(instance.InstanceId);

        // Assert
        Assert.Single(retrieved);
        var tx = retrieved[0];
        Assert.Equal(transaction.TransactionId, tx.TransactionId);
        Assert.Equal(transaction.Type, tx.Type);
        Assert.Equal(transaction.AvatarId, tx.AvatarId);
        Assert.Equal(transaction.ClientId, tx.ClientId);
        Assert.Equal("Merchant", tx.Data["CharacterRef"]);
    }

    #endregion

    #region GetTransactionsAfterSequenceAsync Tests

    [Fact]
    public async Task GetTransactionsAfterSequenceAsync_ReturnsOnlyNewerTransactions()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transactions = new List<SagaTransaction>
        {
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.CharacterSpawned },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.TriggerActivated },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.DialogueStarted }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, transactions);

        // Act - Get transactions after sequence 1
        var retrieved = await _repository.GetTransactionsAfterSequenceAsync(instance.InstanceId, 1);

        // Assert
        Assert.Equal(2, retrieved.Count);
        Assert.True(retrieved.All(t => t.SequenceNumber > 1));
    }

    [Fact]
    public async Task GetTransactionsAfterSequenceAsync_NoMatchingTransactions_ReturnsEmpty()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);

        // Act - Get transactions after highest sequence
        var retrieved = await _repository.GetTransactionsAfterSequenceAsync(instance.InstanceId, 100);

        // Assert
        Assert.Empty(retrieved);
    }

    #endregion

    #region CommitTransactionsAsync Tests

    [Fact]
    public async Task CommitTransactionsAsync_PendingTransactions_CommitsSuccessfully()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);

        // Act
        var result = await _repository.CommitTransactionsAsync(instance.InstanceId, [transaction.TransactionId]);

        // Assert
        Assert.True(result);

        var transactions = await _repository.GetTransactionsAsync(instance.InstanceId);
        Assert.Single(transactions);
        Assert.Equal(TransactionStatus.Committed, transactions[0].Status);
        Assert.NotNull(transactions[0].ServerTimestamp);
    }

    [Fact]
    public async Task CommitTransactionsAsync_MultipleTransactions_CommitsAll()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transactions = new List<SagaTransaction>
        {
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.CharacterSpawned },
            new() { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.TriggerActivated }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, transactions);

        // Act
        var transactionIds = transactions.Select(t => t.TransactionId).ToList();
        var result = await _repository.CommitTransactionsAsync(instance.InstanceId, transactionIds);

        // Assert
        Assert.True(result);

        var retrieved = await _repository.GetTransactionsAsync(instance.InstanceId);
        Assert.All(retrieved, t => Assert.Equal(TransactionStatus.Committed, t.Status));
    }

    [Fact]
    public async Task CommitTransactionsAsync_NonExistentTransaction_ReturnsFalse()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        // Act
        var result = await _repository.CommitTransactionsAsync(instance.InstanceId, [Guid.NewGuid()]);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CommitTransactionsAsync_AlreadyCommitted_ReturnsFalse()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);
        await _repository.CommitTransactionsAsync(instance.InstanceId, [transaction.TransactionId]);

        // Act - Try to commit again
        var result = await _repository.CommitTransactionsAsync(instance.InstanceId, [transaction.TransactionId]);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CommitTransactionsAsync_WrongInstance_ReturnsFalse()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance1 = await _repository.GetOrCreateInstanceAsync(avatarId, "Saga1");
        var instance2 = await _repository.GetOrCreateInstanceAsync(avatarId, "Saga2");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };
        await _repository.AddTransactionsAsync(instance1.InstanceId, [transaction]);

        // Act - Try to commit with wrong instance ID
        var result = await _repository.CommitTransactionsAsync(instance2.InstanceId, [transaction.TransactionId]);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RollbackTransactionsAsync Tests

    [Fact]
    public async Task RollbackTransactionsAsync_PendingTransaction_MarksAsRejected()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);

        // Act
        await _repository.RollbackTransactionsAsync(instance.InstanceId, [transaction.TransactionId]);

        // Assert
        var transactions = await _repository.GetTransactionsAsync(instance.InstanceId);
        Assert.Single(transactions);
        Assert.Equal(TransactionStatus.Rejected, transactions[0].Status);
    }

    [Fact]
    public async Task RollbackTransactionsAsync_NonExistentTransaction_DoesNotThrow()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        // Act & Assert - Should not throw
        await _repository.RollbackTransactionsAsync(instance.InstanceId, [Guid.NewGuid()]);
    }

    #endregion

    #region GetAllInstancesForAvatarAsync Tests

    [Fact]
    public async Task GetAllInstancesForAvatarAsync_ReturnsAllSagasForAvatar()
    {
        // Arrange
        var avatarId = Guid.NewGuid();

        await _repository.GetOrCreateInstanceAsync(avatarId, "Saga1");
        await _repository.GetOrCreateInstanceAsync(avatarId, "Saga2");
        await _repository.GetOrCreateInstanceAsync(avatarId, "Saga3");

        // Act
        var instances = await _repository.GetAllInstancesForAvatarAsync(avatarId);

        // Assert
        Assert.Equal(3, instances.Count);
        Assert.All(instances, i => Assert.Equal(avatarId, i.OwnerAvatarId));
    }

    [Fact]
    public async Task GetAllInstancesForAvatarAsync_DoesNotReturnOtherAvatarsInstances()
    {
        // Arrange
        var avatarId1 = Guid.NewGuid();
        var avatarId2 = Guid.NewGuid();

        await _repository.GetOrCreateInstanceAsync(avatarId1, "Saga1");
        await _repository.GetOrCreateInstanceAsync(avatarId2, "Saga1");
        await _repository.GetOrCreateInstanceAsync(avatarId2, "Saga2");

        // Act
        var instances = await _repository.GetAllInstancesForAvatarAsync(avatarId1);

        // Assert
        Assert.Single(instances);
        Assert.Equal(avatarId1, instances[0].OwnerAvatarId);
    }

    [Fact]
    public async Task GetAllInstancesForAvatarAsync_LoadsTransactionsForEachInstance()
    {
        // Arrange
        var avatarId = Guid.NewGuid();

        var instance1 = await _repository.GetOrCreateInstanceAsync(avatarId, "Saga1");
        var instance2 = await _repository.GetOrCreateInstanceAsync(avatarId, "Saga2");

        var tx1 = new SagaTransaction { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.CharacterSpawned };
        var tx2 = new SagaTransaction { TransactionId = Guid.NewGuid(), Type = SagaTransactionType.TriggerActivated };

        await _repository.AddTransactionsAsync(instance1.InstanceId, [tx1]);
        await _repository.AddTransactionsAsync(instance2.InstanceId, [tx2]);

        await _repository.CommitTransactionsAsync(instance1.InstanceId, [tx1.TransactionId]);
        await _repository.CommitTransactionsAsync(instance2.InstanceId, [tx2.TransactionId]);

        // Act
        var instances = await _repository.GetAllInstancesForAvatarAsync(avatarId);

        // Assert
        Assert.Equal(2, instances.Count);
        Assert.All(instances, i => Assert.Single(i.Transactions));
    }

    [Fact]
    public async Task GetAllInstancesForAvatarAsync_NoInstances_ReturnsEmptyList()
    {
        // Act
        var instances = await _repository.GetAllInstancesForAvatarAsync(Guid.NewGuid());

        // Assert
        Assert.Empty(instances);
    }

    #endregion

    #region Transaction Data Round-Trip Tests

    [Fact]
    public async Task TransactionData_RoundTrips_ComplexData()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var characterInstanceId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga");

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ItemTraded,
            AvatarId = avatarId.ToString(),
            ClientId = "TestClient-123",
            LocalTimestamp = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            Data = new Dictionary<string, string>
            {
                ["ItemRef"] = "HealthPotion",
                ["Quantity"] = "5",
                ["IsBuying"] = "True",
                ["TotalPrice"] = "250",
                ["CharacterInstanceId"] = characterInstanceId.ToString()
            }
        };
        await _repository.AddTransactionsAsync(instance.InstanceId, [transaction]);
        await _repository.CommitTransactionsAsync(instance.InstanceId, [transaction.TransactionId]);

        // Act
        var retrieved = await _repository.GetTransactionsAsync(instance.InstanceId);

        // Assert
        Assert.Single(retrieved);
        var tx = retrieved[0];
        Assert.Equal("HealthPotion", tx.Data["ItemRef"]);
        Assert.Equal("5", tx.Data["Quantity"]);
        Assert.Equal("True", tx.Data["IsBuying"]);
        Assert.Equal("250", tx.Data["TotalPrice"]);
        Assert.Equal(characterInstanceId.ToString(), tx.Data["CharacterInstanceId"]);
    }

    #endregion
}
