using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates AvatarArchetypes XML content by copying from theme.
/// </summary>
public class AvatarArchetypesXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "AvatarArchetypes";

    public AvatarArchetypesXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
        XNamespace ns = "Ambient.Domain";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        var root = new XElement(ns + "AvatarArchetypes",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
        );

        // Copy archetypes from theme
        if (context.Theme?.CharacterArchetypes != null)
        {
            foreach (var archetype in context.Theme.CharacterArchetypes)
            {
                var element = new XElement(ns + "AvatarArchetype",
                    new XAttribute("RefName", archetype.RefName ?? ""),
                    new XAttribute("DisplayName", archetype.DisplayName ?? ""),
                    new XAttribute("Description", archetype.Description ?? "")
                );

                // Optional attributes
                if (!string.IsNullOrEmpty(archetype.AffinityRef))
                    element.Add(new XAttribute("AffinityRef", archetype.AffinityRef));
                if (!string.IsNullOrEmpty(archetype.ExtensionData))
                    element.Add(new XAttribute("ExtensionData", archetype.ExtensionData));

                // SpawnStats
                if (archetype.SpawnStats != null)
                {
                    element.Add(CreateStatsElement(ns, "SpawnStats", archetype.SpawnStats));
                }

                // SpawnCapabilities
                if (archetype.SpawnCapabilities != null)
                {
                    element.Add(CreateCapabilitiesElement(ns, "SpawnCapabilities", archetype.SpawnCapabilities));
                }

                // RespawnStats
                if (archetype.RespawnStats != null)
                {
                    element.Add(CreateStatsElement(ns, "RespawnStats", archetype.RespawnStats));
                }

                // RespawnCapabilities
                if (archetype.RespawnCapabilities != null)
                {
                    element.Add(CreateCapabilitiesElement(ns, "RespawnCapabilities", archetype.RespawnCapabilities));
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

    private static XElement CreateStatsElement(XNamespace ns, string elementName, CharacterStats stats)
    {
        return new XElement(ns + elementName,
            new XAttribute("Health", stats.Health),
            new XAttribute("Stamina", stats.Stamina),
            new XAttribute("Mana", stats.Mana),
            new XAttribute("Hunger", stats.Hunger),
            new XAttribute("Thirst", stats.Thirst),
            new XAttribute("Temperature", stats.Temperature),
            new XAttribute("Insulation", stats.Insulation),
            new XAttribute("Credits", stats.Credits),
            new XAttribute("Experience", stats.Experience),
            new XAttribute("Strength", stats.Strength),
            new XAttribute("Defense", stats.Defense),
            new XAttribute("Speed", stats.Speed),
            new XAttribute("Magic", stats.Magic)
        );
    }

    private static XElement CreateCapabilitiesElement(XNamespace ns, string elementName, ItemCollection capabilities)
    {
        var element = new XElement(ns + elementName);

        // Order must match XSD: Equipment, Consumables, Spells, Blocks, Tools, BuildingMaterials, QuestTokens

        // Equipment
        if (capabilities.Equipment != null && capabilities.Equipment.Length > 0)
        {
            var equipmentElement = new XElement(ns + "Equipment");
            foreach (var entry in capabilities.Equipment)
            {
                equipmentElement.Add(new XElement(ns + "Entry",
                    new XAttribute("EquipmentRef", entry.EquipmentRef ?? ""),
                    new XAttribute("Condition", entry.Condition)
                ));
            }
            element.Add(equipmentElement);
        }

        // Consumables
        if (capabilities.Consumables != null && capabilities.Consumables.Length > 0)
        {
            var consumablesElement = new XElement(ns + "Consumables");
            foreach (var entry in capabilities.Consumables)
            {
                consumablesElement.Add(new XElement(ns + "Entry",
                    new XAttribute("ConsumableRef", entry.ConsumableRef ?? ""),
                    new XAttribute("Quantity", entry.Quantity)
                ));
            }
            element.Add(consumablesElement);
        }

        // Spells
        if (capabilities.Spells != null && capabilities.Spells.Length > 0)
        {
            var spellsElement = new XElement(ns + "Spells");
            foreach (var entry in capabilities.Spells)
            {
                spellsElement.Add(new XElement(ns + "Entry",
                    new XAttribute("SpellRef", entry.SpellRef ?? ""),
                    new XAttribute("Condition", entry.Condition)
                ));
            }
            element.Add(spellsElement);
        }

        // Blocks
        if (capabilities.Blocks != null && capabilities.Blocks.Length > 0)
        {
            var blocksElement = new XElement(ns + "Blocks");
            foreach (var entry in capabilities.Blocks)
            {
                blocksElement.Add(new XElement(ns + "Entry",
                    new XAttribute("BlockRef", entry.BlockRef ?? ""),
                    new XAttribute("Quantity", entry.Quantity)
                ));
            }
            element.Add(blocksElement);
        }

        // Tools
        if (capabilities.Tools != null && capabilities.Tools.Length > 0)
        {
            var toolsElement = new XElement(ns + "Tools");
            foreach (var entry in capabilities.Tools)
            {
                toolsElement.Add(new XElement(ns + "Entry",
                    new XAttribute("ToolRef", entry.ToolRef ?? ""),
                    new XAttribute("Condition", entry.Condition)
                ));
            }
            element.Add(toolsElement);
        }

        // BuildingMaterials
        if (capabilities.BuildingMaterials != null && capabilities.BuildingMaterials.Length > 0)
        {
            var materialsElement = new XElement(ns + "BuildingMaterials");
            foreach (var entry in capabilities.BuildingMaterials)
            {
                materialsElement.Add(new XElement(ns + "Entry",
                    new XAttribute("BuildingMaterialRef", entry.BuildingMaterialRef ?? ""),
                    new XAttribute("Quantity", entry.Quantity)
                ));
            }
            element.Add(materialsElement);
        }

        // QuestTokens
        if (capabilities.QuestTokens != null && capabilities.QuestTokens.Length > 0)
        {
            var tokensElement = new XElement(ns + "QuestTokens");
            foreach (var entry in capabilities.QuestTokens)
            {
                tokensElement.Add(new XElement(ns + "Entry",
                    new XAttribute("QuestTokenRef", entry.QuestTokenRef ?? "")
                ));
            }
            element.Add(tokensElement);
        }

        return element;
    }
}
