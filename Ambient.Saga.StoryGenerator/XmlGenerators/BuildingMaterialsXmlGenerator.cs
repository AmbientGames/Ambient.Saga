using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates BuildingMaterials XML content.
/// Extracted from StoryGenerator.GenerateBuildingMaterialsXml()
/// </summary>
public class BuildingMaterialsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "BuildingMaterials";

    public BuildingMaterialsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "BuildingMaterials",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Wood
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Wood"),
                    new XAttribute("DisplayName", "Wooden Nails"),
                    new XAttribute("Description", "Simple wooden pegs for basic wood construction"),
                    new XAttribute("TextureRef", "Nails"),
                    new XAttribute("WholesalePrice", "2"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Plant")
                        )
                    )
                ));

                // Mortar
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Mortar"),
                    new XAttribute("DisplayName", "Mortar"),
                    new XAttribute("Description", "Stone mortar for construction"),
                    new XAttribute("TextureRef", "Mortar"),
                    new XAttribute("WholesalePrice", "3"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate")
                        )
                    )
                ));

                // Stone
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Stone"),
                    new XAttribute("DisplayName", "Stone Mortar"),
                    new XAttribute("Description", "Basic binding paste for masonry"),
                    new XAttribute("TextureRef", "Mortar"),
                    new XAttribute("WholesalePrice", "3"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate")
                        )
                    )
                ));

                // Clay
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Clay"),
                    new XAttribute("DisplayName", "Clay Adhesive"),
                    new XAttribute("Description", "Natural clay binding material"),
                    new XAttribute("TextureRef", "Mortar"),
                    new XAttribute("WholesalePrice", "2"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Carbon")
                        )
                    )
                ));

                // WoodGlue
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "WoodGlue"),
                    new XAttribute("DisplayName", "Wood Glue"),
                    new XAttribute("Description", "Strong adhesive for joining wooden components"),
                    new XAttribute("TextureRef", "WoodGlue"),
                    new XAttribute("WholesalePrice", "4"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Plant")
                        )
                    )
                ));

                // Cement
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Cement"),
                    new XAttribute("DisplayName", "Cement"),
                    new XAttribute("Description", "Hydraulic cement for concrete construction"),
                    new XAttribute("TextureRef", "Cement"),
                    new XAttribute("WholesalePrice", "5"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "SteelReinforcedConcrete")
                        )
                    )
                ));

                // Nails
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Nails"),
                    new XAttribute("DisplayName", "Metal Nails"),
                    new XAttribute("Description", "Steel fasteners for wood construction"),
                    new XAttribute("TextureRef", "Nails"),
                    new XAttribute("WholesalePrice", "3"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood")
                        )
                    )
                ));

                // Bolts
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Bolts"),
                    new XAttribute("DisplayName", "Bolts"),
                    new XAttribute("Description", "Threaded fasteners for metal and heavy construction"),
                    new XAttribute("TextureRef", "Bolts"),
                    new XAttribute("WholesalePrice", "4"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Metal")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Steel")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood")
                        )
                    )
                ));

                // Epoxy
                root.Add(new XElement(ns + "BuildingMaterial",
                    new XAttribute("RefName", "Epoxy"),
                    new XAttribute("DisplayName", "Epoxy Resin"),
                    new XAttribute("Description", "Strong adhesive for multiple materials"),
                    new XAttribute("TextureRef", "Epoxy"),
                    new XAttribute("WholesalePrice", "12"),
                    new XElement(ns + "CompatibleSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Metal")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Plant")
                        )
                    )
                ));

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
