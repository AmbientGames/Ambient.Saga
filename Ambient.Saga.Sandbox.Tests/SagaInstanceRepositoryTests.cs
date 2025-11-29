using LiteDB;

namespace Ambient.Saga.Sandbox.Tests;

/// <summary>
/// Integration tests for SagaInstanceRepository.
/// Tests actual LiteDB persistence, CRUD operations, and transaction management.
///
/// TODO: These tests need to be rewritten for the new CQRS repository.
/// The old SagaInstanceRepository_Old used synchronous methods and different patterns.
/// The new repository (Ambient.Saga.Engine.Infrastructure.Persistence.SagaInstanceRepository) uses:
/// - Async methods (GetOrCreateInstanceAsync, AddTransactionsAsync, etc.)
/// - Separate collections for instances and transactions
/// - Transaction records with manual InstanceId linking
///
/// When rewriting:
/// 1. Test async operations with proper await
/// 2. Test the new two-collection architecture
/// 3. Test transaction sequence numbering
/// 4. Test GetAllInstancesForAvatarAsync for multiplayer scenarios
///
/// For now, core CQRS functionality is tested in:
/// - Ambient.Application.Tests (command/query integration tests)
/// - Ambient.Domain.Tests (state machine and transaction replay)
/// </summary>
[Collection("LiteDB Tests")]
public class SagaInstanceRepositoryTests : IDisposable
{
    private readonly ILiteDatabase _database;

    public SagaInstanceRepositoryTests()
    {
        // Create temporary in-memory database for each test
        _database = new LiteDatabase(":memory:");
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    // All tests removed - will be rewritten for CQRS async repository
    // See Ambient.Saga.Engine.Infrastructure.Persistence.SagaInstanceRepository for new implementation
}
