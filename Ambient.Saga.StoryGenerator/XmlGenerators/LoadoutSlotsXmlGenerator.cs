using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates LoadoutSlots XML content.
/// Extracted from StoryGenerator.GenerateLoadoutSlotsXml()
/// </summary>
public class LoadoutSlotsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "LoadoutSlots";

    public LoadoutSlotsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "LoadoutSlots",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Armor slots
                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "Head"),
                    new XAttribute("DisplayName", "Head"),
                    new XAttribute("Category", "Armor")
                ));

                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "Chest"),
                    new XAttribute("DisplayName", "Chest"),
                    new XAttribute("Category", "Armor")
                ));

                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "Legs"),
                    new XAttribute("DisplayName", "Legs"),
                    new XAttribute("Category", "Armor")
                ));

                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "Feet"),
                    new XAttribute("DisplayName", "Feet"),
                    new XAttribute("Category", "Armor")
                ));

                // Weapon slots
                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "LeftHand"),
                    new XAttribute("DisplayName", "Left Hand"),
                    new XAttribute("Category", "Weapon")
                ));

                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "RightHand"),
                    new XAttribute("DisplayName", "Right Hand"),
                    new XAttribute("Category", "Weapon")
                ));

                // Special slots (excluded from round-robin effect processing)
                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "Affinity"),
                    new XAttribute("DisplayName", "Affinity"),
                    new XAttribute("Category", "Special"),
                    new XAttribute("IsSpecial", "true")
                ));

                root.Add(new XElement(ns + "LoadoutSlot",
                    new XAttribute("RefName", "Stance"),
                    new XAttribute("DisplayName", "Stance"),
                    new XAttribute("Category", "Special"),
                    new XAttribute("IsSpecial", "true")
                ));

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
