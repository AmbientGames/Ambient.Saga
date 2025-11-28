using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates QuestTokens XML content.
/// Extracted from StoryGenerator.GenerateQuestTokensXml()
/// </summary>
public class QuestTokensXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "QuestTokens";

    public QuestTokensXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "QuestTokens",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Generate quest tokens for each location in token chain
                foreach (var link in context.Narrative.TokenChains)
                {
                    var token = new XElement(ns + "QuestToken",
                        new XAttribute("RefName", link.TokenAwarded),
                        new XAttribute("DisplayName", $"Completed {link.Location.DisplayName}"),
                        new XAttribute("Description", $"Proof of completing {link.Location.DisplayName}")
                    );
                    root.Add(token);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
