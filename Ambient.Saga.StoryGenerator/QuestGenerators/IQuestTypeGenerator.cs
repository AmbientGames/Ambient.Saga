using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Interface for quest type-specific stage generators.
/// Each quest type (Combat, Exploration, etc.) implements this to generate appropriate stages.
/// </summary>
public interface IQuestTypeGenerator
{
    /// <summary>
    /// The quest type this generator supports
    /// </summary>
    QuestType SupportedType { get; }

    /// <summary>
    /// Generate quest stages for this quest type
    /// </summary>
    /// <param name="locations">Locations to visit in this quest</param>
    /// <param name="narrative">Full narrative structure for context</param>
    /// <param name="progress">Progress through the game (0.0 = start, 1.0 = end)</param>
    /// <returns>List of quest stages</returns>
    List<QuestStage> GenerateStages(
        List<SourceLocation> locations,
        NarrativeStructure narrative,
        double progress);
}
