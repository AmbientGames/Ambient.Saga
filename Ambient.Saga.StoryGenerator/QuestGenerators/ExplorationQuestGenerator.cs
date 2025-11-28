using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Generates exploration-focused quest stages.
/// Extracted from QuestGenerator.GenerateExplorationStages()
/// </summary>
public class ExplorationQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Exploration;

    public ExplorationQuestGenerator(QuestGenerationContext context)
    {
        _context = context;
    }

    public List<QuestStage> GenerateStages(
        List<SourceLocation> locations,
        NarrativeStructure narrative,
        double progress)
    {
        var stages = new List<QuestStage>();

                if (locations.Count == 0)
                    return stages;

                // Stage 1: Discover multiple locations
                var landmarks = locations.Where(l => l.Type == SourceLocationType.Landmark).Take(3).ToList();
                if (landmarks.Count > 0)
                {
                    stages.Add(new QuestStage
                    {
                        RefName = "EXPLORE_REGION",
                        DisplayName = "Explore the Region",
                        Objectives = landmarks.Select((loc, idx) => new QuestObjective
                        {
                            RefName = $"DISCOVER_LANDMARK_{idx + 1}",
                            Type = "LocationReached",
                            DisplayName = $"Discover {loc.DisplayName}",
                            SagaArcRef = _context.RefNameGenerator.GetRefName(loc),
                            Threshold = 1
                        }).ToList()
                    });

                    // Stage 2: Gather information (only if we have landmarks to explore)
                    stages.Add(new QuestStage
                    {
                        RefName = "GATHER_INFO",
                        DisplayName = "Gather Information",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "TALK_TO_NPCS",
                                Type = "DialogueCompleted",
                                DisplayName = "Speak with local travelers",
                                Threshold = 3
                            }
                        }
                    });

                    // Stage 3: Return with findings
                    var finalLocation = locations.Last();
                    stages.Add(new QuestStage
                    {
                        RefName = "RETURN",
                        DisplayName = "Report Findings",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "RETURN_LOCATION",
                                Type = "LocationReached",
                                DisplayName = $"Return to {finalLocation.DisplayName}",
                                SagaArcRef = _context.RefNameGenerator.GetRefName(finalLocation),
                                Threshold = 1
                            }
                        },
                        Rewards = new List<QuestReward>
                        {
                            _context.RewardFactory.CreateGoldReward(500 + (int)(progress * 1000)),
                            _context.RewardFactory.CreateQuestTokenReward($"TOKEN_EXPLORER_{locations.Count}", 1)
                        }
                    });

                    // Link stages
                    for (var i = 0; i < stages.Count - 1; i++)
                    {
                        stages[i].NextStage = stages[i + 1].RefName;
                    }
                }

                return stages;
    }
}
