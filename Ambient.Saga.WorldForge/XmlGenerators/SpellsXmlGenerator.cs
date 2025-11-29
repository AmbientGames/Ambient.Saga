using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates Spells XML content by copying from theme.
/// </summary>
public class SpellsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Spells";

    public SpellsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
        XNamespace ns = "Ambient.Domain";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        var root = new XElement(ns + "Spells",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
        );

        // Copy spells from theme
        if (context.Theme?.Spells != null)
        {
            foreach (var spell in context.Theme.Spells)
            {
                var element = new XElement(ns + "Spell",
                    new XAttribute("RefName", spell.RefName ?? ""),
                    new XAttribute("DisplayName", spell.DisplayName ?? ""),
                    new XAttribute("Description", spell.Description ?? ""),
                    new XAttribute("WholesalePrice", spell.WholesalePrice)
                );

                // Optional attributes
                if (!string.IsNullOrEmpty(spell.AffinityRef))
                    element.Add(new XAttribute("AffinityRef", spell.AffinityRef));
                if (!string.IsNullOrEmpty(spell.ExtensionData))
                    element.Add(new XAttribute("ExtensionData", spell.ExtensionData));
                if (spell.DurabilityLoss != 0.01f)
                    element.Add(new XAttribute("DurabilityLoss", spell.DurabilityLoss));
                if (spell.UseType != ItemUseType.Defensive)
                    element.Add(new XAttribute("UseType", spell.UseType.ToString()));
                if (!string.IsNullOrEmpty(spell.StatusEffectRef))
                    element.Add(new XAttribute("StatusEffectRef", spell.StatusEffectRef));
                if (spell.StatusEffectChance != 1.0f)
                    element.Add(new XAttribute("StatusEffectChance", spell.StatusEffectChance));
                if (spell.CleansesStatusEffects)
                    element.Add(new XAttribute("CleansesStatusEffects", spell.CleansesStatusEffects));
                if (!spell.CleanseTargetSelf)
                    element.Add(new XAttribute("CleanseTargetSelf", spell.CleanseTargetSelf));

                // Effects element
                if (spell.Effects != null)
                {
                    var effects = new XElement(ns + "Effects");
                    effects.Add(new XAttribute("Health", spell.Effects.Health));
                    effects.Add(new XAttribute("Stamina", spell.Effects.Stamina));
                    effects.Add(new XAttribute("Mana", spell.Effects.Mana));
                    effects.Add(new XAttribute("Strength", spell.Effects.Strength));
                    effects.Add(new XAttribute("Defense", spell.Effects.Defense));
                    effects.Add(new XAttribute("Speed", spell.Effects.Speed));
                    effects.Add(new XAttribute("Magic", spell.Effects.Magic));
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
