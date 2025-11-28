using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestStructureGenerators;

/// <summary>
/// Generates branch quests - shorter, focused optional content.
/// Extracted from QuestGenerator for SRP compliance.
/// </summary>
public static class BranchQuestGenerator
{
    public static List<GeneratedQuest> Generate(
        StoryThread branchThread,
        NarrativeStructure narrative,
        QuestTypeSelector questTypeSelector,
        Func<string, string, List<SourceLocation>, QuestType, double, NarrativeStructure, bool, GeneratedQuest> generateMultiStageQuest)
    {
        var quests = new List<GeneratedQuest>();
        var locations = branchThread.Locations;

        if (locations.Count < 2) return quests;

        var branchName = branchThread.RefName.Replace("BRANCH_", "");
        var questType = questTypeSelector.DetermineQuestType(locations, 0.5);

        var quest = generateMultiStageQuest(
            $"SIDE_QUEST_{branchName}",
            $"The {branchName} Path",
            locations,
            questType,
            0.5,
            narrative,
            false
        );

        quests.Add(quest);
        return quests;
    }
}
