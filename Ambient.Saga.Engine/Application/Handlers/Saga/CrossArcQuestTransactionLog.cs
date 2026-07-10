using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Builds the avatar's CROSS-ARC committed transaction log for quest objective
/// evaluation.
///
/// Quest objectives are intentionally cross-arc: a quest given in one arc is
/// satisfied by the triggers/tokens/dialogue/defeats that land in whichever arc
/// they OCCUR in, not the quest's owning arc (Trail worlds like EverestTrail send
/// you from the arc that gave the quest to a trigger/NPC/boss in another arc). The
/// satisfying <see cref="SagaTransaction"/> therefore lives in a DIFFERENT
/// <see cref="SagaInstance"/> than the one that accepted the quest. Evaluating an
/// objective against only the owning instance's log leaves those objectives stuck
/// at zero forever. Dialogue GATING already reads cross-arc state (via
/// IAvatarProgressRepository); this makes objective EVALUATION equally cross-arc.
///
/// The counters and <see cref="QuestProgressEvaluator.ScopeToCurrentAcceptance"/>
/// are unchanged — they simply receive this unioned log instead of one arc's.
/// </summary>
internal static class CrossArcQuestTransactionLog
{
    /// <summary>
    /// Loads every Saga instance for the avatar and merges their committed
    /// transactions into one chronological, coherently-sequenced log.
    /// </summary>
    public static async Task<List<SagaTransaction>> BuildAsync(
        Guid avatarId,
        ISagaInstanceRepository instanceRepository,
        CancellationToken ct)
    {
        var instances = await instanceRepository.GetAllInstancesForAvatarAsync(avatarId, ct);
        return Build(avatarId, instances);
    }

    /// <summary>
    /// Merges the committed transactions of the given instances into one log.
    /// </summary>
    /// <remarks>
    /// Two things make a naive union unsafe, and both are handled here:
    ///
    /// 1. <b>Author filtering.</b> Owned single-player instances only ever hold this
    ///    avatar's own transactions or system (null-author) ones — both are already
    ///    counted today, so single-arc quests are unaffected. But
    ///    <c>GetAllInstancesForAvatarAsync</c> also returns SHARED multiplayer
    ///    instances (server-sourced shops/geocaches), which can hold OTHER avatars'
    ///    transactions. Those are excluded so a peer's action on a shared arc can't
    ///    satisfy this avatar's objective. Null-author transactions are kept, which
    ///    preserves the exact single-arc counting behavior.
    ///
    /// 2. <b>Re-sequencing.</b> <see cref="SagaTransaction.SequenceNumber"/> is a
    ///    PER-INSTANCE counter (each arc numbers from 1), so numbers collide across
    ///    arcs. <see cref="QuestProgressEvaluator.ScopeToCurrentAcceptance"/> scopes
    ///    by sequence number, so feeding it a raw union would corrupt the scope. We
    ///    merge by canonical timestamp (server clock when present, else local — the
    ///    documented ordering key) and reassign a single monotonic sequence over the
    ///    merged stream. Within any one instance this reproduces the original order
    ///    (sequence and commit timestamp are assigned together, in causal order), so
    ///    the acceptance-scoping logic behaves identically for single-arc quests and
    ///    correctly includes later cross-arc transactions for Trail quests.
    /// </remarks>
    public static List<SagaTransaction> Build(Guid avatarId, IEnumerable<SagaInstance> instances)
    {
        var avatarIdString = avatarId.ToString();

        // OrderBy is a stable sort: transactions sharing a timestamp keep their input
        // order, which (because each instance's committed list is already sequence-
        // ordered) preserves per-instance ordering under ties.
        var ordered = instances
            .SelectMany(instance => instance.GetCommittedTransactions())
            .Where(t => string.IsNullOrEmpty(t.AvatarId) || t.AvatarId == avatarIdString)
            .OrderBy(t => t.GetCanonicalTimestamp())
            .ToList();

        var merged = new List<SagaTransaction>(ordered.Count);
        long sequence = 1;
        foreach (var source in ordered)
        {
            merged.Add(CopyWithSequence(source, sequence++));
        }

        return merged;
    }

    /// <summary>
    /// Shallow copy that only overrides <see cref="SagaTransaction.SequenceNumber"/>.
    /// Copying (rather than mutating in place) keeps the re-sequencing from leaking
    /// into any instance the caller still holds; the evaluator only reads
    /// <see cref="SagaTransaction.Data"/>, so sharing that dictionary reference is safe.
    /// </summary>
    private static SagaTransaction CopyWithSequence(SagaTransaction source, long sequenceNumber) => new()
    {
        TransactionId = source.TransactionId,
        SequenceNumber = sequenceNumber,
        AvatarId = source.AvatarId,
        LocalTimestamp = source.LocalTimestamp,
        ServerTimestamp = source.ServerTimestamp,
        Status = source.Status,
        SyncState = source.SyncState,
        Type = source.Type,
        ExtensionTypeName = source.ExtensionTypeName,
        Data = source.Data,
        ReversesTransactionId = source.ReversesTransactionId,
        ReversalReason = source.ReversalReason
    };
}
