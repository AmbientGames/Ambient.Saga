using Ambient.Domain.Entities;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Contracts.Services;

/// <summary>
/// Service for updating avatar state based on Saga transactions.
/// Handles avatar state mutations and persistence after Saga events.
/// </summary>
public interface IAvatarUpdateService
{
    /// <summary>
    /// Updates avatar inventory and credits based on a trade transaction.
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="sagaInstance">The Saga instance containing trade transactions</param>
    /// <param name="tradeTransactionId">The ID of the ItemTraded transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForTradeAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid tradeTransactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates avatar health, energy, and equipment based on battle transactions.
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="sagaInstance">The Saga instance containing battle transactions</param>
    /// <param name="battleStartedTransactionId">The ID of the BattleStarted transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForBattleAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid battleStartedTransactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates avatar inventory based on loot transaction.
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="sagaInstance">The Saga instance containing loot transactions</param>
    /// <param name="lootTransactionId">The ID of the LootAwarded transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForLootAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid lootTransactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a quest token to avatar's capabilities.
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="questTokenRef">The quest token reference to add</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> AddQuestTokenAsync(
        AvatarEntity avatar,
        string questTokenRef,
        CancellationToken ct = default);

    /// <summary>
    /// Updates avatar stats based on an EffectApplied transaction.
    /// Applies stat bonuses like health, stamina, strength, defense, etc.
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="sagaInstance">The Saga instance containing effect transactions</param>
    /// <param name="effectTransactionId">The ID of the EffectApplied transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForEffectsAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid effectTransactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Persists avatar to repository.
    /// </summary>
    /// <param name="avatar">The avatar to persist</param>
    /// <param name="ct">Cancellation token</param>
    Task PersistAvatarAsync(AvatarEntity avatar, CancellationToken ct = default);
}
