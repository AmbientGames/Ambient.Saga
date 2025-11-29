using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestStructureGenerators;

/// <summary>
/// Generates hidden/secret quests with special prerequisites and hidden objectives.
/// Creates 4 types of secret quests: Forgotten Shrine, Master Craftsman, Collector's Vault, Time Trial.
/// Extracted from QuestGenerator for SRP compliance.
/// </summary>
public static class HiddenQuestGenerator
{
    public static List<GeneratedQuest> Generate(
        NarrativeStructure narrative,
        RefNameGenerator refNameGenerator,
        QuestRewardFactory rewardFactory,
        Random random,
        Func<string, string> getRandomItemRef,
        Func<string, string> getRandomEquipmentRef)
    {
        var quests = new List<GeneratedQuest>();
        var allLocations = narrative.StoryThreads.SelectMany(t => t.Locations).Distinct().ToList();

        if (allLocations.Count < 3)
            return quests;

        // Secret Quest 1: The Forgotten Shrine (requires finding a rare map)
        var shrineLocation = allLocations
            .Where(l => l.Type == SourceLocationType.Landmark || l.Type == SourceLocationType.Structure)
            .Skip(random.Next(Math.Max(1, allLocations.Count / 2)))
            .FirstOrDefault();

        if (shrineLocation != null)
        {
            var shrineQuest = new GeneratedQuest
            {
                RefName = "HIDDEN_FORGOTTEN_SHRINE",
                DisplayName = "Secret: The Forgotten Shrine",
                Description = "An ancient shrine marked on a mysterious map. What secrets does it hold?",
                Prerequisites = new List<QuestPrerequisite>
                {
                    new QuestPrerequisite
                    {
                        MinimumLevel = 20,
                        RequiredItemRef = getRandomItemRef("AncientMap")
                    }
                }
            };

            shrineQuest.Stages = new List<QuestStage>
            {
                new QuestStage
                {
                    RefName = "FIND_SHRINE",
                    DisplayName = "Locate the Hidden Shrine",
                    IsStartStage = true,
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DISCOVER_LOCATION",
                            Type = "LocationReached",
                            DisplayName = $"Find the shrine at {shrineLocation.DisplayName}",
                            SagaArcRef = refNameGenerator.GetRefName(shrineLocation),
                            Threshold = 1,
                            Hidden = true // Hidden until discovered
                        }
                    },
                    NextStage = "UNLOCK_SHRINE"
                },
                new QuestStage
                {
                    RefName = "UNLOCK_SHRINE",
                    DisplayName = "Unlock the Shrine's Secrets",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "COLLECT_KEYS",
                            Type = "QuestTokenCollected",
                            DisplayName = "Collect 3 shrine keys",
                            ItemRef = "TOKEN_SHRINE_KEY",
                            Threshold = 3,
                            Hidden = false
                        },
                        new QuestObjective
                        {
                            RefName = "SOLVE_SHRINE_PUZZLE",
                            Type = "Custom",
                            DisplayName = "Solve the shrine's ancient puzzle",
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        rewardFactory.CreateGoldReward(3000),
                        rewardFactory.CreateExperienceReward(5000),
                        rewardFactory.CreateEquipmentReward("ShrineArtifact", 1)
                    }
                }
            };

            quests.Add(shrineQuest);
        }

        // Secret Quest 2: Master Craftsman's Legacy (requires crafting achievement)
        var craftLocation = allLocations
            .Where(l => l.Type == SourceLocationType.Structure)
            .Skip(random.Next(Math.Max(1, allLocations.Count / 3)))
            .FirstOrDefault();

        if (craftLocation != null)
        {
            var craftQuest = new GeneratedQuest
            {
                RefName = "HIDDEN_MASTER_CRAFTSMAN",
                DisplayName = "Secret: Master Craftsman's Legacy",
                Description = "Only true masters of crafting can unlock this legendary workshop.",
                Prerequisites = new List<QuestPrerequisite>
                {
                    new QuestPrerequisite
                    {
                        MinimumLevel = 30
                    }
                }
            };

            craftQuest.Stages = new List<QuestStage>
            {
                new QuestStage
                {
                    RefName = "DISCOVER_WORKSHOP",
                    DisplayName = "Discover the Hidden Workshop",
                    IsStartStage = true,
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "FIND_WORKSHOP",
                            Type = "LocationReached",
                            DisplayName = $"Find the legendary workshop at {craftLocation.DisplayName}",
                            SagaArcRef = refNameGenerator.GetRefName(craftLocation),
                            Threshold = 1,
                            Hidden = true
                        }
                    },
                    NextStage = "CRAFT_MASTERPIECE"
                },
                new QuestStage
                {
                    RefName = "CRAFT_MASTERPIECE",
                    DisplayName = "Craft the Ultimate Masterpiece",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "GATHER_RARE_MATERIALS",
                            Type = "ItemCollected",
                            DisplayName = "Gather 5 legendary materials",
                            ItemRef = getRandomItemRef("LegendaryMaterial"),
                            Threshold = 5
                        },
                        new QuestObjective
                        {
                            RefName = "CRAFT_LEGENDARY_ITEM",
                            Type = "ItemCrafted",
                            DisplayName = "Craft the legendary masterpiece",
                            ItemRef = getRandomEquipmentRef("Masterpiece"),
                            Threshold = 1
                        },
                        new QuestObjective
                        {
                            RefName = "BONUS_FLAWLESS_CRAFT",
                            Type = "Custom",
                            DisplayName = "Achieve 100% quality on first try",
                            Optional = true
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        rewardFactory.CreateGoldReward(5000),
                        rewardFactory.CreateExperienceReward(8000),
                        rewardFactory.CreateEquipmentReward("MasterCraftsmanTools", 2)
                    }
                }
            };

            quests.Add(craftQuest);
        }

        // Secret Quest 3: The Collector's Paradise (requires specific quest tokens)
        var collectorLocation = allLocations
            .Where(l => l.Type == SourceLocationType.Landmark)
            .Skip(random.Next(Math.Max(1, allLocations.Count / 4)))
            .FirstOrDefault();

        if (collectorLocation != null)
        {
            var collectorQuest = new GeneratedQuest
            {
                RefName = "HIDDEN_COLLECTOR_PARADISE",
                DisplayName = "Secret: The Collector's Vault",
                Description = "A legendary collector's vault, accessible only to those who possess all the ancient seals.",
                Prerequisites = new List<QuestPrerequisite>
                {
                    new QuestPrerequisite
                    {
                        MinimumLevel = 35,
                        RequiredItemRef = getRandomItemRef("AncientSeals")
                    }
                }
            };

            collectorQuest.Stages = new List<QuestStage>
            {
                new QuestStage
                {
                    RefName = "UNLOCK_VAULT",
                    DisplayName = "Unlock the Collector's Vault",
                    IsStartStage = true,
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "REACH_VAULT",
                            Type = "LocationReached",
                            DisplayName = $"Reach the vault at {collectorLocation.DisplayName}",
                            SagaArcRef = refNameGenerator.GetRefName(collectorLocation),
                            Threshold = 1,
                            Hidden = true
                        },
                        new QuestObjective
                        {
                            RefName = "PLACE_SEALS",
                            Type = "Custom",
                            DisplayName = "Place all 5 ancient seals",
                            Threshold = 5
                        }
                    },
                    NextStage = "CLAIM_TREASURES"
                },
                new QuestStage
                {
                    RefName = "CLAIM_TREASURES",
                    DisplayName = "Claim the Legendary Treasures",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DEFEAT_VAULT_GUARDIAN",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat the vault guardian",
                            CharacterTag = "boss",
                            Threshold = 1
                        },
                        new QuestObjective
                        {
                            RefName = "LOOT_VAULT",
                            Type = "Custom",
                            DisplayName = "Claim the vault's treasures",
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        rewardFactory.CreateGoldReward(10000),
                        rewardFactory.CreateExperienceReward(15000),
                        rewardFactory.CreateEquipmentReward("UltimateLegendaryArmor", 5)
                    }
                }
            };

            quests.Add(collectorQuest);
        }

        // Secret Quest 4: Time Trial Challenge (no prerequisites except level, but has time limit)
        var challengeLocation = allLocations.LastOrDefault();
        if (challengeLocation != null)
        {
            var timeTrialQuest = new GeneratedQuest
            {
                RefName = "HIDDEN_TIME_TRIAL",
                DisplayName = "Secret: Speedrunner's Challenge",
                Description = "Complete a series of challenges against the clock. Only the fastest will prevail.",
                Prerequisites = new List<QuestPrerequisite>
                {
                    new QuestPrerequisite
                    {
                        MinimumLevel = 25
                    }
                }
            };

            timeTrialQuest.Stages = new List<QuestStage>
            {
                new QuestStage
                {
                    RefName = "START_CHALLENGE",
                    DisplayName = "Begin the Time Trial",
                    IsStartStage = true,
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "REACH_START",
                            Type = "LocationReached",
                            DisplayName = $"Reach the challenge arena at {challengeLocation.DisplayName}",
                            SagaArcRef = refNameGenerator.GetRefName(challengeLocation),
                            Threshold = 1
                        }
                    },
                    NextStage = "COMPLETE_TRIAL",
                    FailConditions = new List<QuestFailCondition>
                    {
                        new QuestFailCondition
                        {
                            Type = "TimeExpired",
                            TimeLimit = 600 // 10 minutes
                        }
                    }
                },
                new QuestStage
                {
                    RefName = "COMPLETE_TRIAL",
                    DisplayName = "Complete All Challenges",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DEFEAT_WAVE_1",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat 10 enemies (Wave 1)",
                            CharacterTag = "hostile",
                            Threshold = 10
                        },
                        new QuestObjective
                        {
                            RefName = "COLLECT_SPEED_TOKENS",
                            Type = "QuestTokenCollected",
                            DisplayName = "Collect 5 speed tokens",
                            ItemRef = "TOKEN_SPEED_CHALLENGE",
                            Threshold = 5
                        },
                        new QuestObjective
                        {
                            RefName = "DEFEAT_FINAL_BOSS",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat the time keeper",
                            CharacterTag = "boss",
                            Threshold = 1
                        },
                        new QuestObjective
                        {
                            RefName = "BONUS_UNDER_5_MIN",
                            Type = "Custom",
                            DisplayName = "Complete in under 5 minutes",
                            Optional = true
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        rewardFactory.CreateGoldReward(4000),
                        rewardFactory.CreateExperienceReward(7000),
                        rewardFactory.CreateEquipmentReward("SpeedBoostLegendary", 1)
                    },
                    FailConditions = new List<QuestFailCondition>
                    {
                        new QuestFailCondition
                        {
                            Type = "TimeExpired",
                            TimeLimit = 600 // 10 minutes total
                        }
                    }
                }
            };

            // Global fail condition
            timeTrialQuest.FailConditions = new List<QuestFailCondition>
            {
                new QuestFailCondition
                {
                    Type = "TimeExpired",
                    TimeLimit = 600
                }
            };

            quests.Add(timeTrialQuest);
        }

        return quests;
    }
}
