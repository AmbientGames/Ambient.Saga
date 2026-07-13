using Ambient.Saga.Engine.Application.Handlers.Saga;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.Rpg.Battle;

/// <summary>
/// Expiry of a persisted telegraphed attack (tell): the authored reaction window
/// plus the fixed server grace. LiteDB round-trips DateTime back as LOCAL kind,
/// so the check must normalize to UTC before comparing — treating a local-kind
/// timestamp as UTC shifts expiry by the machine's UTC offset (a tell could look
/// hours in the future and never auto-resolve, or expire the instant it's issued).
/// </summary>
public class ReactionTellExpiryTests
{
    private static SagaTransaction Tell(DateTime issuedAt, int windowMs) => new()
    {
        TransactionId = Guid.NewGuid(),
        Type = SagaTransactionType.BattleTurnExecuted,
        LocalTimestamp = issuedAt,
        Data = new Dictionary<string, string>
        {
            [TransactionDataKeys.ReactionWindowMs] = windowMs.ToString()
        }
    };

    [Fact]
    public void WithinWindowPlusGrace_IsNotExpired()
    {
        var now = DateTime.UtcNow;
        var tell = Tell(now.AddMilliseconds(-1000), windowMs: 5000);

        Assert.False(ReactionTransactionFactory.IsExpired(tell, now));
    }

    [Fact]
    public void AfterWindowPlusGrace_IsExpired()
    {
        var now = DateTime.UtcNow;
        // 5000ms window + TimeoutGraceMs (3000ms) = 8s; issued 10s ago
        var tell = Tell(now.AddSeconds(-10), windowMs: 5000);

        Assert.True(ReactionTransactionFactory.IsExpired(tell, now));
    }

    [Fact]
    public void MissingReactionWindow_StillGetsTheGracePeriod()
    {
        var now = DateTime.UtcNow;
        var tell = Tell(now.AddMilliseconds(-1000), windowMs: 0);
        tell.Data.Clear();

        // No window key parses as 0 — grace alone decides
        Assert.False(ReactionTransactionFactory.IsExpired(tell, now));
        Assert.True(ReactionTransactionFactory.IsExpired(
            Tell(now.AddMilliseconds(-(ReactionTransactionFactory.TimeoutGraceMs + 1000)), windowMs: 0), now));
    }

    [Fact]
    public void LiteDbLocalKindRoundTrip_DoesNotShiftExpiry()
    {
        var now = DateTime.UtcNow;

        // LiteDB hands back the same instant converted to local time, Kind=Local —
        // expiry must match what the original UTC timestamp would produce
        var freshUtc = now.AddMilliseconds(-1000);
        Assert.False(ReactionTransactionFactory.IsExpired(Tell(freshUtc.ToLocalTime(), 5000), now));

        var staleUtc = now.AddSeconds(-10);
        Assert.True(ReactionTransactionFactory.IsExpired(Tell(staleUtc.ToLocalTime(), 5000), now));
    }
}
