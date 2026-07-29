using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Locates the arc instance that actually owns an active quest for an avatar.
///
/// A quest is owned by whichever arc accepted it — not necessarily the arc the avatar
/// is currently interacting with. A dialogue action like CompleteQuest can be attached
/// to an NPC in a completely different Arc than the one that issued the quest (e.g.
/// accept ReachThePole at EquatorialBaseCamp, complete it at NorthPoleStation). This
/// resolver lets the matching handler find the quest's real home before acting on it.
/// </summary>
internal static class QuestInstanceLocator
{
    /// <summary>
    /// Returns the <see cref="ArcInstance"/> whose replayed state lists the quest in
    /// <see cref="ArcState.ActiveQuests"/>. Prefers the caller's hinted arc if the
    /// quest is there; otherwise scans the avatar's other instances.
    /// Returns null when no instance has the quest active.
    /// </summary>
    public static async Task<ArcInstance?> ResolveActiveQuestInstanceAsync(
        Guid avatarId,
        string questRef,
        string preferredArcRef,
        IArcInstanceRepository instanceRepository,
        IWorld world,
        CancellationToken ct)
    {
        if (await HasActiveQuestAsync(instanceRepository, world, avatarId, preferredArcRef, questRef, ct) is { } preferred)
        {
            return preferred;
        }

        var all = await instanceRepository.GetAllInstancesForAvatarAsync(avatarId, ct);
        foreach (var candidate in all)
        {
            if (candidate.ArcRef == preferredArcRef)
                continue;

            var loaded = await instanceRepository.GetOrCreateInstanceAsync(avatarId, candidate.ArcRef, ct);
            if (TryReplayState(loaded, candidate.ArcRef, world) is { } state
                && state.ActiveQuests.ContainsKey(questRef))
            {
                return loaded;
            }
        }

        return null;
    }

    private static async Task<ArcInstance?> HasActiveQuestAsync(
        IArcInstanceRepository instanceRepository,
        IWorld world,
        Guid avatarId,
        string arcRef,
        string questRef,
        CancellationToken ct)
    {
        var instance = await instanceRepository.GetOrCreateInstanceAsync(avatarId, arcRef, ct);
        var state = TryReplayState(instance, arcRef, world);
        return state != null && state.ActiveQuests.ContainsKey(questRef) ? instance : null;
    }

    /// <summary>
    /// Strips the dev-spawn suffix ("RealArcRef__DEV__uniqueid" → "RealArcRef") for
    /// template lookups. Instance operations keep the full ref.
    /// </summary>
    internal static string StripDevSuffix(string arcRef)
    {
        const string devSuffix = "__DEV__";
        return arcRef.Contains(devSuffix)
            ? arcRef.Substring(0, arcRef.IndexOf(devSuffix, StringComparison.Ordinal))
            : arcRef;
    }

    private static ArcState? TryReplayState(ArcInstance instance, string arcRef, IWorld world)
    {
        // Dev arc instances ("RealArcRef__DEV__uniqueid") replay against the real template
        var arcRefForLookup = StripDevSuffix(arcRef);

        if (!world.ArcLookup.TryGetValue(arcRefForLookup, out var template))
            return null;
        if (!world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var triggers))
            return null;

        var stateMachine = new ArcStateMachine(template, triggers, world);
        return stateMachine.ReplayToNow(instance);
    }
}
