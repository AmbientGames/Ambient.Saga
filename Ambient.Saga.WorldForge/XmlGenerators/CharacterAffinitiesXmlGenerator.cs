using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates CharacterAffinities XML content by copying from theme.
/// </summary>
public class CharacterAffinitiesXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "CharacterAffinities";

    public CharacterAffinitiesXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
        XNamespace ns = "Ambient.Domain";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        var root = new XElement(ns + "CharacterAffinities",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
        );

        // Copy affinities from theme
        if (context.Theme?.Affinities != null && context.Theme.Affinities.Count > 0)
        {
            foreach (var affinity in context.Theme.Affinities)
            {
                var element = new XElement(ns + "Affinity",
                    new XAttribute("RefName", affinity.RefName ?? ""),
                    new XAttribute("DisplayName", affinity.DisplayName ?? ""),
                    new XAttribute("Description", affinity.Description ?? "")
                );

                // Optional NeutralMultiplier attribute (default is 1.0f)
                if (affinity.NeutralMultiplier != 1.0f)
                {
                    element.Add(new XAttribute("NeutralMultiplier", affinity.NeutralMultiplier));
                }

                // Matchups
                if (affinity.Matchup != null)
                {
                    foreach (var matchup in affinity.Matchup)
                    {
                        element.Add(new XElement(ns + "Matchup",
                            new XAttribute("TargetAffinityRef", matchup.TargetAffinityRef ?? ""),
                            new XAttribute("Multiplier", matchup.Multiplier)
                        ));
                    }
                }

                root.Add(element);
            }
        }
        else
        {
            // Fallback: Generate minimal required affinities if theme has none
            // XSD requires at least one Affinity element
            root.Add(new XElement(ns + "Affinity",
                new XAttribute("RefName", "Physical"),
                new XAttribute("DisplayName", "Physical"),
                new XAttribute("Description", "Physical attacks and defense")
            ));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            root
        );
        doc.Save(outputPath);
    }
}
