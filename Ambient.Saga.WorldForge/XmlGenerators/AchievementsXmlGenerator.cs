using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates Achievements XML content.
/// Extracted from StoryGenerator.GenerateAchievementsXml()
/// </summary>
public class AchievementsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Achievements";

    public AchievementsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "Achievements",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Generate achievements based on context.Narrative structure

                // 1. Main quest completion achievement
                var mainThread = context.Narrative.StoryThreads.FirstOrDefault(t => t.Type == StoryThreadType.Main);
                if (mainThread != null && mainThread.Locations.Count > 0)
                {
                    var mainAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_COMPLETE_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", $"Hero of {context.WorldConfig.RefName}"),
                        new XAttribute("Description", $"Completed the main quest in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "SagaArcsCompleted"),
                            new XAttribute("Threshold", mainThread.Locations.Count.ToString())
                        )
                    );
                    root.Add(mainAchievement);
                }

                // 2. Boss defeat achievements
                var bossPlacements = context.Narrative.CharacterPlacements.Where(p => p.CharacterType == "Boss").ToList();
                if (bossPlacements.Count > 0)
                {
                    var bossAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_DEFEAT_ALL_BOSSES_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Champion"),
                        new XAttribute("Description", $"Defeated all bosses in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "CharactersDefeatedByType"),
                            new XAttribute("CharacterType", "Boss"),
                            new XAttribute("Threshold", bossPlacements.Count.ToString())
                        )
                    );
                    root.Add(bossAchievement);
                }

                // 3. Explorer achievement (discover all locations)
                var totalLocations = context.Narrative.StoryThreads.SelectMany(t => t.Locations).Distinct().Count();
                if (totalLocations > 0)
                {
                    var explorerAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_EXPLORER_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Explorer"),
                        new XAttribute("Description", $"Discovered all locations in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "SagaArcsDiscovered"),
                            new XAttribute("Threshold", totalLocations.ToString())
                        )
                    );
                    root.Add(explorerAchievement);
                }

                // 4. Social butterfly achievement (talk to all characters)
                var totalCharacters = context.Narrative.CharacterPlacements.Count;
                if (totalCharacters > 0)
                {
                    var socialAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_SOCIAL_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Social Butterfly"),
                        new XAttribute("Description", $"Met all characters in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "UniqueCharactersMet"),
                            new XAttribute("Threshold", totalCharacters.ToString())
                        )
                    );
                    root.Add(socialAchievement);
                }

                // 5. Dialogue master achievement (complete all dialogue trees)
                if (totalCharacters > 0)
                {
                    var dialogueAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_DIALOGUE_MASTER_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Master Conversationalist"),
                        new XAttribute("Description", $"Completed all dialogue trees in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "DialogueTreesCompleted"),
                            new XAttribute("Threshold", totalCharacters.ToString())
                        )
                    );
                    root.Add(dialogueAchievement);
                }

                // 6. Token collector achievement (earn all quest tokens)
                var totalTokens = context.Narrative.TokenChains.Count;
                if (totalTokens > 0)
                {
                    var tokenAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_COLLECTOR_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Completionist"),
                        new XAttribute("Description", $"Earned all quest tokens in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "QuestTokensEarned"),
                            new XAttribute("Threshold", totalTokens.ToString())
                        )
                    );
                    root.Add(tokenAchievement);
                }

                // 7. Merchant achievement (trade with all merchants)
                var merchantCount = context.Narrative.CharacterPlacements.Count(p => p.CharacterType == "Merchant");
                if (merchantCount > 0)
                {
                    var merchantAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_MERCHANT_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Master Trader"),
                        new XAttribute("Description", $"Traded with all merchants in {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "ItemsTraded"),
                            new XAttribute("Threshold", (merchantCount * 5).ToString())
                        )
                    );
                    root.Add(merchantAchievement);
                }

                // 8. Diplomat achievement (assign positive traits)
                if (totalCharacters > 0)
                {
                    var diplomatAchievement = new XElement(ns + "Achievement",
                        new XAttribute("RefName", $"ACH_DIPLOMAT_{context.WorldConfig.RefName.ToUpper()}"),
                        new XAttribute("DisplayName", "Diplomat"),
                        new XAttribute("Description", $"Made friends throughout {context.WorldConfig.RefName}"),
                        new XElement(ns + "Criteria",
                            new XAttribute("Type", "TraitsAssignedByType"),
                            new XAttribute("TraitType", "Friendly"),
                            new XAttribute("Threshold", Math.Max(3, totalCharacters / 2).ToString())
                        )
                    );
                    root.Add(diplomatAchievement);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
