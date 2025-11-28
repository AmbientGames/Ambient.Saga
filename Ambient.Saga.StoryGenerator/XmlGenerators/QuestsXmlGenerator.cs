using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates Quests XML content.
/// Extracted from StoryGenerator.GenerateQuestsXml()
/// </summary>
public class QuestsXmlGenerator : IXmlContentGenerator
{
    private readonly QuestGenerator _questGenerator;

    public string GeneratorName => "Quests";

    public QuestsXmlGenerator(QuestGenerator questGenerator)
    {
        _questGenerator = questGenerator;
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "Quests",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Load Official SagaArcs from WorldConfiguration SourceLocations (major story points)
                List<OfficialSagaArc>? officialSagas = null;
                if (context.SourceLocations != null && context.SourceLocations.Length > 0)
                {
                    officialSagas = context.SourceLocations
                        .Select(sl => new OfficialSagaArc
                        {
                            RefName = context.RefNameGenerator.GetOrGenerateRefName(sl),
                            DisplayName = sl.DisplayName,
                            Description = sl.Description ?? sl.DisplayName,
                            Location = sl
                        })
                        .ToList();
                }

                // Use comprehensive quest generator with existing RefNameGenerator and context.Theme
                // Official sagas will be used for major quest chains, generated sagas for filler
                var _questGenerator = new QuestGenerator(context.RefNameGenerator, context.Theme);
                var generatedQuests = _questGenerator.GenerateQuests(context.Narrative, officialSagas);

                // Convert to XML
                foreach (var quest in generatedQuests)
                {
                    var questElement = new XElement(ns + "Quest",
                        new XAttribute("RefName", quest.RefName),
                        new XAttribute("DisplayName", quest.DisplayName),
                        new XAttribute("Description", quest.Description)
                    );

                    // Add prerequisites
                    if (quest.Prerequisites.Count > 0)
                    {
                        var prerequisitesElement = new XElement(ns + "Prerequisites");
                        foreach (var prereq in quest.Prerequisites)
                        {
                            var prereqElement = new XElement(ns + "Prerequisite");

                            // Add attributes only if they have values
                            if (!string.IsNullOrEmpty(prereq.QuestRef))
                                prereqElement.Add(new XAttribute("QuestRef", prereq.QuestRef));
                            if (prereq.MinimumLevel > 0)
                                prereqElement.Add(new XAttribute("MinimumLevel", prereq.MinimumLevel));
                            if (!string.IsNullOrEmpty(prereq.RequiredItemRef))
                                prereqElement.Add(new XAttribute("RequiredItemRef", prereq.RequiredItemRef));
                            if (!string.IsNullOrEmpty(prereq.RequiredAchievementRef))
                                prereqElement.Add(new XAttribute("RequiredAchievementRef", prereq.RequiredAchievementRef));
                            if (!string.IsNullOrEmpty(prereq.FactionRef))
                                prereqElement.Add(new XAttribute("FactionRef", prereq.FactionRef));
                            if (!string.IsNullOrEmpty(prereq.RequiredReputationLevel))
                                prereqElement.Add(new XAttribute("RequiredReputationLevel", prereq.RequiredReputationLevel));

                            prerequisitesElement.Add(prereqElement);
                        }
                        questElement.Add(prerequisitesElement);
                    }

                    // Add stages
                    var stagesElement = new XElement(ns + "Stages");
                    if (quest.Stages.Count > 0)
                    {
                        // Set StartStage attribute
                        var startStage = quest.Stages.FirstOrDefault(s => s.IsStartStage);
                        if (startStage != null)
                        {
                            stagesElement.Add(new XAttribute("StartStage", startStage.RefName));
                        }

                        foreach (var stage in quest.Stages)
                        {
                            var stageElement = new XElement(ns + "Stage",
                                new XAttribute("RefName", stage.RefName),
                                new XAttribute("DisplayName", stage.DisplayName)
                            );

                            // Note: Description attribute not allowed on QuestStage per schema

                            // Add objectives
                            if (stage.Objectives.Count > 0)
                            {
                                var objectivesElement = new XElement(ns + "Objectives");
                                foreach (var objective in stage.Objectives)
                                {
                                    var objElement = new XElement(ns + "Objective",
                                        new XAttribute("RefName", objective.RefName),
                                        new XAttribute("Type", objective.Type),
                                        new XAttribute("Threshold", objective.Threshold),
                                        new XAttribute("DisplayName", objective.DisplayName)
                                    );

                                    // Add optional attributes
                                    if (!string.IsNullOrEmpty(objective.SagaArcRef))
                                        objElement.Add(new XAttribute("SagaArcRef", objective.SagaArcRef));
                                    if (!string.IsNullOrEmpty(objective.CharacterRef))
                                        objElement.Add(new XAttribute("CharacterRef", objective.CharacterRef));
                                    if (!string.IsNullOrEmpty(objective.CharacterTag))
                                        objElement.Add(new XAttribute("CharacterTag", objective.CharacterTag));
                                    if (!string.IsNullOrEmpty(objective.ItemRef))
                                        objElement.Add(new XAttribute("ItemRef", objective.ItemRef));
                                    if (objective.Optional)
                                        objElement.Add(new XAttribute("Optional", "true"));
                                    if (objective.Hidden)
                                        objElement.Add(new XAttribute("Hidden", "true"));

                                    objectivesElement.Add(objElement);
                                }
                                stageElement.Add(objectivesElement);
                            }

                            // Add rewards
                            if (stage.Rewards.Count > 0)
                            {
                                var rewardsElement = new XElement(ns + "Rewards");
                                foreach (var reward in stage.Rewards)
                                {
                                    var rewardElement = new XElement(ns + "Reward",
                                        new XAttribute("Condition", reward.Condition)
                                    );

                                    // Add conditional attributes
                                    if (!string.IsNullOrEmpty(reward.BranchRef))
                                        rewardElement.Add(new XAttribute("BranchRef", reward.BranchRef));
                                    if (!string.IsNullOrEmpty(reward.ObjectiveRef))
                                        rewardElement.Add(new XAttribute("ObjectiveRef", reward.ObjectiveRef));

                                    // Currency
                                    if (reward.Currency != null)
                                    {
                                        rewardElement.Add(new XElement(ns + "Currency",
                                            new XAttribute("Amount", reward.Currency.Amount)
                                        ));
                                    }

                                    // Equipment
                                    if (reward.Equipment != null)
                                    {
                                        foreach (var equip in reward.Equipment)
                                        {
                                            rewardElement.Add(new XElement(ns + "Equipment",
                                                new XAttribute("EquipmentRef", equip.RefName),
                                                new XAttribute("Quantity", equip.Quantity)
                                            ));
                                        }
                                    }

                                    // Consumables
                                    if (reward.Consumable != null)
                                    {
                                        foreach (var consumable in reward.Consumable)
                                        {
                                            rewardElement.Add(new XElement(ns + "Consumable",
                                                new XAttribute("ConsumableRef", consumable.RefName),
                                                new XAttribute("Quantity", consumable.Quantity)
                                            ));
                                        }
                                    }

                                    // Quest tokens
                                    if (reward.QuestToken != null)
                                    {
                                        foreach (var token in reward.QuestToken)
                                        {
                                            rewardElement.Add(new XElement(ns + "QuestToken",
                                                new XAttribute("QuestTokenRef", token.RefName),
                                                new XAttribute("Quantity", token.Quantity)
                                            ));
                                        }
                                    }

                                    // Experience
                                    if (reward.Experience != null)
                                    {
                                        rewardElement.Add(new XElement(ns + "Experience",
                                            new XAttribute("Amount", reward.Experience.Amount)
                                        ));
                                    }

                                    // Reputation
                                    if (reward.Reputation != null)
                                    {
                                        foreach (var reputation in reward.Reputation)
                                        {
                                            rewardElement.Add(new XElement(ns + "Reputation",
                                                new XAttribute("FactionRef", reputation.FactionRef),
                                                new XAttribute("Amount", reputation.Amount)
                                            ));
                                        }
                                    }

                                    // Achievements
                                    if (reward.Achievements != null && reward.Achievements.Count > 0)
                                    {
                                        foreach (var achievement in reward.Achievements)
                                        {
                                            rewardElement.Add(new XElement(ns + "Achievement",
                                                new XAttribute("AchievementRef", achievement)
                                            ));
                                        }
                                    }

                                    rewardsElement.Add(rewardElement);
                                }
                                stageElement.Add(rewardsElement);
                            }

                            // Add fail conditions
                            if (stage.FailConditions != null && stage.FailConditions.Count > 0)
                            {
                                var failConditionsElement = new XElement(ns + "FailConditions");
                                foreach (var failCondition in stage.FailConditions)
                                {
                                    var failElement = new XElement(ns + "FailCondition",
                                        new XAttribute("Type", failCondition.Type)
                                    );

                                    if (!string.IsNullOrEmpty(failCondition.CharacterRef))
                                        failElement.Add(new XAttribute("CharacterRef", failCondition.CharacterRef));
                                    if (!string.IsNullOrEmpty(failCondition.ItemRef))
                                        failElement.Add(new XAttribute("ItemRef", failCondition.ItemRef));
                                    if (!string.IsNullOrEmpty(failCondition.LocationRef))
                                        failElement.Add(new XAttribute("LocationRef", failCondition.LocationRef));
                                    if (failCondition.TimeLimit > 0)
                                        failElement.Add(new XAttribute("TimeLimit", failCondition.TimeLimit));

                                    failConditionsElement.Add(failElement);
                                }
                                stageElement.Add(failConditionsElement);
                            }

                            // Add branches (for branching quests)
                            if (stage.Branches.Count > 0)
                            {
                                var branchesElement = new XElement(ns + "Branches");
                                foreach (var branch in stage.Branches)
                                {
                                    branchesElement.Add(new XElement(ns + "Branch",
                                        new XAttribute("RefName", branch.RefName),
                                        new XAttribute("DisplayName", branch.DisplayName),
                                        new XAttribute("LeadsToStage", branch.LeadsToStage)
                                    ));
                                }
                                stageElement.Add(branchesElement);
                            }

                            // Add stage advancement
                            if (!string.IsNullOrEmpty(stage.NextStage))
                            {
                                stageElement.Add(new XAttribute("NextStage", stage.NextStage));
                            }

                            stagesElement.Add(stageElement);
                        }
                    }
                    questElement.Add(stagesElement);

                    // Add global rewards (quest completion rewards)
                    if (quest.GlobalRewards != null && quest.GlobalRewards.Count > 0)
                    {
                        var globalRewardsElement = new XElement(ns + "Rewards");
                        foreach (var reward in quest.GlobalRewards)
                        {
                            var rewardElement = new XElement(ns + "Reward",
                                new XAttribute("Condition", reward.Condition)
                            );

                            if (reward.Currency != null)
                                rewardElement.Add(new XElement(ns + "Currency", new XAttribute("Amount", reward.Currency.Amount)));
                            if (reward.Equipment != null)
                                foreach (var equip in reward.Equipment)
                                    rewardElement.Add(new XElement(ns + "Equipment", new XAttribute("EquipmentRef", equip.RefName), new XAttribute("Quantity", equip.Quantity)));
                            if (reward.Consumable != null)
                                foreach (var consumable in reward.Consumable)
                                    rewardElement.Add(new XElement(ns + "Consumable", new XAttribute("ConsumableRef", consumable.RefName), new XAttribute("Quantity", consumable.Quantity)));
                            if (reward.QuestToken != null)
                                foreach (var token in reward.QuestToken)
                                    rewardElement.Add(new XElement(ns + "QuestToken", new XAttribute("QuestTokenRef", token.RefName), new XAttribute("Quantity", token.Quantity)));
                            if (reward.Experience != null)
                                rewardElement.Add(new XElement(ns + "Experience", new XAttribute("Amount", reward.Experience.Amount)));
                            if (reward.Reputation != null)
                                foreach (var reputation in reward.Reputation)
                                    rewardElement.Add(new XElement(ns + "Reputation", new XAttribute("FactionRef", reputation.FactionRef), new XAttribute("Amount", reputation.Amount)));
                            if (reward.Achievements != null && reward.Achievements.Count > 0)
                                foreach (var achievement in reward.Achievements)
                                    rewardElement.Add(new XElement(ns + "Achievement", new XAttribute("AchievementRef", achievement)));

                            globalRewardsElement.Add(rewardElement);
                        }
                        questElement.Add(globalRewardsElement);
                    }

                    // Add global fail conditions
                    if (quest.FailConditions != null && quest.FailConditions.Count > 0)
                    {
                        var failConditionsElement = new XElement(ns + "FailConditions");
                        foreach (var failCondition in quest.FailConditions)
                        {
                            var failElement = new XElement(ns + "FailCondition", new XAttribute("Type", failCondition.Type));
                            if (!string.IsNullOrEmpty(failCondition.CharacterRef))
                                failElement.Add(new XAttribute("CharacterRef", failCondition.CharacterRef));
                            if (!string.IsNullOrEmpty(failCondition.ItemRef))
                                failElement.Add(new XAttribute("ItemRef", failCondition.ItemRef));
                            if (!string.IsNullOrEmpty(failCondition.LocationRef))
                                failElement.Add(new XAttribute("LocationRef", failCondition.LocationRef));
                            if (failCondition.TimeLimit > 0)
                                failElement.Add(new XAttribute("TimeLimit", failCondition.TimeLimit));
                            failConditionsElement.Add(failElement);
                        }
                        questElement.Add(failConditionsElement);
                    }

                    root.Add(questElement);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
