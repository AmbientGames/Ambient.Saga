using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestStructureGenerators;

/// <summary>
/// Generates main story quests - epic multi-stage quests with branching.
/// Extracted from QuestGenerator for SRP compliance.
/// </summary>
public static class MainStoryQuestGenerator
{
    public static List<GeneratedQuest> Generate(
        StoryThread mainThread,
        NarrativeStructure narrative,
        QuestTypeSelector questTypeSelector,
        Func<string, string, List<SourceLocation>, QuestType, double, NarrativeStructure, bool, GeneratedQuest> generateMultiStageQuest)
    {
        var quests = new List<GeneratedQuest>();
        var locations = mainThread.Locations;

        // Group locations into quest arcs (3-5 locations per quest)
        var arcSize = 4;
        for (var i = 0; i < locations.Count; i += arcSize)
        {
            var arcLocations = locations.Skip(i).Take(arcSize).ToList();
            if (arcLocations.Count < 2) break; // Need at least 2 locations for a quest

            var questNumber = i / arcSize + 1;
            var progress = (double)i / Math.Max(1, locations.Count - 1);
            var questType = questTypeSelector.DetermineQuestType(arcLocations, progress);

            var quest = generateMultiStageQuest(
                $"MAIN_QUEST_{questNumber:D2}",
                questTypeSelector.GenerateQuestTitle(questType, progress, questNumber),
                arcLocations,
                questType,
                progress,
                narrative,
                true
            );

            // Add prerequisites (previous quest must be completed + level requirement)
            if (quests.Count > 0)
            {
                quest.Prerequisites.Add(new QuestPrerequisite
                {
                    QuestRef = quests[quests.Count - 1].RefName,
                    MinimumLevel = (int)(progress * 50) // Level 0-50 based on progress
                });
            }
            else
            {
                // First quest has no previous quest but may have level requirement
                if (progress > 0)
                {
                    quest.Prerequisites.Add(new QuestPrerequisite
                    {
                        MinimumLevel = (int)(progress * 50)
                    });
                }
            }

            quests.Add(quest);
        }

        return quests;
    }
}
