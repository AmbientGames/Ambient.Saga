using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Applies QuestTokenAwarded transactions to every other saga instance for the avatar
/// whose triggers require the token. Gates in other arcs then unlock naturally on replay.
/// </summary>
internal static class QuestTokenFanOut
{
    public static async Task FanOutAsync(
        Guid avatarId,
        string originatingSagaRef,
        IEnumerable<string> tokenRefs,
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world,
        CancellationToken ct)
    {
        var tokenList = tokenRefs.Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();
        if (tokenList.Count == 0) return;

        var instances = await instanceRepository.GetAllInstancesForAvatarAsync(avatarId, ct);
        foreach (var targetInstance in instances)
        {
            if (targetInstance.SagaRef == originatingSagaRef) continue;

            if (!world.SagaTriggersLookup.TryGetValue(targetInstance.SagaRef, out var targetTriggers))
                continue;

            var relevantTokens = tokenList
                .Where(tokenRef => targetTriggers.Any(t =>
                    t.RequiresQuestTokenRef != null && Array.IndexOf(t.RequiresQuestTokenRef, tokenRef) >= 0))
                .ToList();

            if (relevantTokens.Count == 0) continue;

            var fanOutTxs = relevantTokens.Select(tokenRef => new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestTokenAwarded,
                AvatarId = avatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestTokenRef] = tokenRef,
                    [TransactionDataKeys.Reason] = $"Fan-out from saga '{originatingSagaRef}'"
                }
            }).ToList();

            await instanceRepository.AddAndCommitTransactionsAsync(targetInstance.InstanceId, fanOutTxs, ct);
            await readModelRepository.InvalidateCacheAsync(avatarId, targetInstance.SagaRef, ct);
        }
    }
}
