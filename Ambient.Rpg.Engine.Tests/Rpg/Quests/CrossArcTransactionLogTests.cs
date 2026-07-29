using Ambient.Rpg.Engine.Application.Handlers.Arcs;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Xunit;

namespace Ambient.Rpg.Engine.Tests.Rpg.Quests;

/// <summary>
/// Unit tests for <see cref="CrossArcQuestTransactionLog"/> — the merge that feeds
/// quest objective evaluation the avatar's WHOLE committed history instead of one
/// arc's (the 90North bug class: a quest accepted in one arc, satisfied by
/// tokens/triggers landing in another arc, sat at zero forever).
///
/// The two invariants documented on Build():
/// 1. Peer-author exclusion — shared multiplayer instances can hold OTHER avatars'
///    transactions; those must never satisfy this avatar's objectives. Null-author
///    (system) transactions stay in.
/// 2. Canonical-timestamp re-sequencing — per-instance sequence numbers collide
///    across arcs, and acceptance scoping is sequence-based, so the merged stream
///    gets ONE monotonic sequence assigned in canonical-timestamp order
///    (server timestamp when present, else local), stable within an instance.
/// </summary>
public class CrossArcTransactionLogTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _avatarId = Guid.NewGuid();
    private readonly Guid _peerId = Guid.NewGuid();

    private static ArcTransaction Tx(
        Guid? avatarId,
        long sequenceNumber,
        DateTime localTimestamp,
        DateTime? serverTimestamp = null,
        ArcTransactionType type = ArcTransactionType.TriggerActivated,
        TransactionStatus status = TransactionStatus.Committed,
        string? marker = null)
    {
        var tx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            SequenceNumber = sequenceNumber,
            AvatarId = avatarId?.ToString(),
            LocalTimestamp = localTimestamp,
            ServerTimestamp = serverTimestamp,
            Status = status,
            Type = type
        };
        if (marker != null)
        {
            tx.Data["Marker"] = marker;
        }
        return tx;
    }

    private static ArcInstance Instance(string arcRef, params ArcTransaction[] transactions) => new()
    {
        ArcRef = arcRef,
        Transactions = transactions.ToList()
    };

    // ===== 1. Peer-author exclusion =====

    [Fact]
    public void Build_ExcludesPeerTransactions_KeepsOwnAndSystem()
    {
        var shared = Instance("shared-shop",
            Tx(_avatarId, 1, BaseTime, marker: "mine"),
            Tx(_peerId, 2, BaseTime.AddSeconds(1), marker: "peer"),
            Tx(null, 3, BaseTime.AddSeconds(2), marker: "system"));

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { shared });

        Assert.Equal(2, merged.Count);
        Assert.Equal(new[] { "mine", "system" }, merged.Select(t => t.Data["Marker"]));
        Assert.DoesNotContain(merged, t => t.AvatarId == _peerId.ToString());
    }

    [Fact]
    public void Build_PeerTokenAwardOnSharedArc_CannotSatisfyThisAvatar()
    {
        // The exact 90North failure shape inverted: a PEER earns a quest token on a
        // shared arc — the merged log used for THIS avatar's objective evaluation
        // must not contain it, or the peer would complete this avatar's quest.
        var shared = Instance("shared-arc",
            Tx(_peerId, 1, BaseTime, type: ArcTransactionType.QuestTokenAwarded, marker: "peer-token"));
        var own = Instance("own-arc",
            Tx(_avatarId, 1, BaseTime.AddSeconds(5), type: ArcTransactionType.QuestAccepted, marker: "my-accept"));

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { shared, own });

        var only = Assert.Single(merged);
        Assert.Equal("my-accept", only.Data["Marker"]);
        Assert.DoesNotContain(merged, t => t.Type == ArcTransactionType.QuestTokenAwarded);
    }

    // ===== 2. Canonical-timestamp merge + re-sequencing =====

    [Fact]
    public void Build_MergesArcsChronologically_AndAssignsOneMonotonicSequence()
    {
        // Two arcs, each numbering its own log from 1 — the raw per-instance
        // sequence numbers collide. Timestamps interleave the arcs.
        var arcA = Instance("arc-a",
            Tx(_avatarId, 1, BaseTime.AddSeconds(0), marker: "a1"),
            Tx(_avatarId, 2, BaseTime.AddSeconds(20), marker: "a2"));
        var arcB = Instance("arc-b",
            Tx(_avatarId, 1, BaseTime.AddSeconds(10), marker: "b1"),
            Tx(_avatarId, 2, BaseTime.AddSeconds(30), marker: "b2"));

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { arcA, arcB });

        Assert.Equal(new[] { "a1", "b1", "a2", "b2" }, merged.Select(t => t.Data["Marker"]));
        // One coherent sequence over the merged stream — this is what acceptance
        // scoping (ScopeToCurrentAcceptance) keys on
        Assert.Equal(new long[] { 1, 2, 3, 4 }, merged.Select(t => t.SequenceNumber));
    }

    [Fact]
    public void Build_PrefersServerTimestampOverLocal_ForOrdering()
    {
        // Synced transaction: early server clock, wildly late local clock (client
        // clock skew). The documented ordering key is canonical = server ?? local.
        var synced = Tx(_avatarId, 1,
            localTimestamp: BaseTime.AddHours(6),
            serverTimestamp: BaseTime.AddSeconds(1),
            marker: "synced-early");
        var localOnly = Tx(_avatarId, 1,
            localTimestamp: BaseTime.AddSeconds(30),
            marker: "local-later");

        var merged = CrossArcQuestTransactionLog.Build(
            _avatarId,
            new[] { Instance("arc-a", synced), Instance("arc-b", localOnly) });

        Assert.Equal(new[] { "synced-early", "local-later" }, merged.Select(t => t.Data["Marker"]));
    }

    [Fact]
    public void Build_TimestampTies_PreservePerInstanceOrder()
    {
        // All transactions share one timestamp — the stable sort must keep each
        // instance's own committed (sequence) order so single-arc quests behave
        // exactly as they did before the union existed.
        var arc = Instance("arc-a",
            Tx(_avatarId, 3, BaseTime, marker: "third"),
            Tx(_avatarId, 1, BaseTime, marker: "first"),
            Tx(_avatarId, 2, BaseTime, marker: "second"));

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { arc });

        // GetCommittedTransactions orders by per-instance sequence before the merge
        Assert.Equal(new[] { "first", "second", "third" }, merged.Select(t => t.Data["Marker"]));
        Assert.Equal(new long[] { 1, 2, 3 }, merged.Select(t => t.SequenceNumber));
    }

    [Fact]
    public void Build_SequenceIsStrictlyMonotonic_EvenWithCollidingInputSequences()
    {
        var arcs = Enumerable.Range(0, 3)
            .Select(arc => Instance($"arc-{arc}",
                Tx(_avatarId, 1, BaseTime.AddSeconds(arc), marker: $"{arc}-1"),
                Tx(_avatarId, 2, BaseTime.AddMinutes(1).AddSeconds(arc), marker: $"{arc}-2")))
            .ToArray();

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, arcs);

        Assert.Equal(6, merged.Count);
        Assert.Equal(Enumerable.Range(1, 6).Select(i => (long)i), merged.Select(t => t.SequenceNumber));
    }

    // ===== Committed-only input =====

    [Fact]
    public void Build_IncludesOnlyCommittedTransactions()
    {
        var arc = Instance("arc-a",
            Tx(_avatarId, 1, BaseTime, status: TransactionStatus.Committed, marker: "committed"),
            Tx(_avatarId, 2, BaseTime.AddSeconds(1), status: TransactionStatus.Pending, marker: "pending"),
            Tx(_avatarId, 3, BaseTime.AddSeconds(2), status: TransactionStatus.Rejected, marker: "rejected"));

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { arc });

        var only = Assert.Single(merged);
        Assert.Equal("committed", only.Data["Marker"]);
    }

    // ===== Copy semantics =====

    [Fact]
    public void Build_DoesNotMutateSourceInstances()
    {
        // Re-sequencing must not leak back into the instances the caller still
        // holds — the repository caches ArcInstance objects, and corrupting their
        // per-instance sequence numbers would corrupt every later replay.
        var a1 = Tx(_avatarId, 7, BaseTime.AddSeconds(10), marker: "a1");
        var b1 = Tx(_avatarId, 7, BaseTime, marker: "b1");
        var arcA = Instance("arc-a", a1);
        var arcB = Instance("arc-b", b1);

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { arcA, arcB });

        // Sources untouched
        Assert.Equal(7, a1.SequenceNumber);
        Assert.Equal(7, b1.SequenceNumber);

        // Copies carry identity + payload, with the merged sequence
        Assert.Equal(new[] { "b1", "a1" }, merged.Select(t => t.Data["Marker"]));
        Assert.Equal(b1.TransactionId, merged[0].TransactionId);
        Assert.Equal(a1.TransactionId, merged[1].TransactionId);
        Assert.All(merged, t => Assert.NotSame(a1, t));
        Assert.Equal(new long[] { 1, 2 }, merged.Select(t => t.SequenceNumber));
    }

    [Fact]
    public void Build_CopiesPreserveAllReplayRelevantFields()
    {
        var original = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            SequenceNumber = 42,
            AvatarId = _avatarId.ToString(),
            LocalTimestamp = BaseTime,
            ServerTimestamp = BaseTime.AddSeconds(1),
            Status = TransactionStatus.Committed,
            SyncState = TransactionSyncState.Synced,
            Type = ArcTransactionType.QuestTokenAwarded,
            ExtensionTypeName = "SomeExtension",
            Data = new Dictionary<string, string> { [TransactionDataKeys.QuestTokenRef] = "token-1" },
            ReversesTransactionId = Guid.NewGuid(),
            ReversalReason = "test"
        };

        var merged = CrossArcQuestTransactionLog.Build(_avatarId, new[] { Instance("arc", original) });

        var copy = Assert.Single(merged);
        Assert.Equal(original.TransactionId, copy.TransactionId);
        Assert.Equal(1, copy.SequenceNumber); // re-sequenced
        Assert.Equal(original.AvatarId, copy.AvatarId);
        Assert.Equal(original.LocalTimestamp, copy.LocalTimestamp);
        Assert.Equal(original.ServerTimestamp, copy.ServerTimestamp);
        Assert.Equal(original.Status, copy.Status);
        Assert.Equal(original.SyncState, copy.SyncState);
        Assert.Equal(original.Type, copy.Type);
        Assert.Equal(original.ExtensionTypeName, copy.ExtensionTypeName);
        Assert.Equal("token-1", copy.GetData<string>(TransactionDataKeys.QuestTokenRef));
        Assert.Equal(original.ReversesTransactionId, copy.ReversesTransactionId);
        Assert.Equal(original.ReversalReason, copy.ReversalReason);
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmptyLog()
    {
        Assert.Empty(CrossArcQuestTransactionLog.Build(_avatarId, Array.Empty<ArcInstance>()));
        Assert.Empty(CrossArcQuestTransactionLog.Build(_avatarId, new[] { Instance("empty-arc") }));
    }
}
