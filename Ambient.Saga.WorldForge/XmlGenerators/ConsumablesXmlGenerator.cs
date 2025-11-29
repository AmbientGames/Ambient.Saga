using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates Consumables XML content by copying from theme.
/// </summary>
public class ConsumablesXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Consumables";

    public ConsumablesXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
        XNamespace ns = "Ambient.Domain";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        var root = new XElement(ns + "Consumables",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
        );

        // Copy consumables from theme
        if (context.Theme?.Consumables != null)
        {
            foreach (var consumable in context.Theme.Consumables)
            {
                var element = new XElement(ns + "Consumable",
                    new XAttribute("RefName", consumable.RefName ?? ""),
                    new XAttribute("DisplayName", consumable.DisplayName ?? ""),
                    new XAttribute("Description", consumable.Description ?? ""),
                    new XAttribute("WholesalePrice", consumable.WholesalePrice)
                );

                // Optional attributes
                if (!string.IsNullOrEmpty(consumable.ExtensionData))
                    element.Add(new XAttribute("ExtensionData", consumable.ExtensionData));
                if (consumable.DurabilityLoss != 0.01f)
                    element.Add(new XAttribute("DurabilityLoss", consumable.DurabilityLoss));
                if (consumable.UseType != ItemUseType.Defensive)
                    element.Add(new XAttribute("UseType", consumable.UseType.ToString()));

                // Effects element
                if (consumable.Effects != null)
                {
                    var effects = new XElement(ns + "Effects");
                    effects.Add(new XAttribute("Health", consumable.Effects.Health));
                    effects.Add(new XAttribute("Stamina", consumable.Effects.Stamina));
                    effects.Add(new XAttribute("Mana", consumable.Effects.Mana));
                    effects.Add(new XAttribute("Hunger", consumable.Effects.Hunger));
                    effects.Add(new XAttribute("Thirst", consumable.Effects.Thirst));
                    effects.Add(new XAttribute("Temperature", consumable.Effects.Temperature));
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
