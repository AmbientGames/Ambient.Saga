using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Determines appropriate quest types based on location characteristics and game progress.
/// Also generates quest titles and descriptions.
/// Extracted from QuestGenerator to follow Single Responsibility Principle.
/// </summary>
public class QuestTypeSelector
{
    /// <summary>
    /// Determine quest type based on location characteristics and progress through the game
    /// </summary>
    /// <param name="locations">Locations involved in the quest</param>
    /// <param name="progress">Game progress (0.0 = start, 1.0 = end)</param>
    /// <returns>Appropriate quest type</returns>
    public QuestType DetermineQuestType(List<SourceLocation> locations, double progress)
    {
        // Determine based on location types and progress
        var hasStructure = locations.Any(l => l.Type == SourceLocationType.Structure);
        var hasQuestSignpost = locations.Any(l => l.Type == SourceLocationType.QuestSignpost);
        var landmarkCount = locations.Count(l => l.Type == SourceLocationType.Landmark);
        var locationCount = locations.Count;

        // Use deterministic selection based on progress and location characteristics
        var seed = (int)(progress * 100) + locations.Count;
        var random = new Random(seed);
        var roll = random.Next(100);

        // Early game (0-30% progress): Focus on Collection, Exploration, Discovery, Crafting
        if (progress < 0.3)
        {
            if (landmarkCount >= 3 && roll < 30)
                return QuestType.Discovery; // Hidden secrets
            if (roll < 45)
                return QuestType.Collection; // Resource gathering
            if (roll < 60)
                return QuestType.Crafting; // Learn crafting
            if (roll < 70)
                return QuestType.Trading; // Trade basics
            if (roll < 80)
                return QuestType.Puzzle; // Simple puzzles
            return QuestType.Exploration; // Exploration
        }

        // Mid game (30-70% progress): Mix of quest types
        if (progress < 0.7)
        {
            if (hasStructure && roll < 25)
                return QuestType.Defense; // Defend locations
            if (locationCount >= 3 && roll < 40)
                return QuestType.Escort; // Escort missions
            if (hasQuestSignpost && roll < 55)
                return QuestType.Dialogue; // Social quests
            if (roll < 65)
                return QuestType.Puzzle; // Complex puzzles
            if (roll < 75)
                return QuestType.Trading; // Advanced trading
            if (landmarkCount >= 3)
                return QuestType.Exploration;
            return QuestType.Hybrid;
        }

        // Late game (70%+ progress): Combat-focused, epic quests
        if (hasStructure && roll < 30)
            return QuestType.Combat; // Epic boss battles
        if (roll < 45)
            return QuestType.Defense; // Defend against invasions
        if (roll < 60)
            return QuestType.Puzzle; // Master-level puzzles
        if (roll < 70)
            return QuestType.Crafting; // Legendary crafting
        return QuestType.Hybrid; // Epic multi-type quests
    }

    /// <summary>
    /// Generate quest title based on type, progress, and quest number
    /// </summary>
    public string GenerateQuestTitle(QuestType type, double progress, int questNumber)
    {
        var phase = progress < 0.3 ? "Beginning" : progress < 0.7 ? "Journey" : "Finale";

        return type switch
        {
            QuestType.Combat => $"Trial of Combat: {phase} {questNumber}",
            QuestType.Exploration => $"Path of Discovery: {phase} {questNumber}",
            QuestType.Collection => $"Gathering Quest: {phase} {questNumber}",
            QuestType.Dialogue => $"Diplomatic Mission: {phase} {questNumber}",
            QuestType.Hybrid => $"Epic Quest: {phase} {questNumber}",
            QuestType.Escort => $"Safe Passage: {phase} {questNumber}",
            QuestType.Defense => $"Hold the Line: {phase} {questNumber}",
            QuestType.Discovery => $"Hidden Secrets: {phase} {questNumber}",
            QuestType.Puzzle => $"Riddle of the Ancients: {phase} {questNumber}",
            QuestType.Crafting => $"Master Craftsman: {phase} {questNumber}",
            QuestType.Trading => $"Merchant's Path: {phase} {questNumber}",
            _ => $"Quest {questNumber}"
        };
    }

    /// <summary>
    /// Generate quest description based on type, locations, and progress
    /// </summary>
    public string GenerateQuestDescription(QuestType type, List<SourceLocation> locations, double progress)
    {
        var locationList = string.Join(", ", locations.Select(l => l.DisplayName).Take(3));
        var mood = progress < 0.3 ? "welcoming" : progress < 0.7 ? "challenging" : "climactic";

        return type switch
        {
            QuestType.Combat => $"A {mood} quest of combat and courage, taking you through {locationList}.",
            QuestType.Exploration => $"Explore the mysteries of {locationList} in this {mood} journey of discovery.",
            QuestType.Collection => $"Gather resources and aid the people of {locationList}.",
            QuestType.Dialogue => $"Navigate the complex social landscape of {locationList} through diplomacy and wit.",
            QuestType.Hybrid => $"An epic {mood} adventure through {locationList}, testing all your skills.",
            QuestType.Escort => $"Protect a traveler on their {mood} journey from {locations.FirstOrDefault()?.DisplayName ?? "the starting point"} to {locations.LastOrDefault()?.DisplayName ?? "the destination"}.",
            QuestType.Defense => $"Defend {locations.FirstOrDefault()?.DisplayName ?? "the settlement"} from waves of hostile forces in this {mood} battle.",
            QuestType.Discovery => $"Uncover hidden secrets and lost knowledge scattered across {locationList}.",
            QuestType.Puzzle => $"Solve ancient riddles and puzzles to unlock the mysteries of {locationList}.",
            QuestType.Crafting => $"Master the art of crafting through {mood} challenges, learning from artisans in {locationList}.",
            QuestType.Trading => $"Build your trading empire through {mood} deals and negotiations across {locationList}.",
            _ => $"A quest through {locationList}."
        };
    }
}
