using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates Sagas XML content.
/// Extracted from StoryGenerator.GenerateSagasXml()
/// </summary>
public class SagasXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Sagas";

    public SagasXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "SagaArcs",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Add SagaArc elements for each UNIQUE SourceLocation
                foreach (var sourceLocation in context.UniqueLocations)
                {
                    var refName = context.RefNameGenerator.GetRefName(sourceLocation);
                    var saga = new XElement(ns + "SagaArc",
                        new XAttribute("RefName", refName),
                        new XAttribute("DisplayName", sourceLocation.DisplayName),
                        new XAttribute("Description", sourceLocation.Description ?? sourceLocation.DisplayName),
                        new XAttribute("LatitudeZ", sourceLocation.Lat),
                        new XAttribute("LongitudeX", sourceLocation.Lon),
                        new XAttribute("Source", "Generated")
                    );

                    // Add unified SagaFeatureRef (replaces old StructureRef/LandmarkRef/QuestSignpostRef)
                    saga.Add(new XElement(ns + "SagaFeatureRef", refName));

                    // Create SagaTrigger with token requirements and rewards
                    var sagaTrigger = new XElement(ns + "SagaTrigger",
                        new XAttribute("RefName", $"TRIGGER_{refName}"),
                        new XAttribute("DisplayName", $"Approaching {sourceLocation.DisplayName}"),
                        new XAttribute("Description", $"Triggered when entering {sourceLocation.DisplayName}"),
                        new XAttribute("EnterRadius", "50.0")
                    );

                    // Add elements in XSD order: Spawn, RequiresQuestTokenRef, GivesQuestTokenRef

                    // 1. Spawn (character spawn from context.Narrative)
                    var characterPlacement = context.Narrative.CharacterPlacements.FirstOrDefault(p => context.RefNameGenerator.GetRefName(p.Location) == refName);
                    if (characterPlacement != null)
                    {
                        sagaTrigger.Add(new XElement(ns + "Spawn",
                            new XElement(ns + "CharacterRef", characterPlacement.CharacterRefName),
                            new XAttribute("Count", "1")
                        ));
                    }

                    // 2. RequiresQuestTokenRef (from previous locations in chain)
                    var tokenLink = context.Narrative.TokenChains.FirstOrDefault(t => context.RefNameGenerator.GetRefName(t.Location) == refName);
                    if (tokenLink != null && tokenLink.TokensRequired.Count > 0)
                    {
                        foreach (var requiredToken in tokenLink.TokensRequired)
                        {
                            sagaTrigger.Add(new XElement(ns + "RequiresQuestTokenRef", requiredToken));
                        }

                        // Mark saga as locked initially
                        saga.SetAttributeValue("Locked", "true");
                    }

                    // 3. GivesQuestTokenRef (token awarded when completing this location)
                    if (tokenLink != null)
                    {
                        sagaTrigger.Add(new XElement(ns + "GivesQuestTokenRef", tokenLink.TokenAwarded));
                    }

                    saga.Add(sagaTrigger);

                    // Add AIMetadata
                    if (context.Narrative.LocationMetadata.TryGetValue(refName, out var metadata))
                    {
                        var aiMetadata = new XElement(ns + "AIMetadata",
                            new XAttribute("NarrativeRole", metadata.NarrativeRole.ToString()),
                            new XAttribute("NarrativeSequence", metadata.NarrativeSequence),
                            new XAttribute("Tone", metadata.Tone)
                        );

                        // Add elements in XSD order: StoryThreadRef, ThematicTags, NarrativeConnection

                        // 1. StoryThreadRef
                        foreach (var threadRef in metadata.StoryThreadRefs)
                        {
                            aiMetadata.Add(new XElement(ns + "StoryThreadRef", threadRef));
                        }

                        // 2. ThematicTags
                        aiMetadata.Add(new XElement(ns + "ThematicTags", metadata.ThematicTags));

                        // 3. NarrativeConnection
                        foreach (var connection in metadata.NarrativeConnections)
                        {
                            aiMetadata.Add(new XElement(ns + "NarrativeConnection",
                                new XAttribute("TargetRef", connection.TargetRef),
                                new XAttribute("Relationship", connection.Relationship),
                                new XAttribute("Description", connection.Description)
                            ));
                        }

                        saga.Add(aiMetadata);
                    }

                    root.Add(saga);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
