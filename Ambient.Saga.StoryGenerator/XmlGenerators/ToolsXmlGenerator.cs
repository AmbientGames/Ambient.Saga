using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates Tools XML content.
/// Extracted from StoryGenerator.GenerateToolsXml()
/// </summary>
public class ToolsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Tools";

    public ToolsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "Tools",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Multitool
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "Multitool"),
                    new XAttribute("DisplayName", "Multitool"),
                    new XAttribute("TextureRef", "Multitool"),
                    new XAttribute("WholesalePrice", "10"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone"),
                            new XAttribute("EffectivenessMultiplier", ".5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Plant"),
                            new XAttribute("EffectivenessMultiplier", ".5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood"),
                            new XAttribute("EffectivenessMultiplier", ".5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate"),
                            new XAttribute("EffectivenessMultiplier", ".5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Carbon"),
                            new XAttribute("EffectivenessMultiplier", ".5")
                        )
                    )
                ));

                // Pickaxe
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "Pickaxe"),
                    new XAttribute("DisplayName", "Pickaxe"),
                    new XAttribute("TextureRef", "Pickaxe"),
                    new XAttribute("WholesalePrice", "5"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone")
                        )
                    )
                ));

                // Axe
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "Axe"),
                    new XAttribute("DisplayName", "Axe"),
                    new XAttribute("TextureRef", "Axe"),
                    new XAttribute("WholesalePrice", "5"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Plant")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood")
                        )
                    )
                ));

                // Spade
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "Spade"),
                    new XAttribute("DisplayName", "Spade"),
                    new XAttribute("TextureRef", "Spade"),
                    new XAttribute("WholesalePrice", "5"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Carbon")
                        )
                    )
                ));

                // Steel Pickaxe
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "SteelPickaxe"),
                    new XAttribute("DisplayName", "Steel Pickaxe"),
                    new XAttribute("TextureRef", "Pickaxe"),
                    new XAttribute("WholesalePrice", "15"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Stone"),
                            new XAttribute("EffectivenessMultiplier", "3.0")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate"),
                            new XAttribute("EffectivenessMultiplier", "2.5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Metal"),
                            new XAttribute("EffectivenessMultiplier", "1.5")
                        )
                    )
                ));

                // Steel Axe
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "SteelAxe"),
                    new XAttribute("DisplayName", "Steel Axe"),
                    new XAttribute("TextureRef", "Axe"),
                    new XAttribute("WholesalePrice", "15"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Plant"),
                            new XAttribute("EffectivenessMultiplier", "2.5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Wood"),
                            new XAttribute("EffectivenessMultiplier", "3.0")
                        )
                    )
                ));

                // Steel Spade
                root.Add(new XElement(ns + "Tool",
                    new XAttribute("RefName", "SteelSpade"),
                    new XAttribute("DisplayName", "Steel Spade"),
                    new XAttribute("TextureRef", "Spade"),
                    new XAttribute("WholesalePrice", "15"),
                    new XElement(ns + "EffectiveSubstances",
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Aggregate"),
                            new XAttribute("EffectivenessMultiplier", "2.5")
                        ),
                        new XElement(ns + "Substance",
                            new XAttribute("SubstanceRef", "Carbon"),
                            new XAttribute("EffectivenessMultiplier", "3.0")
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
