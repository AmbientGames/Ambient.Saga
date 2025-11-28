namespace Ambient.Saga.StoryGenerator.QuestStructureGenerators;

/// <summary>
/// Generates location-specific micro-quests (bounties, challenges, collections).
/// Extracted from QuestGenerator for SRP compliance.
/// </summary>
public static class LocationQuestGenerator
{
    public static List<GeneratedQuest> Generate(
        NarrativeStructure narrative,
        RefNameGenerator refNameGenerator,
        QuestRewardFactory rewardFactory)
    {
        var quests = new List<GeneratedQuest>();

        // Create bounty quests for locations with bosses
        var bossLocations = narrative.CharacterPlacements
            .Where(cp => cp.CharacterType == "Boss")
            .Take(5) // Limit to avoid quest spam
            .ToList();

        foreach (var placement in bossLocations)
        {
            var locationRef = refNameGenerator.GetRefName(placement.Location);
            var quest = new GeneratedQuest
            {
                RefName = $"BOUNTY_{locationRef}",
                DisplayName = $"Bounty: {placement.DisplayName}",
                Description = $"Defeat {placement.DisplayName} and claim the bounty.",
                Stages = new List<QuestStage>
                {
                    new QuestStage
                    {
                        RefName = "DEFEAT",
                        DisplayName = "Defeat the Target",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "DEFEAT_BOSS",
                                Type = "CharacterDefeated",
                                DisplayName = $"Defeat {placement.DisplayName}",
                                CharacterRef = placement.CharacterRefName,
                                Threshold = 1
                            }
                        },
                        Rewards = new List<QuestReward>
                        {
                            rewardFactory.CreateGoldReward(500),
                            rewardFactory.CreateEquipmentReward("BossDefeatWeapon", 1)
                        }
                    }
                }
            };
            quest.Stages.First().IsStartStage = true;

            quests.Add(quest);
        }

        return quests;
    }
}
