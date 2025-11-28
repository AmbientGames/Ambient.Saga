using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Generates defense-focused quest stages.
/// Extracted from QuestGenerator.GenerateDefenseStages()
/// </summary>
public class DefenseQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Defense;

    public DefenseQuestGenerator(QuestGenerationContext context)
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

                var defendLocation = locations.First();

                // Stage 1: Prepare defenses
                stages.Add(new QuestStage
                {
                    RefName = "PREPARE_DEFENSES",
                    DisplayName = "Prepare Defenses",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "GATHER_SUPPLIES",
                            Type = "ItemCollected",
                            DisplayName = "Gather defensive supplies",
                            ItemRef = _context.ItemResolver.GetRandomItemRef("DefensiveSupplies"),
                            Threshold = 5
                        }
                    }
                });

                // Stage 2: Defend against wave 1
                stages.Add(new QuestStage
                {
                    RefName = "DEFEND_WAVE_1",
                    DisplayName = "Defend Against First Wave",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DEFEAT_WAVE_1",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat first wave of attackers",
                            CharacterTag = "hostile",
                            Threshold = 5 + (int)(progress * 10)
                        }
                    }
                });

                // Stage 3: Defend against wave 2 (stronger)
                stages.Add(new QuestStage
                {
                    RefName = "DEFEND_WAVE_2",
                    DisplayName = "Defend Against Second Wave",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DEFEAT_WAVE_2",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat second wave of attackers",
                            CharacterTag = "hostile",
                            Threshold = 8 + (int)(progress * 12)
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(1000 + (int)(progress * 2500)),
                        _context.RewardFactory.CreateExperienceReward(800 + (int)(progress * 1500)),
                        _context.RewardFactory.CreateEquipmentReward("DefensiveSuccessfulDefense", 1),
                        _context.RewardFactory.CreateQuestTokenReward($"TOKEN_DEFENDER_{_context.RefNameGenerator.GetRefName(defendLocation)}", 1)
                    },
                    FailConditions = new List<QuestFailCondition>
                    {
                        new QuestFailCondition
                        {
                            Type = "TimeExpired",
                            TimeLimit = 600 // 10 minutes to defend
                        },
                        new QuestFailCondition
                        {
                            Type = "LocationLeft",
                            LocationRef = _context.RefNameGenerator.GetRefName(defendLocation)
                        }
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
