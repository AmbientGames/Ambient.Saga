using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates Equipment XML content by copying from theme.
/// </summary>
public class EquipmentXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Equipment";

    public EquipmentXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
        XNamespace ns = "Ambient.Domain";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        var root = new XElement(ns + "Equipment",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
        );

        // Copy equipment from theme
        if (context.Theme?.Equipment != null)
        {
            foreach (var equipment in context.Theme.Equipment)
            {
                var element = new XElement(ns + "Equipment",
                    new XAttribute("RefName", equipment.RefName ?? ""),
                    new XAttribute("DisplayName", equipment.DisplayName ?? ""),
                    new XAttribute("Description", equipment.Description ?? ""),
                    new XAttribute("SlotRef", equipment.SlotRef ?? "RightHand"),
                    new XAttribute("WholesalePrice", equipment.WholesalePrice)
                );

                // Optional attributes
                if (!string.IsNullOrEmpty(equipment.ModelRef))
                    element.Add(new XAttribute("ModelRef", equipment.ModelRef));
                if (!string.IsNullOrEmpty(equipment.AffinityRef))
                    element.Add(new XAttribute("AffinityRef", equipment.AffinityRef));
                if (!string.IsNullOrEmpty(equipment.ExtensionData))
                    element.Add(new XAttribute("ExtensionData", equipment.ExtensionData));
                if (equipment.DurabilityLoss != 0.01f)
                    element.Add(new XAttribute("DurabilityLoss", equipment.DurabilityLoss));
                if (equipment.UseType != ItemUseType.Defensive)
                    element.Add(new XAttribute("UseType", equipment.UseType.ToString()));

                // Effects element
                if (equipment.Effects != null)
                {
                    var effects = new XElement(ns + "Effects");
                    effects.Add(new XAttribute("Health", equipment.Effects.Health));
                    effects.Add(new XAttribute("Defense", equipment.Effects.Defense));
                    effects.Add(new XAttribute("Strength", equipment.Effects.Strength));
                    effects.Add(new XAttribute("Speed", equipment.Effects.Speed));
                    effects.Add(new XAttribute("Magic", equipment.Effects.Magic));
                    if (equipment.Effects.Mana != 0)
                        effects.Add(new XAttribute("Mana", equipment.Effects.Mana));
                    if (equipment.Effects.Stamina != 0)
                        effects.Add(new XAttribute("Stamina", equipment.Effects.Stamina));
                    element.Add(effects);
                }

                root.Add(element);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            root
        );
        doc.Save(outputPath);
    }
}
