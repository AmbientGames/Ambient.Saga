using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.NarrativeServices;

/// <summary>
/// Generates quest chain with dependencies.
/// </summary>
public class QuestChainGenerator
{
    private readonly RefNameGenerator _refNameGenerator;

    public QuestChainGenerator(RefNameGenerator refNameGenerator)
    {
        _refNameGenerator = refNameGenerator;
    }

    public List<QuestChainLink> GenerateQuestChain(List<StoryThread> threads)
    {
        var quests = new List<QuestChainLink>();
        var mainThread = threads.FirstOrDefault(t => t.Type == StoryThreadType.Main);

        if (mainThread != null)
        {
            for (var i = 0; i < mainThread.Locations.Count; i++)
            {
                var location = mainThread.Locations[i];
                if (location.Type == SourceLocationType.QuestSignpost)
                {
                    var refName = _refNameGenerator.GetRefName(location);
                    var quest = new QuestChainLink
                    {
                        QuestRef = $"QUEST_{refName}",
                        DisplayName = $"Quest: {location.DisplayName}",
                        Description = $"[AI: Generate quest objective for {location.DisplayName}]",
                        SequenceNumber = quests.Count,
                        PrerequisiteQuestRefs = new List<string>(),
                        AwardsTokenRef = $"TOKEN_{refName}_COMPLETE"
                    };

                    // Add prerequisite from previous quest
                    if (quests.Count > 0)
                    {
                        quest.PrerequisiteQuestRefs.Add(quests[quests.Count - 1].QuestRef);
                    }

                    quests.Add(quest);
                }
            }
        }

        return quests;
    }
}
