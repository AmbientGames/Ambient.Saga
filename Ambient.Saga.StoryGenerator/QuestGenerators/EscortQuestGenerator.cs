using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Generates escort-focused quest stages.
/// Extracted from QuestGenerator.GenerateEscortStages()
/// </summary>
public class EscortQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Escort;

    public EscortQuestGenerator(QuestGenerationContext context)
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

                var startLocation = locations.First();
                var endLocation = locations.Last();
                var npc = narrative.CharacterPlacements
                    .FirstOrDefault(cp => _context.RefNameGenerator.GetRefName(cp.Location) == _context.RefNameGenerator.GetRefName(startLocation)
                        && cp.CharacterType == "NPC");

                // Stage 1: Accept escort mission
                stages.Add(new QuestStage
                {
                    RefName = "ACCEPT_ESCORT",
                    DisplayName = "Accept Escort Mission",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "MEET_ESCORT_TARGET",
                            Type = "DialogueCompleted",
                            DisplayName = npc != null ? $"Speak with {npc.DisplayName}" : "Speak with traveler",
                            CharacterRef = npc?.CharacterRefName ?? _context.ItemResolver.GetRandomCharacterRef("Traveler"),
                            Threshold = 1
                        }
                    }
                });

                // Stage 2: Escort through dangerous territory
                stages.Add(new QuestStage
                {
                    RefName = "ESCORT_JOURNEY",
                    DisplayName = $"Escort to {endLocation.DisplayName}",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "SURVIVE_AMBUSH",
                            Type = "CharactersDefeatedByTag",
                            DisplayName = "Defeat ambushers",
                            CharacterTag = "hostile",
                            Threshold = 3 + (int)(progress * 5)
                        },
                        new QuestObjective
                        {
                            RefName = "REACH_DESTINATION",
                            Type = "LocationReached",
                            DisplayName = $"Reach {endLocation.DisplayName} safely",
                            SagaArcRef = _context.RefNameGenerator.GetRefName(endLocation),
                            Threshold = 1
                        },
                        new QuestObjective
                        {
                            RefName = "BONUS_NO_DAMAGE",
                            Type = "Custom",
                            DisplayName = "Complete without taking damage",
                            Threshold = 1,
                            Optional = true, // Bonus objective
                            Hidden = false
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(700 + (int)(progress * 1500)),
                        _context.RewardFactory.CreateExperienceReward(500 + (int)(progress * 1000)),
                        _context.RewardFactory.CreateEquipmentReward("ProtectiveEscort", 1)
                    },
                    FailConditions = new List<QuestFailCondition>
                    {
                        new QuestFailCondition
                        {
                            Type = "CharacterDied",
                            CharacterRef = npc?.CharacterRefName ?? _context.ItemResolver.GetRandomCharacterRef("Traveler")
                        }
                    }
                });

                stages[0].NextStage = stages[1].RefName;
                return stages;
    }
}
