using Ambient.Domain;
using Ambient.Saga.WorldForge;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates Factions XML content.
/// Extracted from StoryGenerator.GenerateFactionsXml()
/// </summary>
public class FactionsXmlGenerator : IXmlContentGenerator
{
    private readonly FactionGenerator _factionGenerator;

    public string GeneratorName => "Factions";

    public FactionsXmlGenerator(FactionGenerator factionGenerator)
    {
        _factionGenerator = factionGenerator;
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "Factions",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                var _factionGenerator = new FactionGenerator(context.RefNameGenerator);
                var factions = _factionGenerator.GenerateFactions(context.Narrative, context.WorldConfig.RefName, context.Theme);

                foreach (var faction in factions)
                {
                    var factionElement = new XElement(ns + "Faction",
                        new XAttribute("RefName", faction.RefName),
                        new XAttribute("DisplayName", faction.DisplayName),
                        new XAttribute("Description", faction.Description),
                        new XAttribute("StartingReputation", faction.StartingReputation),
                        new XAttribute("Category", faction.Category)
                    );

                    // Add relationships
                    if (faction.Relationships.Count > 0)
                    {
                        var relationshipsElement = new XElement(ns + "Relationships");
                        foreach (var rel in faction.Relationships)
                        {
                            relationshipsElement.Add(new XElement(ns + "Relationship",
                                new XAttribute("FactionRef", rel.FactionRef),
                                new XAttribute("RelationshipType", rel.RelationshipType),
                                new XAttribute("SpilloverPercent", rel.SpilloverPercent)
                            ));
                        }
                        factionElement.Add(relationshipsElement);
                    }

                    // Add reputation rewards
                    if (faction.ReputationRewards.Count > 0)
                    {
                        var rewardsElement = new XElement(ns + "ReputationRewards");
                        foreach (var reward in faction.ReputationRewards)
                        {
                            var rewardElement = new XElement(ns + "Reward",
                                new XAttribute("RequiredLevel", reward.RequiredLevel)
                            );

                            foreach (var item in reward.Items)
                            {
                                var itemElement = new XElement(ns + item.Type,
                                    new XAttribute($"{item.Type}Ref", item.RefName),
                                    new XAttribute("Quantity", item.Quantity)
                                );

                                if (item.DiscountPercent > 0)
                                {
                                    itemElement.Add(new XAttribute("DiscountPercent", item.DiscountPercent));
                                }

                                rewardElement.Add(itemElement);
                            }

                            rewardsElement.Add(rewardElement);
                        }
                        factionElement.Add(rewardsElement);
                    }

                    root.Add(factionElement);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
