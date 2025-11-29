using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates CharacterArchetypes XML content.
/// Extracted from StoryGenerator.GenerateCharacterArchetypesXml()
/// </summary>
public class CharacterArchetypesXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "CharacterArchetypes";

    public CharacterArchetypesXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "CharacterArchetypes",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Boss pool
                var bossArchetype = new XElement(ns + "CharacterArchetype",
                    new XAttribute("RefName", "RandomBoss"),
                    new XAttribute("DisplayName", "Random Boss"),
                    new XAttribute("Description", "Spawns a random boss character")
                );
                foreach (var placement in context.Narrative.CharacterPlacements.Where(p => p.CharacterType == "Boss"))
                {
                    bossArchetype.Add(new XElement(ns + "CharacterRef", placement.CharacterRefName));
                }
                if (bossArchetype.Elements(ns + "CharacterRef").Any())
                    root.Add(bossArchetype);

                // Merchant pool
                var merchantArchetype = new XElement(ns + "CharacterArchetype",
                    new XAttribute("RefName", "RandomMerchant"),
                    new XAttribute("DisplayName", "Random Merchant"),
                    new XAttribute("Description", "Spawns a random merchant")
                );
                foreach (var placement in context.Narrative.CharacterPlacements.Where(p => p.CharacterType == "Merchant"))
                {
                    merchantArchetype.Add(new XElement(ns + "CharacterRef", placement.CharacterRefName));
                }
                if (merchantArchetype.Elements(ns + "CharacterRef").Any())
                    root.Add(merchantArchetype);

                // Quest giver pool
                var questArchetype = new XElement(ns + "CharacterArchetype",
                    new XAttribute("RefName", "RandomQuestGiver"),
                    new XAttribute("DisplayName", "Random Quest Giver"),
                    new XAttribute("Description", "Spawns a random quest giver")
                );
                foreach (var placement in context.Narrative.CharacterPlacements.Where(p => p.CharacterType == "Quest"))
                {
                    questArchetype.Add(new XElement(ns + "CharacterRef", placement.CharacterRefName));
                }
                if (questArchetype.Elements(ns + "CharacterRef").Any())
                    root.Add(questArchetype);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
