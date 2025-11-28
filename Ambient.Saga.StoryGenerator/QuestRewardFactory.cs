using Ambient.Saga.StoryGenerator.Models;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Factory for creating quest rewards with consistent structure.
/// Extracted from QuestGenerator to follow Single Responsibility Principle.
/// </summary>
public class QuestRewardFactory
{
    private readonly ThemeContent? _theme;
    private readonly Random _random;

    public QuestRewardFactory(ThemeContent? theme, Random random)
    {
        _theme = theme;
        _random = random;
    }

    public QuestReward CreateGoldReward(int amount)
    {
        return new QuestReward
        {
            Currency = new QuestRewardCurrency { Amount = amount },
            Condition = "OnSuccess"
        };
    }

    public QuestReward CreateExperienceReward(int amount)
    {
        return new QuestReward
        {
            Experience = new QuestRewardExperience { Amount = amount },
            Condition = "OnSuccess"
        };
    }

    public QuestReward CreateAchievementReward(string achievementRef)
    {
        return new QuestReward
        {
            Achievements = new List<string> { achievementRef },
            Condition = "OnSuccess"
        };
    }

    public QuestReward CreateEquipmentReward(string contextHint, int quantity)
    {
        var equipmentRef = GetRandomEquipmentRef(contextHint);
        return new QuestReward
        {
            Equipment = new[]
            {
                new QuestRewardEquipment
                {
                    RefName = equipmentRef,
                    Quantity = quantity
                }
            },
            Condition = "OnSuccess"
        };
    }

    public QuestReward CreateConsumableReward(string contextHint, int quantity)
    {
        var consumableRef = GetRandomConsumableRef(contextHint);
        return new QuestReward
        {
            Consumable = new[]
            {
                new QuestRewardConsumable
                {
                    RefName = consumableRef,
                    Quantity = quantity
                }
            },
            Condition = "OnSuccess"
        };
    }

    public QuestReward CreateQuestTokenReward(string refName, int quantity)
    {
        return new QuestReward
        {
            QuestToken = new[]
            {
                new QuestRewardQuestToken
                {
                    RefName = refName,
                    Quantity = quantity
                }
            },
            Condition = "OnSuccess"
        };
    }

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
}
