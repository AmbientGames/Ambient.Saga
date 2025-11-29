using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Generates collection-focused quest stages.
/// Extracted from QuestGenerator.GenerateCollectionStages()
/// </summary>
public class CollectionQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Collection;

    public CollectionQuestGenerator(QuestGenerationContext context)
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

                // Stage 1: Collect items from the region
                stages.Add(new QuestStage
                {
                    RefName = "COLLECT_ITEMS",
                    DisplayName = "Gather Resources",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "COLLECT_PRIMARY_RESOURCE",
                            Type = "ItemCollected",
                            DisplayName = "Collect primary resource",
                            ItemRef = _context.ItemResolver.GetRandomItemRef("Resource1"),
                            Threshold = 10 + (int)(progress * 20)
                        },
                        new QuestObjective
                        {
                            RefName = "COLLECT_SECONDARY_RESOURCE",
                            Type = "ItemCollected",
                            DisplayName = "Collect secondary resource",
                            ItemRef = _context.ItemResolver.GetRandomItemRef("Resource2"),
                            Threshold = 10 + (int)(progress * 20)
                        }
                    }
                });

                // Stage 2: Craft or deliver
                stages.Add(new QuestStage
                {
                    RefName = "DELIVER",
                    DisplayName = "Deliver Resources",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "REACH_DROPOFF",
                            Type = "LocationReached",
                            DisplayName = "Deliver to collection point",
                            SagaArcRef = _context.RefNameGenerator.GetRefName(locations.Last()),
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(300 + (int)(progress * 700)),
                        _context.RewardFactory.CreateConsumableReward("HealthPotion", 3)
                    }
                });

                stages[0].NextStage = stages[1].RefName;
                return stages;
    }
}
