using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates CombatStances XML content.
/// Extracted from StoryGenerator.GenerateCombatStancesXml()
/// </summary>
public class CombatStancesXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "CombatStances";

    public CombatStancesXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "CombatStances",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Balanced
                root.Add(new XElement(ns + "CombatStance",
                    new XAttribute("RefName", "Balanced"),
                    new XAttribute("DisplayName", "Balanced"),
                    new XAttribute("Description", "Well-rounded stance with no significant strengths or weaknesses"),
                    new XElement(ns + "Effects",
                        new XAttribute("Strength", "1.0"),
                        new XAttribute("Defense", "1.0"),
                        new XAttribute("Speed", "1.0"),
                        new XAttribute("Magic", "1.0"))
                ));

                // Defensive
                root.Add(new XElement(ns + "CombatStance",
                    new XAttribute("RefName", "Defensive"),
                    new XAttribute("DisplayName", "Defensive"),
                    new XAttribute("Description", "Focus on defense and survival at the cost of attack power"),
                    new XElement(ns + "Effects",
                        new XAttribute("Strength", "0.8"),
                        new XAttribute("Defense", "1.3"),
                        new XAttribute("Speed", "0.9"),
                        new XAttribute("Magic", "0.9"))
                ));

                // Offensive
                root.Add(new XElement(ns + "CombatStance",
                    new XAttribute("RefName", "Offensive"),
                    new XAttribute("DisplayName", "Offensive"),
                    new XAttribute("Description", "Aggressive stance that deals more damage but lowers defenses"),
                    new XElement(ns + "Effects",
                        new XAttribute("Strength", "1.3"),
                        new XAttribute("Defense", "0.7"),
                        new XAttribute("Speed", "1.0"),
                        new XAttribute("Magic", "1.1"))
                ));

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
