using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Generates hybrid-focused quest stages.
/// Extracted from QuestGenerator.GenerateHybridStages()
/// </summary>
public class HybridQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Hybrid;

    public HybridQuestGenerator(QuestGenerationContext context)
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

                // Mix different quest types for variety
                // Safely split locations into chunks for different quest types
                var explorationLocs = locations.Take(Math.Min(2, locations.Count)).ToList();
                var combatLocs = locations.Skip(1).Take(Math.Min(2, Math.Max(0, locations.Count - 1))).ToList();
                var dialogueLocs = locations.Skip(2).Take(Math.Max(0, locations.Count - 2)).ToList();

                // Instantiate other generators to compose hybrid quest
                var explorationGen = new ExplorationQuestGenerator(_context);
                var combatGen = new CombatQuestGenerator(_context);
                var dialogueGen = new DialogueQuestGenerator(_context);

                var subQuests = new[]
                {
                    explorationGen.GenerateStages(explorationLocs, narrative, progress),
                    combatGen.GenerateStages(combatLocs, narrative, progress),
                    dialogueGen.GenerateStages(dialogueLocs, narrative, progress)
                };

                // Take first stage from each sub-quest
                foreach (var subQuest in subQuests)
                {
                    if (subQuest.Count > 0)
                    {
                        stages.Add(subQuest[0]);
                    }
                }

                // Add final reward stage if we have any stages
                if (stages.Count > 0)
                {
                    stages.Add(new QuestStage
                    {
                        RefName = "COMPLETE",
                        DisplayName = "Quest Complete",
                        Rewards = new List<QuestReward>
                        {
                            _context.RewardFactory.CreateGoldReward(1500 + (int)(progress * 3000)),
                            _context.RewardFactory.CreateEquipmentReward("EpicHybridQuest", 1),
                            _context.RewardFactory.CreateQuestTokenReward($"TOKEN_HERO_{(int)(progress * 10)}", 1)
                        }
                    });

                    // Link all stages
                    for (var i = 0; i < stages.Count - 1; i++)
                    {
                        stages[i].NextStage = stages[i + 1].RefName;
                    }
                }

                return stages;
    }
}
