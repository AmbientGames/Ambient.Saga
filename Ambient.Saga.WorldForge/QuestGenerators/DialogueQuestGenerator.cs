using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Generates dialogue-focused quest stages.
/// Extracted from QuestGenerator.GenerateDialogueStages()
/// </summary>
public class DialogueQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Dialogue;

    public DialogueQuestGenerator(QuestGenerationContext context)
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

                // Find NPCs and merchants in the area
                var npcs = narrative.CharacterPlacements
                    .Where(cp => locations.Any(loc => _context.RefNameGenerator.GetRefName(loc) == _context.RefNameGenerator.GetRefName(cp.Location)))
                    .Where(cp => cp.CharacterType == "NPC" || cp.CharacterType == "Merchant")
                    .Take(3)
                    .ToList();

                // Only create dialogue quest if there are NPCs to talk to
                if (npcs.Count > 0)
                {
                    // Stage 1: Speak with locals
                    stages.Add(new QuestStage
                    {
                        RefName = "SPEAK_WITH_LOCALS",
                        DisplayName = "Speak with Locals",
                        Objectives = npcs.Select((npc, idx) => new QuestObjective
                        {
                            RefName = $"TALK_TO_NPC_{idx + 1}",
                            Type = "DialogueCompleted",
                            DisplayName = $"Speak with {npc.DisplayName}",
                            CharacterRef = npc.CharacterRefName,
                            Threshold = 1
                        }).ToList()
                    });

                    // Stage 2: Make a critical choice
                    var keyNpc = npcs.FirstOrDefault();
                    if (keyNpc != null)
                    {
                        stages.Add(new QuestStage
                        {
                            RefName = "MAKE_CHOICE",
                            DisplayName = "Make Your Decision",
                            Description = $"Choose how to proceed in your conversation with {keyNpc.DisplayName}",
                            Branches = new List<QuestBranch>
                            {
                                new QuestBranch
                                {
                                    RefName = "PEACEFUL",
                                    DisplayName = "Peaceful Resolution",
                                    LeadsToStage = "PEACEFUL_OUTCOME"
                                },
                                new QuestBranch
                                {
                                    RefName = "AGGRESSIVE",
                                    DisplayName = "Aggressive Approach",
                                    LeadsToStage = "AGGRESSIVE_OUTCOME"
                                }
                            }
                        });

                        // Peaceful outcome
                        stages.Add(new QuestStage
                        {
                            RefName = "PEACEFUL_OUTCOME",
                            DisplayName = "Diplomatic Solution",
                            Objectives = new List<QuestObjective>
                            {
                                new QuestObjective
                                {
                                    RefName = "CHOOSE_PEACE",
                                    Type = "DialogueChoiceSelected",
                                    DisplayName = "Select the peaceful dialogue option",
                                    CharacterRef = keyNpc.CharacterRefName,
                                    ItemRef = "DIALOGUE_CHOICE_PEACEFUL",
                                    Threshold = 1
                                },
                                new QuestObjective
                                {
                                    RefName = "COMPLETE_NEGOTIATION",
                                    Type = "DialogueCompleted",
                                    DisplayName = "Complete the negotiation",
                                    CharacterRef = keyNpc.CharacterRefName,
                                    Threshold = 1
                                }
                            },
                            Rewards = new List<QuestReward>
                            {
                                _context.RewardFactory.CreateGoldReward(800 + (int)(progress * 1200)),
                                _context.RewardFactory.CreateExperienceReward(400 + (int)(progress * 800)),
                                _context.RewardFactory.CreateQuestTokenReward("TOKEN_DIPLOMAT", 1)
                            }
                        });
                    }
                    else
                    {
                        // Fallback if no key NPC
                        stages.Add(new QuestStage
                        {
                            RefName = "PEACEFUL_OUTCOME",
                            DisplayName = "Diplomatic Solution",
                            Objectives = new List<QuestObjective>
                            {
                                new QuestObjective
                                {
                                    RefName = "NEGOTIATE",
                                    Type = "DialogueCompleted",
                                    DisplayName = "Negotiate a peaceful resolution",
                                    Threshold = 1
                                }
                            },
                            Rewards = new List<QuestReward>
                            {
                                _context.RewardFactory.CreateGoldReward(800 + (int)(progress * 1200)),
                                _context.RewardFactory.CreateQuestTokenReward("TOKEN_DIPLOMAT", 1)
                            }
                        });
                    }

                    // Aggressive outcome
                    stages.Add(new QuestStage
                    {
                        RefName = "AGGRESSIVE_OUTCOME",
                        DisplayName = "Show of Force",
                        Objectives = new List<QuestObjective>
                        {
                            new QuestObjective
                            {
                                RefName = "INTIMIDATE",
                                Type = "CharactersDefeatedByTag",
                                DisplayName = "Defeat guards",
                                CharacterTag = "guard",
                                Threshold = 3
                            }
                        },
                        Rewards = new List<QuestReward>
                        {
                            _context.RewardFactory.CreateGoldReward(600 + (int)(progress * 1000)),
                            _context.RewardFactory.CreateEquipmentReward("CombatAggressiveApproach", 1)
                        }
                    });

                    // Link first stage to choice stage
                    stages[0].NextStage = stages[1].RefName;
                }

                return stages;
    }
}
