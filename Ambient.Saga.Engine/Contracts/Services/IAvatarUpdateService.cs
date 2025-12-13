using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Contracts.Services;

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
    /// Updates avatar block inventory after mining (adds mined blocks).
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="blocksMined">Dictionary of BlockRef -> quantity mined</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForMiningAsync(
        AvatarEntity avatar,
        Dictionary<string, int> blocksMined,
        CancellationToken ct = default);

    /// <summary>
    /// Updates avatar block/material inventory after building (removes consumed materials).
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="materialsConsumed">Dictionary of MaterialRef -> quantity consumed</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForBuildingAsync(
        AvatarEntity avatar,
        Dictionary<string, int> materialsConsumed,
        CancellationToken ct = default);

    /// <summary>
    /// Updates avatar tool condition after use.
    /// </summary>
    /// <param name="avatar">The avatar to update</param>
    /// <param name="toolRef">The tool reference</param>
    /// <param name="newCondition">The new tool condition (0.0 - 1.0)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated avatar</returns>
    Task<AvatarEntity> UpdateAvatarForToolWearAsync(
        AvatarEntity avatar,
        string toolRef,
        float newCondition,
        CancellationToken ct = default);

    /// <summary>
    /// Persists avatar to repository.
    /// </summary>
    /// <param name="avatar">The avatar to persist</param>
    /// <param name="ct">Cancellation token</param>
    Task PersistAvatarAsync(AvatarEntity avatar, CancellationToken ct = default);

    /// <summary>
    /// Gets achievement instances for an avatar.
    /// </summary>
    /// <param name="avatarId">The avatar ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of achievement instances</returns>
    Task<List<AchievementInstance>> GetAchievementInstancesAsync(Guid avatarId, CancellationToken ct = default);

    /// <summary>
    /// Updates achievement instances for an avatar.
    /// </summary>
    /// <param name="avatarId">The avatar ID</param>
    /// <param name="instances">The updated achievement instances</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateAchievementInstancesAsync(Guid avatarId, List<AchievementInstance> instances, CancellationToken ct = default);
}
