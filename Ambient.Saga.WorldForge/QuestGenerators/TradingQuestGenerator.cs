using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.QuestGenerators;

/// <summary>
/// Generates trading-focused quest stages.
/// Extracted from QuestGenerator.GenerateTradingStages()
/// </summary>
public class TradingQuestGenerator : IQuestTypeGenerator
{
    private readonly QuestGenerationContext _context;

    public QuestType SupportedType => QuestType.Trading;

    public TradingQuestGenerator(QuestGenerationContext context)
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

                // Stage 1: Establish trade route
                var merchants = narrative.CharacterPlacements
                    .Where(cp => cp.CharacterType == "Merchant" && locations.Contains(cp.Location))
                    .Take(3)
                    .ToList();

                if (merchants.Count > 0)
                {
                    stages.Add(new QuestStage
                    {
                        RefName = "ESTABLISH_ROUTE",
                        DisplayName = "Establish Trade Route",
                        Objectives = merchants.Select((merchant, idx) => new QuestObjective
                        {
                            RefName = $"MEET_MERCHANT_{idx + 1}",
                            Type = "DialogueCompleted",
                            DisplayName = $"Establish contact with {merchant.DisplayName}",
                            CharacterRef = merchant.CharacterRefName,
                            Threshold = 1
                        }).ToList()
                    });
                }
                else
                {
                    // Fallback if no merchants
                    stages.Add(new QuestStage
                    {
                        RefName = "ESTABLISH_ROUTE",
                        DisplayName = "Find Trading Partners",
                        Objectives = locations.Take(3).Select((loc, idx) => new QuestObjective
                        {
                            RefName = $"VISIT_MARKET_{idx + 1}",
                            Type = "LocationReached",
                            DisplayName = $"Visit the market at {loc.DisplayName}",
                            SagaArcRef = _context.RefNameGenerator.GetRefName(loc),
                            Threshold = 1
                        }).ToList()
                    });
                }

                // Stage 2: Complete trades
                var tradeCount = 5 + (int)(progress * 10); // 5-15 trades depending on difficulty
                stages.Add(new QuestStage
                {
                    RefName = "COMPLETE_TRADES",
                    DisplayName = "Complete Trade Deals",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "TRADE_GOODS",
                            Type = "ItemTraded",
                            DisplayName = $"Complete {tradeCount} successful trades",
                            Threshold = tradeCount
                        },
                        new QuestObjective
                        {
                            RefName = "PROFIT_GOAL",
                            Type = "CurrencyCollected",
                            DisplayName = $"Earn {1000 + (int)(progress * 3000)} gold from trading",
                            Threshold = 1000 + (int)(progress * 3000)
                        },
                        new QuestObjective
                        {
                            RefName = "BONUS_NO_LOSS",
                            Type = "Custom",
                            DisplayName = "Complete all trades without losing money",
                            Optional = true
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(500 + (int)(progress * 1000)),
                        _context.RewardFactory.CreateExperienceReward(400 + (int)(progress * 800))
                    }
                });

                // Stage 3: Become trading mogul
                stages.Add(new QuestStage
                {
                    RefName = "TRADING_MOGUL",
                    DisplayName = "Become a Trading Mogul",
                    Objectives = new List<QuestObjective>
                    {
                        new QuestObjective
                        {
                            RefName = "RARE_ITEM_TRADE",
                            Type = "ItemTraded",
                            DisplayName = "Trade a legendary item",
                            ItemRef = _context.ItemResolver.GetRandomItemRef("LegendaryTradeable"),
                            Threshold = 1
                        }
                    },
                    Rewards = new List<QuestReward>
                    {
                        _context.RewardFactory.CreateGoldReward(2000 + (int)(progress * 3000)),
                        _context.RewardFactory.CreateExperienceReward(1000 + (int)(progress * 2000)),
                        _context.RewardFactory.CreateEquipmentReward("MerchantExclusive", 1)
                    }
                });

                // Link stages
                for (var i = 0; i < stages.Count - 1; i++)
                {
                    stages[i].NextStage = stages[i + 1].RefName;
                }

                return stages;
    }
}
