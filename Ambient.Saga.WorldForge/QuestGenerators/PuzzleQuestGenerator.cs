using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Generates puzzle-focused quest stages.
/// Extracted from QuestGenerator.GeneratePuzzleStages()
/// </summary>
public class PuzzleQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Puzzle;

    public PuzzleQuestGenerator(QuestGenerationContext context)
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

                // Stage 1: Discover the puzzle
                var puzzleLocation = locations.FirstOrDefault(l => l.Type == SourceLocationType.Structure || l.Type == SourceLocationType.Landmark);
                if (puzzleLocation != null)
                {
                    stages.Add(new QuestStage
                    {
                        RefName = "DISCOVER_PUZZLE",
                        DisplayName = "Discover the Ancient Puzzle",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "REACH_PUZZLE_SITE",
                                Type = "LocationReached",
                                DisplayName = $"Find the puzzle at {puzzleLocation.DisplayName}",
                                SagaArcRef = _context.RefNameGenerator.GetRefName(puzzleLocation),
                                Threshold = 1
                            }
                        }
                    });
                }

                // Stage 2: Collect puzzle pieces/tokens
                var collectCount = 3 + (int)(progress * 3); // 3-6 pieces depending on difficulty
                stages.Add(new QuestStage
                {
                    RefName = "COLLECT_PUZZLE_PIECES",
                    DisplayName = "Collect Puzzle Components",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "COLLECT_TOKENS",
                            Type = "QuestTokenCollected",
                            DisplayName = $"Collect {collectCount} puzzle pieces",
                            ItemRef = "TOKEN_PUZZLE_PIECE",
                            Threshold = collectCount
                        },
                        new QuestObjective
                        {
                            RefName = "BONUS_SPEED_RUN",
                            Type = "Custom",
                            DisplayName = "Collect all pieces in under 10 minutes",
                            Optional = true
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateExperienceReward(200 + (int)(progress * 400))
                    }
                });

                // Stage 3: Solve the puzzle
                stages.Add(new QuestStage
                {
                    RefName = "SOLVE_PUZZLE",
                    DisplayName = "Solve the Puzzle",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "COMPLETE_PUZZLE",
                            Type = "Custom",
                            DisplayName = "Solve the ancient puzzle",
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(800 + (int)(progress * 1500)),
                        _context.RewardFactory.CreateExperienceReward(600 + (int)(progress * 1200)),
                        _context.RewardFactory.CreateEquipmentReward("Generate wisdom-based equipment (staff, robes, amulet)", 1)
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
