using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

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
                    new XAttribute("StrengthMultiplier", "1.0"),
                    new XAttribute("DefenseMultiplier", "1.0"),
                    new XAttribute("SpeedMultiplier", "1.0"),
                    new XAttribute("MagicMultiplier", "1.0")
                ));

                // Defensive
                root.Add(new XElement(ns + "CombatStance",
                    new XAttribute("RefName", "Defensive"),
                    new XAttribute("DisplayName", "Defensive"),
                    new XAttribute("Description", "Focus on defense and survival at the cost of attack power"),
                    new XAttribute("StrengthMultiplier", "0.8"),
                    new XAttribute("DefenseMultiplier", "1.3"),
                    new XAttribute("SpeedMultiplier", "0.9"),
                    new XAttribute("MagicMultiplier", "0.9")
                ));

                // Offensive
                root.Add(new XElement(ns + "CombatStance",
                    new XAttribute("RefName", "Offensive"),
                    new XAttribute("DisplayName", "Offensive"),
                    new XAttribute("Description", "Aggressive stance that deals more damage but lowers defenses"),
                    new XAttribute("StrengthMultiplier", "1.3"),
                    new XAttribute("DefenseMultiplier", "0.7"),
                    new XAttribute("SpeedMultiplier", "1.0"),
                    new XAttribute("MagicMultiplier", "1.1")
                ));

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
