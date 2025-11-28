namespace Ambient.Saga.StoryGenerator.QuestStructureGenerators;

/// <summary>
/// Generates epic quest chains - interconnected legendary quests with item/achievement prerequisites.
/// Creates multi-quest story arcs where completing one quest unlocks the next.
/// Extracted from QuestGenerator for SRP compliance.
/// </summary>
public static class EpicQuestChainGenerator
{
    public static List<GeneratedQuest> Generate(
        NarrativeStructure narrative,
        RefNameGenerator refNameGenerator,
        QuestRewardFactory rewardFactory,
        Random random,
        Func<string, string> getRandomCharacterRef,
        Func<string, string> getRandomItemRef,
        Func<string, string> getRandomEquipmentRef)
    {
        var quests = new List<GeneratedQuest>();
        var allLocations = narrative.StoryThreads.SelectMany(t => t.Locations).Distinct().ToList();

        if (allLocations.Count < 6)
            return quests; // Need enough locations for epic chains

        // Create 2-3 epic quest chains
        var chainCount = Math.Min(3, allLocations.Count / 6);

        for (var chainIdx = 0; chainIdx < chainCount; chainIdx++)
        {
            var chainLocations = allLocations.Skip(chainIdx * 6).Take(6).ToList();
            if (chainLocations.Count < 4) continue;

            var chainName = chainIdx == 0 ? "ANCIENT_ARTIFACT" : chainIdx == 1 ? "LEGENDARY_HERO" : "WORLD_SAVIOR";
            var chainProgress = (double)chainIdx / Math.Max(1, chainCount - 1);

            // Quest 1: Discover the Legend
            var quest1 = new GeneratedQuest
            {
                RefName = $"EPIC_{chainName}_01_DISCOVER",
                DisplayName = $"Epic: Discover the {(chainName == "ANCIENT_ARTIFACT" ? "Ancient Artifact" : chainName == "LEGENDARY_HERO" ? "Legendary Hero" : "World Crisis")}",
                Description = $"Begin your epic journey by uncovering ancient legends.",
                Prerequisites = new List<QuestPrerequisite>
                {
                    new QuestPrerequisite
                    {
                        MinimumLevel = 10 + (int)(chainProgress * 20)
                    }
                }
            };

            quest1.Stages = new List<QuestStage>
            {
                new QuestStage
                {
                    RefName = "HEAR_LEGEND",
                    DisplayName = "Hear the Legend",
                    IsStartStage = true,
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "FIND_LOREKEEPER",
                            Type = "LocationReached",
                            DisplayName = $"Find the lorekeeper at {chainLocations[0].DisplayName}",
                            SagaArcRef = refNameGenerator.GetRefName(chainLocations[0]),
                            Threshold = 1
                        },
                        new QuestObjective
                        {
                            RefName = "LEARN_STORY",
                            Type = "DialogueCompleted",
                            DisplayName = "Learn the ancient story",
                            CharacterRef = getRandomCharacterRef("Lorekeeper"),
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        rewardFactory.CreateGoldReward(500),
                        rewardFactory.CreateExperienceReward(800),
                        rewardFactory.CreateQuestTokenReward($"TOKEN_{chainName}_KNOWLEDGE", 1)
                    }
                }
            };

            quests.Add(quest1);

            // Quest 2: Gather the Components (requires token from Quest 1)
            var quest2 = new GeneratedQuest
            {
                RefName = $"EPIC_{chainName}_02_GATHER",
                DisplayName = $"Epic: Gather the Sacred Components",
                Description = $"Collect the legendary components needed to proceed.",
                Prerequisites = new List<QuestPrerequisite>
                {
                    new QuestPrerequisite
                    {
                        QuestRef = quest1.RefName,
                        MinimumLevel = 15 + (int)(chainProgress * 20),
                        RequiredItemRef = $"TOKEN_{chainName}_KNOWLEDGE"
                    }
                }
            };

            quest2.Stages = chainLocations.Skip(1).Take(3).Select((loc, idx) => new QuestStage
            {
                RefName = $"GATHER_COMPONENT_{idx + 1}",
                DisplayName = $"Retrieve Component {idx + 1}",
                IsStartStage = idx == 0,
                Objectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        RefName = $"REACH_LOCATION_{idx + 1}",
                        Type = "LocationReached",
                        DisplayName = $"Travel to {loc.DisplayName}",
                        SagaArcRef = refNameGenerator.GetRefName(loc),
                        Threshold = 1
                    },
                    new QuestObjective
                    {
                        RefName = $"DEFEAT_GUARDIAN_{idx + 1}",
                        Type = "CharactersDefeatedByTag",
                        DisplayName = $"Defeat the guardian at {loc.DisplayName}",
                        CharacterTag = "boss",
                        Threshold = 1
                    },
                    new QuestObjective
                    {
                        RefName = $"COLLECT_COMPONENT_{idx + 1}",
                        Type = "ItemCollected",
                        DisplayName = $"Collect the sacred component",
                        ItemRef = getRandomItemRef($"Component{idx + 1}"),
                        Threshold = 1
                    }
                },
                Rewards = new List<QuestReward>
                {
                    rewardFactory.CreateGoldReward(800 + idx * 200),
                    rewardFactory.CreateExperienceReward(1000 + idx * 300),
                    rewardFactory.CreateEquipmentReward($"ComponentReward{idx + 1}", 1)
                },
                NextStage = idx < 2 ? $"GATHER_COMPONENT_{idx + 2}" : string.Empty
            }).ToList();

            // Add final reward to last stage
            quest2.Stages.Last().Rewards.Add(rewardFactory.CreateQuestTokenReward($"TOKEN_{chainName}_COMPONENTS", 1));

            quests.Add(quest2);

            // Quest 3: The Final Trial (requires components and achievement)
            var finalLocation = chainLocations.LastOrDefault();
            if (finalLocation != null)
            {
                var quest3 = new GeneratedQuest
                {
                    RefName = $"EPIC_{chainName}_03_FINALE",
                    DisplayName = $"Epic: The Final Trial",
                    Description = $"Face the ultimate challenge and complete your legendary quest.",
                    Prerequisites = new List<QuestPrerequisite>
                    {
                        new QuestPrerequisite
                        {
                            QuestRef = quest2.RefName,
                            MinimumLevel = 25 + (int)(chainProgress * 25),
                            RequiredItemRef = $"TOKEN_{chainName}_COMPONENTS"
                        }
                    }
                };

                quest3.Stages = new List<QuestStage>
                {
                    new QuestStage
                    {
                        RefName = "REACH_FINAL_LOCATION",
                        DisplayName = "Journey to the Sacred Site",
                        IsStartStage = true,
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "TRAVEL_TO_SITE",
                                Type = "LocationReached",
                                DisplayName = $"Reach {finalLocation.DisplayName}",
                                SagaArcRef = refNameGenerator.GetRefName(finalLocation),
                                Threshold = 1
                            }
                        },
                        NextStage = "FINAL_BATTLE"
                    },
                    new QuestStage
                    {
                        RefName = "FINAL_BATTLE",
                        DisplayName = "The Ultimate Confrontation",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "DEFEAT_FINAL_BOSS",
                                Type = "CharactersDefeatedByTag",
                                DisplayName = "Defeat the legendary guardian",
                                CharacterTag = "boss",
                                Threshold = 1
                            },
                            new QuestObjective
                            {
                                RefName = "BONUS_PERFECT_VICTORY",
                                Type = "Custom",
                                DisplayName = "Win without being defeated",
                                Optional = true
                            }
                        },
                        Rewards = new List<QuestReward>
                        {
                            rewardFactory.CreateGoldReward(5000 + (int)(chainProgress * 5000)),
                            rewardFactory.CreateExperienceReward(10000 + (int)(chainProgress * 10000)),
                            rewardFactory.CreateEquipmentReward($"Legendary{chainName}", 2)
                        }
                    }
                };

                quests.Add(quest3);
            }
        }

        return quests;
    }
}
