using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates SagaTriggerPatterns XML content.
/// Extracted from StoryGenerator.GenerateSagaTriggerPatternsXml()
/// </summary>
public class SagaTriggerPatternsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "SagaTriggerPatterns";

    public SagaTriggerPatternsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "SagaTriggerPatterns",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Peaceful Landmark - just discovery, no combat
                var peacefulLandmark = new XElement(ns + "SagaTriggerPattern",
                    new XAttribute("RefName", "PeacefulLandmark"),
                    new XAttribute("DisplayName", "Peaceful Landmark"),
                    new XAttribute("Description", "Pure exploration - no combat encounters"),
                    new XAttribute("EnforceProgression", "false")
                );
                peacefulLandmark.Add(new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "outer_view"),
                    new XAttribute("DisplayName", "Distant View"),
                    new XAttribute("EnterRadius", "50.0"),
                    new XAttribute("Tags", "exploration")
                ));
                peacefulLandmark.Add(new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "close_view"),
                    new XAttribute("DisplayName", "Close View"),
                    new XAttribute("EnterRadius", "25.0"),
                    new XAttribute("Tags", "exploration")
                ));
                peacefulLandmark.Add(new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "landmark"),
                    new XAttribute("DisplayName", "Landmark Discovered"),
                    new XAttribute("EnterRadius", "10.0"),
                    new XAttribute("Tags", "exploration,discovery")
                ));
                root.Add(peacefulLandmark);

                // Peaceful Structure - just discovery, no combat
                var peacefulStructure = new XElement(ns + "SagaTriggerPattern",
                    new XAttribute("RefName", "PeacefulStructure"),
                    new XAttribute("DisplayName", "Peaceful Structure"),
                    new XAttribute("Description", "Pure exploration - no combat encounters"),
                    new XAttribute("EnforceProgression", "false")
                );
                peacefulStructure.Add(new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "outer_view"),
                    new XAttribute("DisplayName", "Distant View"),
                    new XAttribute("EnterRadius", "50.0"),
                    new XAttribute("Tags", "exploration")
                ));
                peacefulStructure.Add(new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "close_view"),
                    new XAttribute("DisplayName", "Close View"),
                    new XAttribute("EnterRadius", "25.0"),
                    new XAttribute("Tags", "exploration")
                ));
                peacefulStructure.Add(new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "structure"),
                    new XAttribute("DisplayName", "Structure Discovered"),
                    new XAttribute("EnterRadius", "10.0"),
                    new XAttribute("Tags", "exploration,discovery")
                ));
                root.Add(peacefulStructure);

                // Merchant Shop - single merchant spawn
                var merchantShop = new XElement(ns + "SagaTriggerPattern",
                    new XAttribute("RefName", "MerchantShop"),
                    new XAttribute("DisplayName", "Merchant Shop"),
                    new XAttribute("Description", "Trading post with merchant character"),
                    new XAttribute("EnforceProgression", "false")
                );
                var merchantTrigger = new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "shop"),
                    new XAttribute("DisplayName", "Shop"),
                    new XAttribute("EnterRadius", "20.0"),
                    new XAttribute("Tags", "merchant,shop")
                );
                merchantTrigger.Add(new XElement(ns + "Spawn",
                    new XElement(ns + "CharacterArchetypeRef", "RandomMerchant")
                ));
                merchantShop.Add(merchantTrigger);
                root.Add(merchantShop);

                // Boss Encounter - single boss spawn
                var bossEncounter = new XElement(ns + "SagaTriggerPattern",
                    new XAttribute("RefName", "BossEncounter"),
                    new XAttribute("DisplayName", "Boss Encounter Pattern"),
                    new XAttribute("Description", "Single boss encounter"),
                    new XAttribute("EnforceProgression", "false")
                );
                var bossTrigger = new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "boss"),
                    new XAttribute("DisplayName", "Boss Battle"),
                    new XAttribute("EnterRadius", "25.0"),
                    new XAttribute("Tags", "combat,boss")
                );
                bossTrigger.Add(new XElement(ns + "Spawn",
                    new XElement(ns + "CharacterArchetypeRef", "RandomBoss")
                ));
                bossEncounter.Add(bossTrigger);
                root.Add(bossEncounter);

                // Quest Hub - quest giver spawn
                var questHub = new XElement(ns + "SagaTriggerPattern",
                    new XAttribute("RefName", "QuestHub"),
                    new XAttribute("DisplayName", "Quest Hub Pattern"),
                    new XAttribute("Description", "Location where quests are given and completed"),
                    new XAttribute("EnforceProgression", "false")
                );
                var questTrigger = new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "quest_giver"),
                    new XAttribute("DisplayName", "Quest Giver"),
                    new XAttribute("EnterRadius", "20.0"),
                    new XAttribute("Tags", "quest,npc")
                );
                questTrigger.Add(new XElement(ns + "Spawn",
                    new XElement(ns + "CharacterArchetypeRef", "RandomQuestGiver")
                ));
                questHub.Add(questTrigger);
                root.Add(questHub);

                // Simple Encounter - single random enemy
                var simpleEncounter = new XElement(ns + "SagaTriggerPattern",
                    new XAttribute("RefName", "SimpleEncounter"),
                    new XAttribute("DisplayName", "Simple Encounter"),
                    new XAttribute("Description", "Single random enemy encounter"),
                    new XAttribute("EnforceProgression", "false")
                );
                var encounterTrigger = new XElement(ns + "SagaTrigger",
                    new XAttribute("RefName", "enemy"),
                    new XAttribute("DisplayName", "Enemy Appears"),
                    new XAttribute("EnterRadius", "20.0"),
                    new XAttribute("Tags", "combat,enemy")
                );
                encounterTrigger.Add(new XElement(ns + "Spawn",
                    new XElement(ns + "CharacterArchetypeRef", "RandomBoss")
                ));
                simpleEncounter.Add(encounterTrigger);
                root.Add(simpleEncounter);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
