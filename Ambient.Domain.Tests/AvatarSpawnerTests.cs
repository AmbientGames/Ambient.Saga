using Ambient.Domain.GameLogic.Gameplay.Avatar;

namespace Ambient.Domain.Tests;

public class AvatarSpawnerTests
{
    private static AvatarArchetype CreateArchetype() => new()
    {
        RefName = "Warrior",
        SpawnStats = new CharacterStats { Health = 1.0f, Stamina = 1.0f, Credits = 100, Experience = 0, Level = 1 },
        RespawnStats = new CharacterStats { Health = 0.5f, Stamina = 0.5f, Credits = 100, Experience = 0, Level = 1 },
        SpawnCapabilities = new ItemCollection(),
        RespawnCapabilities = new ItemCollection()
    };

    [Fact]
    public void ReSpawnFromModelAvatar_ResetsVitalsToRespawnStats()
    {
        var archetype = CreateArchetype();
        var avatar = new AvatarBase();
        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);
        avatar.Stats.Health = 0f; // died

        AvatarSpawner.ReSpawnFromModelAvatar(avatar, archetype);

        Assert.Equal(0.5f, avatar.Stats.Health);
        Assert.Equal(0.5f, avatar.Stats.Stamina);
    }

    [Fact]
    public void ReSpawnFromModelAvatar_PreservesEarnedProgression()
    {
        var archetype = CreateArchetype();
        var avatar = new AvatarBase();
        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);

        // Earn progression, then die
        avatar.Stats.Credits = 5000;
        avatar.Stats.Experience = 3000;
        avatar.Stats.Level = 8;
        avatar.Stats.Health = 0f;

        AvatarSpawner.ReSpawnFromModelAvatar(avatar, archetype);

        Assert.Equal(5000, avatar.Stats.Credits);
        Assert.Equal(3000, avatar.Stats.Experience);
        Assert.Equal(8, avatar.Stats.Level);
    }
}
