using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Locates the Saga instance that actually owns an active quest for an avatar.
///
/// A quest is owned by whichever Saga accepted it — not necessarily the Saga the avatar
/// is currently interacting with. A dialogue action like CompleteQuest can be attached
/// to an NPC in a completely different Saga than the one that issued the quest (e.g.
/// accept ReachThePole at EquatorialBaseCamp, complete it at NorthPoleStation). This
/// resolver lets the matching handler find the quest's real home before acting on it.
/// </summary>
internal static class QuestInstanceLocator
{
    /// <summary>
    /// Returns the <see cref="SagaInstance"/> whose replayed state lists the quest in
    /// <see cref="SagaState.ActiveQuests"/>. Prefers the caller's hinted saga if the
    /// quest is there; otherwise scans the avatar's other instances.
    /// Returns null when no instance has the quest active.
    /// </summary>
    public static async Task<SagaInstance?> ResolveActiveQuestInstanceAsync(
        Guid avatarId,
        string questRef,
        string preferredSagaRef,
        ISagaInstanceRepository instanceRepository,
        IWorld world,
        CancellationToken ct)
    {
        if (await HasActiveQuestAsync(instanceRepository, world, avatarId, preferredSagaRef, questRef, ct) is { } preferred)
        {
            return preferred;
        }

        var all = await instanceRepository.GetAllInstancesForAvatarAsync(avatarId, ct);
        foreach (var candidate in all)
        {
            if (candidate.SagaRef == preferredSagaRef)
                continue;

            var loaded = await instanceRepository.GetOrCreateInstanceAsync(avatarId, candidate.SagaRef, ct);
            if (TryReplayState(loaded, candidate.SagaRef, world) is { } state
                && state.ActiveQuests.ContainsKey(questRef))
            {
                return loaded;
            }
        }

        return null;
    }

    private static async Task<SagaInstance?> HasActiveQuestAsync(
        ISagaInstanceRepository instanceRepository,
        IWorld world,
        Guid avatarId,
        string sagaRef,
        string questRef,
        CancellationToken ct)
    {
        var instance = await instanceRepository.GetOrCreateInstanceAsync(avatarId, sagaRef, ct);
        var state = TryReplayState(instance, sagaRef, world);
        return state != null && state.ActiveQuests.ContainsKey(questRef) ? instance : null;
    }

    /// <summary>
    /// Strips the dev-spawn suffix ("RealSagaRef__DEV__uniqueid" → "RealSagaRef") for
    /// template lookups. Instance operations keep the full ref.
    /// </summary>
    internal static string StripDevSuffix(string sagaRef)
    {
        const string devSuffix = "__DEV__";
        return sagaRef.Contains(devSuffix)
            ? sagaRef.Substring(0, sagaRef.IndexOf(devSuffix, StringComparison.Ordinal))
            : sagaRef;
    }

    private static SagaState? TryReplayState(SagaInstance instance, string sagaRef, IWorld world)
    {
        // Dev saga instances ("RealSagaRef__DEV__uniqueid") replay against the real template
        var sagaRefForLookup = StripDevSuffix(sagaRef);

        if (!world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var template))
            return null;
        if (!world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var triggers))
            return null;

        var stateMachine = new SagaStateMachine(template, triggers, world);
        return stateMachine.ReplayToNow(instance);
    }
}
