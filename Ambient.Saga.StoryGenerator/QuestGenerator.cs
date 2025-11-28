using Ambient.Domain;
using Ambient.StoryGenerator;
using Ambient.Saga.StoryGenerator.QuestStructureGenerators;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.Saga.StoryGenerator.QuestGenerators;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Generates comprehensive multi-stage quests from narrative structure.
/// Refactored to use Strategy pattern (SRP compliance).
/// </summary>
public class QuestGenerator
{
    private readonly RefNameGenerator _refNameGenerator;
    private readonly Random _random;
    private readonly ThemeContent? _theme; // Kept for backwards compat
    private readonly QuestTypeGeneratorRegistry _questTypeRegistry;
    private readonly QuestTypeSelector _questTypeSelector;
    private readonly QuestRewardFactory _rewardFactory;

    public QuestGenerator(RefNameGenerator refNameGenerator, ThemeContent? theme = null, Random? random = null)
    {
        _refNameGenerator = refNameGenerator;
        _theme = theme;
        _random = random ?? new Random(42);

        _rewardFactory = new QuestRewardFactory(theme, _random);
        var itemResolver = new ThemeItemResolver(theme, _random);
        _questTypeSelector = new QuestTypeSelector();
        var context = new QuestGenerationContext(_refNameGenerator, _rewardFactory, itemResolver, _random);
        _questTypeRegistry = QuestTypeGeneratorRegistry.CreateDefault(context);
    }

    /// <summary>
    /// Generate complete quest collection from narrative structure
    /// </summary>
    public List<GeneratedQuest> GenerateQuests(NarrativeStructure narrative, List<OfficialSagaArc>? officialSagaArcs = null)
    {
        var quests = new List<GeneratedQuest>();

        // Official saga arc quests - major story quests connecting hand-crafted content
        if (officialSagaArcs != null && officialSagaArcs.Count > 0)
        {
            quests.AddRange(OfficialSagaQuestGenerator.Generate(officialSagaArcs, narrative));
        }

        // Main story quests - longer, multi-stage quests along main thread
        var mainThread = narrative.StoryThreads.FirstOrDefault(t => t.Type == StoryThreadType.Main);
        if (mainThread != null)
        {
            quests.AddRange(MainStoryQuestGenerator.Generate(mainThread, narrative, _questTypeSelector, GenerateMultiStageQuest));
        }

        // Branch quests - shorter, focused quests on side paths
        foreach (var branchThread in narrative.StoryThreads.Where(t => t.Type == StoryThreadType.Branch))
        {
            quests.AddRange(BranchQuestGenerator.Generate(branchThread, narrative, _questTypeSelector, GenerateMultiStageQuest));
        }

        // Location-specific quests - triggered at specific sagas
        quests.AddRange(LocationQuestGenerator.Generate(narrative, _refNameGenerator, _rewardFactory));

        // Epic quest chains - interconnected legendary quests with item/achievement prerequisites
        quests.AddRange(EpicQuestChainGenerator.Generate(narrative, _refNameGenerator, _rewardFactory, _random, GetRandomCharacterRef, GetRandomItemRef, GetRandomEquipmentRef));

        // Hidden/secret quests - discoverable quests unlocked by rare items or achievements
        quests.AddRange(HiddenQuestGenerator.Generate(narrative, _refNameGenerator, _rewardFactory, _random, GetRandomItemRef, GetRandomEquipmentRef));

        // Filter out any quests with no valid stages
        return quests.Where(q => q.Stages.Count > 0 && q.Stages.Any(s => s.Objectives.Count > 0 || s.Branches.Count > 0)).ToList();
    }






    /// <summary>
    /// Generate a multi-stage quest with varied objectives
    /// </summary>
    private GeneratedQuest GenerateMultiStageQuest(
        string refName,
        string displayName,
        List<SourceLocation> arcLocations,
        QuestType questType,
        double progress,
        NarrativeStructure narrative,
        bool isMainQuest)
    {
        var quest = new GeneratedQuest
        {
            RefName = refName,
            DisplayName = displayName,
            Description = _questTypeSelector.GenerateQuestDescription(questType, arcLocations, progress)
        };

        // Create stages using registry pattern (SRP: each generator has single responsibility)
        var generator = _questTypeRegistry.Get(questType);
        quest.Stages = generator.GenerateStages(arcLocations, narrative, progress);

        // Mark first stage as start
        if (quest.Stages.Count > 0)
        {
            quest.Stages[0].IsStartStage = true;
        }

        return quest;
    }












    //#endregion


    #region Reward Helpers






    /// <summary>
    /// Get a random equipment RefName from theme, or generate a generic one if theme not available
    /// </summary>
    private string GetRandomEquipmentRef(string contextHint)
    {
        if (_theme?.Equipment != null && _theme.Equipment.Count > 0)
        {
            var randomEquipment = _theme.Equipment[_random.Next(_theme.Equipment.Count)];
            return randomEquipment.RefName;
        }
        // Fallback: generate generic RefName from context
        return $"EQUIPMENT_{contextHint.ToUpper().Replace(" ", "_")}";
    }

    /// <summary>
    /// Get a random consumable RefName from theme, or generate a generic one if theme not available
    /// </summary>
    private string GetRandomConsumableRef(string contextHint)
    {
        if (_theme?.Consumables != null && _theme.Consumables.Count > 0)
        {
            var randomConsumable = _theme.Consumables[_random.Next(_theme.Consumables.Count)];
            return randomConsumable.RefName;
        }
        // Fallback: generate generic RefName from context
        return $"CONSUMABLE_{contextHint.ToUpper().Replace(" ", "_")}";
    }

    /// <summary>
    /// Get a random character archetype RefName from theme, or generate a generic one if theme not available
    /// </summary>
    private string GetRandomCharacterRef(string characterType)
    {
        if (_theme?.CharacterArchetypes != null && _theme.CharacterArchetypes.Count > 0)
        {
            // Try to find a matching archetype by type
            var matchingArchetypes = _theme.CharacterArchetypes
                .Where(a => a.RefName.Contains(characterType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingArchetypes.Count > 0)
            {
                return matchingArchetypes[_random.Next(matchingArchetypes.Count)].RefName;
            }

            // No match, return random archetype
            var randomArchetype = _theme.CharacterArchetypes[_random.Next(_theme.CharacterArchetypes.Count)];
            return randomArchetype.RefName;
        }
        // Fallback: generate generic RefName
        return $"NPC_{characterType.ToUpper()}";
    }

    /// <summary>
    /// Get a random item RefName (any type) from theme
    /// </summary>
    private string GetRandomItemRef(string contextHint)
    {
        // Try equipment first (most common quest items)
        if (_theme?.Equipment != null && _theme.Equipment.Count > 0)
        {
            var randomEquipment = _theme.Equipment[_random.Next(_theme.Equipment.Count)];
            return randomEquipment.RefName;
        }
        // Then tools
        if (_theme?.Tools != null && _theme.Tools.Count > 0)
        {
            var randomTool = _theme.Tools[_random.Next(_theme.Tools.Count)];
            return randomTool.RefName;
        }
        // Fallback: generate generic RefName
        return $"ITEM_{contextHint.ToUpper().Replace(" ", "_")}";
    }






    #endregion
}

/// <summary>
/// Quest types for generation
/// </summary>
public enum QuestType
{
    Combat,
    Exploration,
    Collection,
    Dialogue,
    Hybrid,
    Escort,      // Protect NPC traveling between locations
    Defense,     // Defend location from waves of enemies
    Discovery,   // Find hidden locations or secrets
    Puzzle,      // Solve environmental puzzles
    Crafting,    // Craft specific items
    Trading      // Trade items with merchants
}

/// <summary>
/// Generated quest with full multi-stage structure
/// </summary>
public class GeneratedQuest
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<QuestStage> Stages { get; set; } = new();
    public List<QuestPrerequisite> Prerequisites { get; set; } = new();
    public List<QuestFailCondition> FailConditions { get; set; } = new(); // Global fail conditions
    public List<QuestReward> GlobalRewards { get; set; } = new(); // Quest completion rewards
}

/// <summary>
/// Quest stage with objectives and rewards
/// </summary>
public class QuestStage
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsStartStage { get; set; }
    public List<QuestObjective> Objectives { get; set; } = new();
    public List<QuestReward> Rewards { get; set; } = new();
    public List<QuestBranch> Branches { get; set; } = new();
    public string NextStage { get; set; } = string.Empty;
    public List<QuestFailCondition> FailConditions { get; set; } = new(); // Stage-specific fail conditions
}

/// <summary>
/// Quest objective
/// </summary>
public class QuestObjective
{
    public string RefName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Target filters
    public string CharacterRef { get; set; } = string.Empty;
    public string CharacterTag { get; set; } = string.Empty;
    public string CharacterType { get; set; } = string.Empty;
    public string DialogueRef { get; set; } = string.Empty;
    public string ChoiceRef { get; set; } = string.Empty;
    public string NodeRef { get; set; } = string.Empty;
    public string ItemRef { get; set; } = string.Empty;
    public string LocationRef { get; set; } = string.Empty;
    public string SagaArcRef { get; set; } = string.Empty;
    public string TriggerRef { get; set; } = string.Empty;
    public string QuestTokenRef { get; set; } = string.Empty;

    // Progress tracking
    public int Threshold { get; set; } = 1;
    public bool Optional { get; set; } = false;
    public bool Hidden { get; set; } = false;
}

/// <summary>
/// Quest reward
/// </summary>
public class QuestReward
{
    public QuestRewardCurrency? Currency { get; set; }
    public QuestRewardEquipment[]? Equipment { get; set; }
    public QuestRewardConsumable[]? Consumable { get; set; }
    public QuestRewardQuestToken[]? QuestToken { get; set; }
    public QuestRewardExperience? Experience { get; set; }
    public QuestRewardReputation[]? Reputation { get; set; }
    public List<string> Achievements { get; set; } = new(); // Achievement RefNames
    public string Condition { get; set; } = "OnSuccess"; // OnSuccess, OnFailure, OnBranch, OnObjective
    public string BranchRef { get; set; } = string.Empty; // For OnBranch condition
    public string ObjectiveRef { get; set; } = string.Empty; // For OnObjective condition
}

/// <summary>
/// Quest reward components
/// </summary>
public class QuestRewardCurrency
{
    public int Amount { get; set; }
}

public class QuestRewardEquipment
{
    public string RefName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

public class QuestRewardConsumable
{
    public string RefName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

public class QuestRewardQuestToken
{
    public string RefName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

public class QuestRewardExperience
{
    public int Amount { get; set; }
}

public class QuestRewardReputation
{
    public string FactionRef { get; set; } = string.Empty;
    public int Amount { get; set; }
}

/// <summary>
/// Quest fail condition
/// </summary>
public class QuestFailCondition
{
    public string Type { get; set; } = string.Empty; // CharacterDied, TimeExpired, ItemLost, LocationLeft, WrongChoiceMade
    public string CharacterRef { get; set; } = string.Empty;
    public string ItemRef { get; set; } = string.Empty;
    public string LocationRef { get; set; } = string.Empty;
    public int TimeLimit { get; set; } = 0; // In seconds
}

/// <summary>
/// Quest branch (for branching quests)
/// </summary>
public class QuestBranch
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LeadsToStage { get; set; } = string.Empty;
}

/// <summary>
/// Quest prerequisite
/// </summary>
public class QuestPrerequisite
{
    public string QuestRef { get; set; } = string.Empty;
    public int MinimumLevel { get; set; } = 0;
    public string RequiredItemRef { get; set; } = string.Empty;
    public string RequiredAchievementRef { get; set; } = string.Empty;
    public string FactionRef { get; set; } = string.Empty;
    public string RequiredReputationLevel { get; set; } = string.Empty; // Friendly, Honored, Revered, Exalted
}

/// <summary>
/// Represents an Official (hand-crafted) SagaArc loaded from existing world content
/// These are major story points that quests should connect
/// </summary>
public class OfficialSagaArc
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SourceLocation Location { get; set; } = null!;
}
