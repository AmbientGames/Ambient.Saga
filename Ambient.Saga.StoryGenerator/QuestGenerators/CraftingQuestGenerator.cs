using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Generates crafting-focused quest stages.
/// Extracted from QuestGenerator.GenerateCraftingStages()
/// </summary>
public class CraftingQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Crafting;

    public CraftingQuestGenerator(QuestGenerationContext context)
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

                // Stage 1: Find the master craftsman
                var craftLocation = locations.FirstOrDefault(l => l.Type == SourceLocationType.Structure) ?? locations.FirstOrDefault();
                if (craftLocation != null)
                {
                    var craftsman = narrative.CharacterPlacements.FirstOrDefault(cp =>
                        _context.RefNameGenerator.GetRefName(cp.Location) == _context.RefNameGenerator.GetRefName(craftLocation) &&
                        cp.CharacterType == "Merchant");

                    stages.Add(new QuestStage
                    {
                        RefName = "FIND_CRAFTSMAN",
                        DisplayName = "Find the Master Craftsman",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "MEET_CRAFTSMAN",
                                Type = "DialogueCompleted",
                                DisplayName = $"Speak with the craftsman at {craftLocation.DisplayName}",
                                CharacterRef = craftsman?.CharacterRefName ?? _context.ItemResolver.GetRandomCharacterRef("Craftsman"),
                                Threshold = 1
                            }
                        }
                    });
                }

                // Stage 2: Gather crafting materials
                var materialCount = 3 + (int)(progress * 4); // 3-7 materials depending on difficulty
                stages.Add(new QuestStage
                {
                    RefName = "GATHER_MATERIALS",
                    DisplayName = "Gather Crafting Materials",
                    Objectives = Enumerable.Range(1, Math.Min(3, locations.Count)).Select(i => new QuestObjective
                    {
                        RefName = $"COLLECT_MATERIAL_{i}",
                        Type = "ItemCollected",
                        DisplayName = $"Gather material type {i}",
                        ItemRef = _context.ItemResolver.GetRandomItemRef($"CraftingMaterial{i}"),
                        Threshold = materialCount
                    }).ToList(),
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateExperienceReward(300 + (int)(progress * 600))
                    }
                });

                // Stage 3: Craft the item
                stages.Add(new QuestStage
                {
                    RefName = "CRAFT_ITEM",
                    DisplayName = "Craft the Masterwork",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "CRAFT_MASTERWORK",
                            Type = "ItemCrafted",
                            DisplayName = "Craft the legendary item",
                            ItemRef = _context.ItemResolver.GetRandomEquipmentRef("Masterwork"),
                            Threshold = 1
                        },
                        new QuestObjective
                        {
                            RefName = "BONUS_PERFECT_CRAFT",
                            Type = "Custom",
                            DisplayName = "Craft with 100% quality",
                            Optional = true
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(1000 + (int)(progress * 2000)),
                        _context.RewardFactory.CreateExperienceReward(800 + (int)(progress * 1500)),
                        _context.RewardFactory.CreateEquipmentReward("Generate additional masterwork equipment as reward", 1)
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
