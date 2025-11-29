using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Generates combat-focused quest stages.
/// Extracted from QuestGenerator.GenerateCombatStages()
/// </summary>
public class CombatQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Combat;

    public CombatQuestGenerator(QuestGenerationContext context)
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

                // Stage 1: Clear the path
                stages.Add(new QuestStage
                {
                    RefName = "CLEAR_PATH",
                    DisplayName = "Clear the Path",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DEFEAT_ENEMIES",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat hostile creatures",
                            CharacterTag = "hostile",
                            Threshold = 5 + (int)(progress * 10) // Scales with progress
                        }
                    }
                });

                // Stage 2: Reach destination
                var destination = locations.Last();
                var destinationRef = _context.RefNameGenerator.GetRefName(destination);
                stages.Add(new QuestStage
                {
                    RefName = "REACH_DESTINATION",
                    DisplayName = $"Reach {destination.DisplayName}",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "DISCOVER_LOCATION",
                            Type = "LocationReached",
                            DisplayName = $"Discover {destination.DisplayName}",
                            SagaArcRef = destinationRef,
                            Threshold = 1
                        }
                    }
                });

                // Stage 3: Boss battle (if structure present)
                var structure = locations.FirstOrDefault(l => l.Type == SourceLocationType.Structure);
                if (structure != null)
                {
                    var structureRef = _context.RefNameGenerator.GetRefName(structure);
                    var bossPlacement = narrative.CharacterPlacements.FirstOrDefault(cp =>
                        _context.RefNameGenerator.GetRefName(cp.Location) == structureRef && cp.CharacterType == "Boss");

                    if (bossPlacement != null)
                    {
                        stages.Add(new QuestStage
                        {
                            RefName = "BOSS_BATTLE",
                            DisplayName = $"Confront {bossPlacement.DisplayName}",
                            Objectives = new List<QuestObjective>
                            {
                                new QuestObjective
                                {
                                    RefName = "DEFEAT_BOSS",
                                    Type = "CharacterDefeated",
                                    DisplayName = $"Defeat {bossPlacement.DisplayName}",
                                    CharacterRef = bossPlacement.CharacterRefName,
                                    Threshold = 1
                                },
                                new QuestObjective
                                {
                                    RefName = "BONUS_FLAWLESS_VICTORY",
                                    Type = "Custom",
                                    DisplayName = "Defeat boss without using consumables",
                                    Threshold = 1,
                                    Optional = true
                                }
                            },
                            Rewards = new List<QuestReward>
                            {
                                _context.RewardFactory.CreateGoldReward(1000 + (int)(progress * 2000)),
                                _context.RewardFactory.CreateExperienceReward(1000 + (int)(progress * 2000)),
                                _context.RewardFactory.CreateEquipmentReward($"BossDefeat{structureRef}", 1),
                                _context.RewardFactory.CreateQuestTokenReward($"TOKEN_{structureRef}_COMPLETE", 1)
                            }
                        });
                    }
                }

                // Add stage-to-stage transitions
                for (var i = 0; i < stages.Count - 1; i++)
                {
                    stages[i].NextStage = stages[i + 1].RefName;
                }

                return stages;
    }
}
