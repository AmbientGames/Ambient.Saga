using Ambient.Domain;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Tests for SteamAchievementService's ledger-driven replay (audit C2):
/// the avatar's persisted Achievements list is the single unlock ledger, and
/// ReplayAchievementsToSteam must seed its sync journal from it so unlocks earned
/// through dialogue / quest rewards / the evaluation pipeline reach Steam even
/// though those paths never call UnlockAchievement directly.
/// Steam itself is unavailable in tests — seeding happens before the availability
/// check, leaving records Pending for a later replay with Steam up.
/// </summary>
public class SteamAchievementServiceTests : IDisposable
{
    private readonly LiteDatabase _database;

    public SteamAchievementServiceTests()
    {
        // Mirror the production mapper bits that matter for the avatar document
        var mapper = new BsonMapper();
        mapper.Entity<AvatarBase>().Ignore(x => x.BlockOwnership);
        mapper.Entity<AvatarEntity>().Id(x => x.Id);
        _database = new LiteDatabase(new MemoryStream(), mapper);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    private AvatarEntity SaveAvatarWithLedger(Guid avatarId, params string[] achievementRefs)
    {
        var avatar = new AvatarEntity
        {
            Id = Guid.NewGuid(),
            AvatarId = avatarId,
            Achievements = achievementRefs
                .Select(r => new AchievementEntry
                {
                    AchievementRef = r,
                    UnlockedDate = DateTime.UtcNow.ToString("O"),
                    ProgressPercentage = 1.0f
                })
                .ToArray()
        };

        new GameAvatarRepository(_database).SaveAvatarAsync(avatar).GetAwaiter().GetResult();
        return avatar;
    }

    [Fact]
    public void ReplayAchievementsToSteam_SeedsJournalFromAvatarLedger()
    {
        // Arrange - two unlocks on the ledger, none in the sync journal
        var avatarId = Guid.NewGuid();
        SaveAvatarWithLedger(avatarId, "ACH_FIRST_BOSS", "ACH_EXPLORER");

        var service = new SteamAchievementService(_database, isSteamAvailable: false);

        // Act
        service.ReplayAchievementsToSteam(avatarId.ToString());

        // Assert - both ledger unlocks now have pending sync records
        var syncs = service.GetAchievementSyncs(avatarId.ToString());
        Assert.Equal(2, syncs.Count);
        Assert.Contains(syncs, s => s.SteamAchievementId == "ACH_FIRST_BOSS");
        Assert.Contains(syncs, s => s.SteamAchievementId == "ACH_EXPLORER");
        Assert.All(syncs, s => Assert.Equal(SteamSyncStatus.Pending, s.Status));
    }

    [Fact]
    public void ReplayAchievementsToSteam_RepeatedReplays_DoNotDuplicateJournalRecords()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        SaveAvatarWithLedger(avatarId, "ACH_FIRST_BOSS");

        var service = new SteamAchievementService(_database, isSteamAvailable: false);

        // Act - replay runs on every world load
        service.ReplayAchievementsToSteam(avatarId.ToString());
        service.ReplayAchievementsToSteam(avatarId.ToString());
        service.ReplayAchievementsToSteam(avatarId.ToString());

        // Assert - still exactly one journal record for the ledger entry
        Assert.Single(service.GetAchievementSyncs(avatarId.ToString()));
    }

    [Fact]
    public void ReplayAchievementsToSteam_LedgerBelongsToDifferentAvatar_DoesNotSeed()
    {
        // Arrange - the stored avatar is not the one being replayed
        var ledgerAvatarId = Guid.NewGuid();
        SaveAvatarWithLedger(ledgerAvatarId, "ACH_FIRST_BOSS");

        var service = new SteamAchievementService(_database, isSteamAvailable: false);

        // Act
        var otherAvatarId = Guid.NewGuid();
        service.ReplayAchievementsToSteam(otherAvatarId.ToString());

        // Assert - nothing seeded for either avatar
        Assert.Empty(service.GetAchievementSyncs(otherAvatarId.ToString()));
        Assert.Empty(service.GetAchievementSyncs(ledgerAvatarId.ToString()));
    }

    [Fact]
    public void ReplayAchievementsToSteam_ExistingJournalRecord_IsNotReseeded()
    {
        // Arrange - the unlock already went through UnlockAchievement (which also
        // writes the journal), and is on the ledger too
        var avatarId = Guid.NewGuid();
        SaveAvatarWithLedger(avatarId, "ACH_FIRST_BOSS");

        var service = new SteamAchievementService(_database, isSteamAvailable: false);
        service.UnlockAchievement("ACH_FIRST_BOSS", avatarId.ToString(), "ACH_FIRST_BOSS");

        // Act
        service.ReplayAchievementsToSteam(avatarId.ToString());

        // Assert - the ledger seed recognized the existing record
        Assert.Single(service.GetAchievementSyncs(avatarId.ToString()));
    }
}
