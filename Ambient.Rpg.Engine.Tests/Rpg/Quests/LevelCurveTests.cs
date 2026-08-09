using Ambient.Domain;
using Ambient.Domain.Entities;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Progression;
using Ambient.Rpg.Engine.Domain.Quests;

namespace Ambient.Rpg.Engine.Tests.Rpg.Quests;

public class LevelCurveTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(299, 2)]
    [InlineData(300, 3)]
    [InlineData(1000, 5)]
    [InlineData(4500, 10)]
    [InlineData(25300, 23)]
    [InlineData(122500, 50)]
    public void GetLevelForExperience_MapsTriangularCurve(float xp, int expectedLevel)
        => Assert.Equal(expectedLevel, LevelCurve.GetLevelForExperience(xp));

    [Fact]
    public void GetExperienceForLevel_IsInverseOfGetLevelForExperience()
    {
        for (var level = 2; level <= 60; level++)
        {
            var xp = LevelCurve.GetExperienceForLevel(level);
            Assert.Equal(level, LevelCurve.GetLevelForExperience(xp));
            Assert.Equal(level - 1, LevelCurve.GetLevelForExperience(xp - 1));
        }
    }

    [Fact]
    public void DistributeReward_ExperienceRaisesLevel()
    {
        var avatar = new AvatarEntity { Stats = new CharacterStats { Experience = 0, Level = 1 } };
        var reward = new QuestReward { Experience = new QuestRewardExperience { Amount = 1000 } };

        QuestRewardDistributor.DistributeReward(reward, avatar, new World());

        Assert.Equal(1000, avatar.Stats.Experience);
        Assert.Equal(5, avatar.Stats.Level); // 1000 XP reaches level 5 on the curve
    }

    [Fact]
    public void DistributeReward_ExperienceNeverDemotesLevel()
    {
        var avatar = new AvatarEntity { Stats = new CharacterStats { Experience = 0, Level = 8 } };
        var reward = new QuestReward { Experience = new QuestRewardExperience { Amount = 100 } };

        QuestRewardDistributor.DistributeReward(reward, avatar, new World());

        Assert.Equal(8, avatar.Stats.Level);
    }
}
