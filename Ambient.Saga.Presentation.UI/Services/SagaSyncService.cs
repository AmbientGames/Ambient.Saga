using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Presentation.UI.Services;

/// <summary>
/// Service for synchronizing Saga instances between local database and server.
/// Handles conflict resolution, merging, and optimistic concurrency.
///
/// NOTE: This is currently a STUB for future multiplayer functionality.
/// Not used in the current single-player implementation.
///
/// Sync Strategy:
/// - Client plays offline, accumulates pending transactions
/// - When online, send pending transactions to server
/// - Server validates and either commits or rejects
/// - Client merges server's confirmed state with local predictions
/// - Conflicts resolved via strategy (server-wins, timestamp-ordering, etc.)
/// </summary>
public class SagaSyncService
{
    private readonly ISagaInstanceRepository _repository;
    private readonly SagaStateMachine _stateMachine;
    // TODO: Add API client when server is implemented
    // private readonly ISagaApiClient _apiClient;

    public SagaSyncService(ISagaInstanceRepository repository, SagaStateMachine stateMachine)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
    }

    // ===== Sync Operations =====

    /// <summary>
    /// Synchronizes a single Saga instance with the server.
    /// Called when player comes online or periodically while online.
    /// </summary>
    public async Task<SyncResult> SyncInstance(Guid instanceId)
    {
        var localInstance = await _repository.GetInstanceByIdAsync(instanceId, CancellationToken.None);
        if (localInstance == null)
            return SyncResult.Failed($"Instance {instanceId} not found");

        // LocalOnly instances never sync
        if (localInstance.InstanceType == SagaInstanceType.LocalOnly)
            return SyncResult.Skipped("LocalOnly instance");

        // Check if there are pending transactions to send
        var pendingTransactions = localInstance.Transactions
            .Where(t => t.Status == TransactionStatus.Pending)
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        if (!pendingTransactions.Any())
        {
            // No local changes, but check for server updates
            return await PullServerUpdates(localInstance);
        }

        // Send pending transactions to server
        return await PushLocalChanges(localInstance, pendingTransactions);
    }

    /// <summary>
    /// Synchronizes all instances that need syncing.
    /// Called periodically when online.
    ///
    /// TODO: Implement with new ISagaInstanceRepository interface
    /// Need to add GetInstancesNeedingSync or iterate by avatar
    /// </summary>
    public async Task<List<SyncResult>> SyncAll()
    {
        // TODO: New repository interface doesn't have GetInstancesNeedingSync
        // Will need to refactor when implementing multiplayer sync
        await Task.CompletedTask;
        return new List<SyncResult>
        {
            SyncResult.Skipped("Not implemented - multiplayer sync pending")
        };
    }

    // ===== Push/Pull Operations =====

    private async Task<SyncResult> PushLocalChanges(SagaInstance localInstance, List<SagaTransaction> pendingTransactions)
    {
        // TODO: Implement when server API is available
        // For now, just mark as committed locally (offline mode)

        // NOTE: New repository interface doesn't have Update method
        // Transaction status updates will need different approach when implementing multiplayer
        foreach (var tx in pendingTransactions)
        {
            tx.Status = TransactionStatus.Committed;
            tx.ServerTimestamp = DateTime.UtcNow;  // Would come from server
        }

        // TODO: Need to implement status update mechanism in ISagaInstanceRepository
        await Task.CompletedTask;

        return SyncResult.Succeeded($"Pushed {pendingTransactions.Count} transactions (stub)");
    }

    private async Task<SyncResult> PullServerUpdates(SagaInstance localInstance)
    {
        // TODO: Implement when server API is available
        // For now, no-op (offline mode)

        return await Task.FromResult(SyncResult.Skipped("No server updates"));
    }

    // ===== Conflict Resolution =====

    /// <summary>
    /// Merges server transactions with local transactions.
    /// Handles conflicts based on instance type and strategy.
    /// </summary>
    private async Task<SyncResult> MergeTransactions(
        SagaInstance localInstance,
        List<SagaTransaction> localPending,
        List<SagaTransaction> serverTransactions)
    {
        var strategy = localInstance.InstanceType == SagaInstanceType.SinglePlayer
            ? ConflictStrategy.ServerWins
            : ConflictStrategy.TimestampOrdering;

        return strategy switch
        {
            ConflictStrategy.ServerWins => MergeServerWins(localInstance, localPending, serverTransactions),
            ConflictStrategy.TimestampOrdering => MergeTimestampOrdering(localInstance, localPending, serverTransactions),
            _ => SyncResult.Failed($"Unknown conflict strategy: {strategy}")
        };
    }

    /// <summary>
    /// Server-wins strategy: Server state is canonical.
    /// Replays local transactions on top of server state.
    /// Used for single-player instances.
    /// </summary>
    private SyncResult MergeServerWins(
        SagaInstance localInstance,
        List<SagaTransaction> localPending,
        List<SagaTransaction> serverTransactions)
    {
        var merged = new List<SagaTransaction>();
        var reversals = 0;

        // Start with all server transactions (these are canonical)
        merged.AddRange(serverTransactions);

        // Replay local transactions on top of server state
        var serverState = _stateMachine.Replay(serverTransactions);

        foreach (var localTx in localPending)
        {
            if (IsValidTransaction(serverState, localTx))
            {
                // Transaction is still valid after server state
                localTx.Status = TransactionStatus.Committed;
                localTx.MergeStrategy = "Replayed after server";
                merged.Add(localTx);

                // Update state with this transaction
                _stateMachine.ApplyTransaction(serverState, localTx);
            }
            else
            {
                // Transaction conflicts with server state - reverse it
                localTx.Status = TransactionStatus.Reversed;
                localTx.ReversalReason = "Conflict with server state";

                var reversal = CreateReversalTransaction(localTx);
                merged.Add(reversal);
                reversals++;
            }
        }

        // Replace local instance transactions with merged result
        localInstance.Transactions = merged;

        // TODO: New repository doesn't support replacing transaction list
        // Will need different merge strategy when implementing multiplayer
        // _repository.Update(localInstance);

        return SyncResult.Succeeded($"Merged server state (stub), {reversals} conflicts reversed");
    }

    /// <summary>
    /// Timestamp-ordering strategy: Merge by server timestamp.
    /// Used for multiplayer instances where multiple players' actions interleave.
    /// </summary>
    private SyncResult MergeTimestampOrdering(
        SagaInstance localInstance,
        List<SagaTransaction> localPending,
        List<SagaTransaction> serverTransactions)
    {
        var merged = new List<SagaTransaction>();
        var reversals = 0;

        // Merge all transactions by timestamp
        var allTransactions = localPending.Concat(serverTransactions)
            .OrderBy(tx => tx.GetCanonicalTimestamp())
            .ToList();

        // Replay in timestamp order, validating each
        var state = _stateMachine.CreateInitialState();

        foreach (var tx in allTransactions)
        {
            if (IsValidTransaction(state, tx))
            {
                tx.Status = TransactionStatus.Committed;
                tx.MergeStrategy = "Timestamp ordering";
                merged.Add(tx);

                _stateMachine.ApplyTransaction(state, tx);
            }
            else
            {
                // Conflict - create reversal
                tx.Status = TransactionStatus.Reversed;
                tx.ReversalReason = "Conflict with concurrent action";

                var reversal = CreateReversalTransaction(tx);
                merged.Add(reversal);
                reversals++;
            }
        }

        localInstance.Transactions = merged;

        // TODO: New repository doesn't support replacing transaction list
        // Will need different merge strategy when implementing multiplayer
        // _repository.Update(localInstance);

        return SyncResult.Succeeded($"Merged by timestamp (stub), {reversals} conflicts reversed");
    }

    // ===== Validation =====

    /// <summary>
    /// Validates whether a transaction is valid given the current state.
    /// Example validations:
    /// - Can't damage a character that's already dead
    /// - Can't activate a trigger that's completed
    /// - Can't loot a character that's already been looted
    /// </summary>
    private bool IsValidTransaction(SagaState state, SagaTransaction tx)
    {
        switch (tx.Type)
        {
            case SagaTransactionType.CharacterDamaged:
                var characterId = tx.GetData<Guid>("CharacterInstanceId");
                if (state.Characters.TryGetValue(characterId.ToString(), out var character))
                {
                    return character.IsAlive;  // Can't damage dead characters
                }
                return false;

            case SagaTransactionType.CharacterDefeated:
                characterId = tx.GetData<Guid>("CharacterInstanceId");
                if (state.Characters.TryGetValue(characterId.ToString(), out character))
                {
                    return character.IsAlive;  // Can't defeat already-dead characters
                }
                return false;

            case SagaTransactionType.TriggerActivated:
                var triggerRef = tx.GetData<string>("SagaTriggerRef");
                if (!string.IsNullOrEmpty(triggerRef) && state.Triggers.TryGetValue(triggerRef, out var trigger))
                {
                    return trigger.Status != SagaTriggerStatus.Completed;  // Can't activate completed triggers
                }
                return false;

            // Add more validation rules as needed
            default:
                return true;  // Unknown transaction types are assumed valid
        }
    }

    /// <summary>
    /// Creates a compensating (reversal) transaction.
    /// </summary>
    private SagaTransaction CreateReversalTransaction(SagaTransaction originalTx)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.TransactionReversed,
            ReversesTransactionId = originalTx.TransactionId,
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow,
            ServerTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["OriginalType"] = originalTx.Type.ToString(),
                ["OriginalDataJson"] = System.Text.Json.JsonSerializer.Serialize(originalTx.Data)
            }
        };
    }
}

// ===== Supporting Types =====

public enum ConflictStrategy
{
    ServerWins,          // Server state is canonical (single-player)
    TimestampOrdering    // Merge by timestamp (multiplayer)
}

public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TransactionsSynced { get; set; }
    public int ConflictsResolved { get; set; }

    public static SyncResult Succeeded(string message) => new() { Success = true, Message = message };
    public static SyncResult Failed(string message) => new() { Success = false, Message = message };
    public static SyncResult Skipped(string message) => new() { Success = true, Message = message };
}
