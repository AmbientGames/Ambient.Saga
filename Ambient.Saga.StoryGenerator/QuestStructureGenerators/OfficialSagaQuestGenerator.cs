namespace Ambient.Saga.StoryGenerator.QuestStructureGenerators;

/// <summary>
/// Generates quests connecting Official (hand-crafted) SagaArcs using spatial distribution.
/// Creates main chain, regional chains, and point-to-point connection quests.
/// Extracted from QuestGenerator for SRP compliance.
/// </summary>
public static class OfficialSagaQuestGenerator
{
    public static List<GeneratedQuest> Generate(
        List<OfficialSagaArc> officialSagas,
        NarrativeStructure narrative)
    {
        var quests = new List<GeneratedQuest>();

        if (officialSagas.Count < 2)
            return quests; // Need at least 2 official sagas to connect

        // Calculate world size for distance context
        var worldSizeKm = GeoHelper.CalculateWorldSizeKm(officialSagas.Select(s => s.Location).ToList());

        // Group official sagas by proximity for regional quest chains
        var regions = GeoHelper.ClusterByProximity(
            officialSagas.Select(s => s.Location).ToList(),
            worldSizeKm * 0.15 // 15% of world size = regional cluster
        );

        // Create main story quest chain connecting all official sagas
        if (officialSagas.Count >= 3)
        {
            quests.Add(GenerateMainOfficialQuestChain(officialSagas, narrative, worldSizeKm));
        }

        // Create regional quest chains for each cluster
        foreach (var region in regions.Where(r => r.Count >= 2))
        {
            var regionalSagas = officialSagas.Where(s => region.Contains(s.Location)).ToList();
            if (regionalSagas.Count >= 2)
            {
                quests.Add(GenerateRegionalQuestChain(regionalSagas, narrative, worldSizeKm));
            }
        }

        // Create point-to-point quests between nearby official sagas
        for (var i = 0; i < officialSagas.Count; i++)
        {
            var from = officialSagas[i];
            // Find 2-3 nearest official sagas
            var nearestSagas = officialSagas
                .Where(s => s != from)
                .OrderBy(s => GeoHelper.DistanceKm(from.Location, s.Location))
                .Take(3)
                .ToList();

            foreach (var to in nearestSagas.Take(2)) // Connect to 2 nearest
            {
                var distance = GeoHelper.DistanceKm(from.Location, to.Location);
                // Only create quest if distance is reasonable (not too far, not too close)
                if (distance > worldSizeKm * 0.05 && distance < worldSizeKm * 0.40)
                {
                    quests.Add(GeneratePointToPointQuest(from, to, narrative, distance, worldSizeKm));
                }
            }
        }

        return quests;
    }

    private static GeneratedQuest GenerateMainOfficialQuestChain(List<OfficialSagaArc> officialSagas, NarrativeStructure narrative, double worldSizeKm)
    {
        var questRefName = "OFFICIAL_MAIN_CHAIN";
        var quest = new GeneratedQuest
        {
            RefName = questRefName,
            DisplayName = "[AI: Epic Quest Chain Title connecting all major locations]",
            Description = "[AI: Description of legendary quest connecting all Official Saga locations]"
        };

        // Create stages visiting each official saga
        foreach (var saga in officialSagas.Take(10)) // Limit to 10 for reasonable quest length
        {
            var stageNum = quest.Stages.Count + 1;
            var locationRef = saga.RefName;

            var stage = new QuestStage
            {
                RefName = $"{questRefName}_STAGE_{stageNum:D2}",
                DisplayName = $"[AI: Visit {saga.DisplayName}]",
                Description = $"[AI: Travel to {saga.DisplayName} and complete objectives]",
                IsStartStage = stageNum == 1
            };

            // Add objectives at this location
            stage.Objectives.Add(new QuestObjective
            {
                RefName = $"{questRefName}_STAGE_{stageNum:D2}_OBJ_DISCOVER",
                Type = "LocationReached",
                DisplayName = $"[AI: Reach {saga.DisplayName}]",
                LocationRef = locationRef
            });

            // Add character interaction if character present
            var character = narrative.CharacterPlacements.FirstOrDefault(c =>
                GeoHelper.DistanceKm(c.Location, saga.Location) < 0.1); // Very close
            if (character != null)
            {
                stage.Objectives.Add(new QuestObjective
                {
                    RefName = $"{questRefName}_STAGE_{stageNum:D2}_OBJ_TALK",
                    Type = character.CharacterType == "Boss" ? "CharacterDefeated" : "DialogueCompleted",
                    DisplayName = $"[AI: {(character.CharacterType == "Boss" ? "Defeat" : "Speak with")} {character.DisplayName}]",
                    CharacterRef = character.CharacterRefName
                });
            }

            quest.Stages.Add(stage);
        }

        // Add epic rewards
        quest.GlobalRewards.Add(new QuestReward
        {
            Currency = new QuestRewardCurrency { Amount = officialSagas.Count * 1000 },
            Experience = new QuestRewardExperience { Amount = officialSagas.Count * 500 },
            Condition = "OnSuccess"
        });

        return quest;
    }

    private static GeneratedQuest GenerateRegionalQuestChain(List<OfficialSagaArc> regionalSagas, NarrativeStructure narrative, double worldSizeKm)
    {
        var firstSaga = regionalSagas.First();
        var questRefName = $"REGIONAL_{firstSaga.RefName}";

        var quest = new GeneratedQuest
        {
            RefName = questRefName,
            DisplayName = $"[AI: Regional Quest Chain for {firstSaga.DisplayName} area]",
            Description = "[AI: Explore regional locations and complete challenges]"
        };

        // Create stages for each regional saga
        foreach (var saga in regionalSagas.Take(5))
        {
            var stageNum = quest.Stages.Count + 1;
            var stage = new QuestStage
            {
                RefName = $"{questRefName}_STAGE_{stageNum:D2}",
                DisplayName = $"[AI: Stage at {saga.DisplayName}]",
                IsStartStage = stageNum == 1
            };

            stage.Objectives.Add(new QuestObjective
            {
                RefName = $"{questRefName}_STAGE_{stageNum:D2}_OBJ",
                Type = "LocationReached",
                LocationRef = saga.RefName
            });

            quest.Stages.Add(stage);
        }

        quest.GlobalRewards.Add(new QuestReward
        {
            Currency = new QuestRewardCurrency { Amount = regionalSagas.Count * 500 },
            Condition = "OnSuccess"
        });

        return quest;
    }

    private static GeneratedQuest GeneratePointToPointQuest(OfficialSagaArc from, OfficialSagaArc to, NarrativeStructure narrative, double distanceKm, double worldSizeKm)
    {
        var questRefName = $"CONNECT_{from.RefName}_TO_{to.RefName}";
        var quest = new GeneratedQuest
        {
            RefName = questRefName,
            DisplayName = $"[AI: Journey from {from.DisplayName} to {to.DisplayName}]",
            Description = $"[AI: Travel {distanceKm:F0}km from {from.DisplayName} to {to.DisplayName}]"
        };

        // Start stage at 'from' location
        var startStage = new QuestStage
        {
            RefName = $"{questRefName}_START",
            DisplayName = $"[AI: Begin at {from.DisplayName}]",
            IsStartStage = true
        };
        startStage.Objectives.Add(new QuestObjective
        {
            RefName = $"{questRefName}_START_OBJ",
            Type = "LocationReached",
            LocationRef = from.RefName
        });
        quest.Stages.Add(startStage);

        // End stage at 'to' location
        var endStage = new QuestStage
        {
            RefName = $"{questRefName}_END",
            DisplayName = $"[AI: Arrive at {to.DisplayName}]"
        };
        endStage.Objectives.Add(new QuestObjective
        {
            RefName = $"{questRefName}_END_OBJ",
            Type = "LocationReached",
            LocationRef = to.RefName
        });
        quest.Stages.Add(endStage);

        // Reward scales with distance
        var rewardMultiplier = Math.Max(1, (int)(distanceKm / (worldSizeKm * 0.1)));
        quest.GlobalRewards.Add(new QuestReward
        {
            Currency = new QuestRewardCurrency { Amount = rewardMultiplier * 200 },
            Condition = "OnSuccess"
        });

        return quest;
    }
}
