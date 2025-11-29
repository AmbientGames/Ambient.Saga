using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates SagaFeatures XML content.
/// Extracted from StoryGenerator.GenerateSagaFeaturesXml()
/// </summary>
public class SagaFeaturesXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "SagaFeatures";

    public SagaFeaturesXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "SagaFeatures",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Add SagaFeature elements for ALL source locations (unified approach)
                foreach (var sourceLocation in context.UniqueLocations)
                {
                    var refName = context.RefNameGenerator.GetRefName(sourceLocation);

                    // Map SourceLocationType to SagaFeatureType
                    string featureType;
                    switch (sourceLocation.Type)
                    {
                        case SourceLocationType.Structure:
                            featureType = "Structure";
                            break;
                        case SourceLocationType.Landmark:
                            featureType = "Landmark";
                            break;
                        case SourceLocationType.QuestSignpost:
                            featureType = "Quest";
                            break;
                        default:
                            throw new InvalidOperationException($"Unknown SourceLocationType: {sourceLocation.Type}");
                    }

                    var feature = new XElement(ns + "SagaFeature",
                        new XAttribute("RefName", refName),
                        new XAttribute("DisplayName", sourceLocation.DisplayName),
                        new XAttribute("Description", sourceLocation.Description ?? sourceLocation.DisplayName),
                        new XAttribute("Type", featureType)
                    );

                    // Add type-specific attributes and elements
                    if (sourceLocation.Type == SourceLocationType.QuestSignpost)
                    {
                        // Quest-specific attributes
                        feature.Add(new XAttribute("QuestRef", $"QUEST_{refName}"));
                        feature.Add(new XAttribute("Difficulty", "Normal"));
                        feature.Add(new XAttribute("EstimatedDurationMinutes", "15"));
                    }
                    else if (sourceLocation.Type == SourceLocationType.Landmark)
                    {
                        // Landmark-specific attributes
                        feature.Add(new XAttribute("SubType", "Informational"));
                    }

                    // Add Interactable element for all types
                    feature.Add(new XElement(ns + "Interactable",
                        new XElement(ns + "Effects",
                            new XAttribute("Health", "0"),
                            new XAttribute("Stamina", "0"),
                            new XAttribute("Mana", "0"),
                            new XAttribute("Temperature", "0")
                        )
                    ));

                    root.Add(feature);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
