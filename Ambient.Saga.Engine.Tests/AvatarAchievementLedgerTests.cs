using Ambient.Domain;
using Ambient.Domain.Entities;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Tests that the avatar's persisted Achievements list is the working single
/// unlock ledger (audit C2): unlocks written by the evaluation pipeline land on
/// the avatar document and read back through the same service.
/// </summary>
public class AvatarAchievementLedgerTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly GameAvatarRepository _avatarRepository;
    private readonly AvatarUpdateService _service;

    public AvatarAchievementLedgerTests()
    {
        var mapper = new BsonMapper();
        mapper.Entity<AvatarBase>().Ignore(x => x.BlockOwnership);
        mapper.Entity<AvatarEntity>().Id(x => x.Id);
        _database = new LiteDatabase(new MemoryStream(), mapper);
        _avatarRepository = new GameAvatarRepository(_database);
        _service = new AvatarUpdateService(() => _avatarRepository, () => new World());
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task UpdateAchievementInstancesAsync_PersistsUnlockToAvatarLedger()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        await _avatarRepository.SaveAvatarAsync(new AvatarEntity { Id = Guid.NewGuid(), AvatarId = avatarId });

        var unlock = new AchievementInstance
        {
            InstanceId = "ACH_FIRST_BOSS",
            TemplateRef = "ACH_FIRST_BOSS",
            AvatarId = avatarId.ToString(),
            IsUnlocked = true,
            UnlockedAt = DateTime.UtcNow
        };

        // Act
        await _service.UpdateAchievementInstancesAsync(avatarId, new List<AchievementInstance> { unlock });

        // Assert - the unlock is on the persisted avatar document
        var reloaded = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        Assert.NotNull(reloaded?.Achievements);
        var entry = Assert.Single(reloaded!.Achievements!);
        Assert.Equal("ACH_FIRST_BOSS", entry.AchievementRef);

        // ...and reads back through the same ledger
        var instances = await _service.GetAchievementInstancesAsync(avatarId);
        var instance = Assert.Single(instances);
        Assert.Equal("ACH_FIRST_BOSS", instance.TemplateRef);
        Assert.True(instance.IsUnlocked);
    }

    [Fact]
    public async Task UpdateAchievementInstancesAsync_SameUnlockTwice_DoesNotDuplicateLedgerEntry()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        await _avatarRepository.SaveAvatarAsync(new AvatarEntity { Id = Guid.NewGuid(), AvatarId = avatarId });

        var unlock = new AchievementInstance
        {
            InstanceId = "ACH_FIRST_BOSS",
            TemplateRef = "ACH_FIRST_BOSS",
            AvatarId = avatarId.ToString(),
            IsUnlocked = true,
            UnlockedAt = DateTime.UtcNow
        };

        // Act - the evaluation behavior re-submits the full instance list each pass
        await _service.UpdateAchievementInstancesAsync(avatarId, new List<AchievementInstance> { unlock });
        await _service.UpdateAchievementInstancesAsync(avatarId, new List<AchievementInstance> { unlock });

        // Assert
        var reloaded = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        Assert.Single(reloaded!.Achievements!);
    }
}
