using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Generates discovery-focused quest stages.
/// Extracted from QuestGenerator.GenerateDiscoveryStages()
/// </summary>
public class DiscoveryQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Discovery;

    public DiscoveryQuestGenerator(QuestGenerationContext context)
    {
        _context = context;
    }

    public List<QuestStage> GenerateStages(
        List<SourceLocation> locations,
        NarrativeStructure narrative,
        double progress)
    {
        var stages = new List<QuestStage>();

                if (locations.Count < 2)
                    return stages;

                // Stage 1: Find clues
                stages.Add(new QuestStage
                {
                    RefName = "FIND_CLUES",
                    DisplayName = "Search for Clues",
                    Objectives = locations.Take(3).Select((loc, idx) => new QuestObjective
                    {
                        RefName = $"FIND_CLUE_{idx + 1}",
                        Type = "LocationReached",
                        DisplayName = $"Discover clue at {loc.DisplayName}",
                        SagaArcRef = _context.RefNameGenerator.GetRefName(loc),
                        Threshold = 1
                    }).ToList()
                });

                // Stage 2: Decode the mystery
                stages.Add(new QuestStage
                {
                    RefName = "DECODE_MYSTERY",
                    DisplayName = "Decode the Mystery",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "SOLVE_PUZZLE",
                            Type = "QuestTokenAwarded",
                            DisplayName = "Solve the puzzle",
                            ItemRef = "TOKEN_PUZZLE_SOLUTION",
                            Threshold = 1
                        }
                    }
                });

                // Stage 3: Claim the discovery
                var finalLocation = locations.Last();
                stages.Add(new QuestStage
                {
                    RefName = "CLAIM_DISCOVERY",
                    DisplayName = "Claim the Discovery",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "REACH_SECRET_LOCATION",
                            Type = "LocationReached",
                            DisplayName = $"Discover the secret at {finalLocation.DisplayName}",
                            SagaArcRef = _context.RefNameGenerator.GetRefName(finalLocation),
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(900 + (int)(progress * 1800)),
                        _context.RewardFactory.CreateEquipmentReward("Generate rare artifact found in hidden location", 1),
                        _context.RewardFactory.CreateQuestTokenReward("TOKEN_DISCOVERER", 1)
                    }
                });

                // Link stages
                for (var i = 0; i < stages.Count - 1; i++)
                {
                    stages[i].NextStage = stages[i + 1].RefName;
                }

                return stages;
    }
}
