using Ambient.Domain;
using Ambient.Saga.WorldForge.Models;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Generates StatusEffects XML content.
/// Extracted from StoryGenerator.GenerateStatusEffectsXml()
/// </summary>
public class StatusEffectsXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "StatusEffects";

    public StatusEffectsXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "StatusEffects",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Damage over time effects
                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "POISON"),
                    new XAttribute("DisplayName", "Poison"),
                    new XAttribute("Description", "Deals damage over time"),
                    new XAttribute("Type", "DamageOverTime"),
                    new XAttribute("DamagePerTurn", "5"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.3")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "BLEED"),
                    new XAttribute("DisplayName", "Bleeding"),
                    new XAttribute("Description", "Causes bleeding damage each turn"),
                    new XAttribute("Type", "DamageOverTime"),
                    new XAttribute("DamagePerTurn", "8"),
                    new XAttribute("DurationTurns", "2"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("MaxStacks", "3"),
                    new XAttribute("ApplicationChance", "0.25")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "BURN"),
                    new XAttribute("DisplayName", "Burning"),
                    new XAttribute("Description", "Fire damage over time, decreases defense"),
                    new XAttribute("Type", "DamageOverTime"),
                    new XAttribute("DamagePerTurn", "10"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("DefenseModifier", "-0.1"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.2")
                ));

                // Control effects
                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "STUN"),
                    new XAttribute("DisplayName", "Stunned"),
                    new XAttribute("Description", "Cannot act for 1 turn"),
                    new XAttribute("Type", "Stun"),
                    new XAttribute("DurationTurns", "1"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.15"),
                    new XAttribute("MaxStacks", "0")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "SLOW"),
                    new XAttribute("DisplayName", "Slowed"),
                    new XAttribute("Description", "Reduced movement and attack speed"),
                    new XAttribute("Type", "Slow"),
                    new XAttribute("DurationTurns", "2"),
                    new XAttribute("SpeedModifier", "-0.3"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.4")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "ROOT"),
                    new XAttribute("DisplayName", "Rooted"),
                    new XAttribute("Description", "Cannot flee from battle"),
                    new XAttribute("Type", "Root"),
                    new XAttribute("DurationTurns", "2"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.3")
                ));

                // Debuffs
                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "WEAKEN"),
                    new XAttribute("DisplayName", "Weakened"),
                    new XAttribute("Description", "Reduced damage output"),
                    new XAttribute("Type", "Weaken"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("StrengthModifier", "-0.25"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.35")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "VULNERABLE"),
                    new XAttribute("DisplayName", "Vulnerable"),
                    new XAttribute("Description", "Takes increased damage"),
                    new XAttribute("Type", "Vulnerable"),
                    new XAttribute("DurationTurns", "2"),
                    new XAttribute("DefenseModifier", "-0.3"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.25")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "BLIND"),
                    new XAttribute("DisplayName", "Blinded"),
                    new XAttribute("Description", "Reduced accuracy"),
                    new XAttribute("Type", "Blind"),
                    new XAttribute("DurationTurns", "2"),
                    new XAttribute("AccuracyModifier", "-0.4"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.3")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "SILENCE"),
                    new XAttribute("DisplayName", "Silenced"),
                    new XAttribute("Description", "Cannot cast spells"),
                    new XAttribute("Type", "Silence"),
                    new XAttribute("DurationTurns", "2"),
                    new XAttribute("Category", "Debuff"),
                    new XAttribute("ApplicationChance", "0.2")
                ));

                // Buffs
                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "STRENGTH_BOOST"),
                    new XAttribute("DisplayName", "Strength"),
                    new XAttribute("Description", "Increased physical damage"),
                    new XAttribute("Type", "StatBoost"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("StrengthModifier", "0.3"),
                    new XAttribute("Category", "Buff"),
                    new XAttribute("ApplicationChance", "1.0")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "DEFENSE_BOOST"),
                    new XAttribute("DisplayName", "Fortified"),
                    new XAttribute("Description", "Increased defense"),
                    new XAttribute("Type", "StatBoost"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("DefenseModifier", "0.4"),
                    new XAttribute("Category", "Buff"),
                    new XAttribute("ApplicationChance", "1.0")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "SPEED_BOOST"),
                    new XAttribute("DisplayName", "Haste"),
                    new XAttribute("Description", "Increased speed and agility"),
                    new XAttribute("Type", "StatBoost"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("SpeedModifier", "0.3"),
                    new XAttribute("Category", "Buff"),
                    new XAttribute("ApplicationChance", "1.0")
                ));

                root.Add(new XElement(ns + "StatusEffect",
                    new XAttribute("RefName", "MAGIC_BOOST"),
                    new XAttribute("DisplayName", "Empowered"),
                    new XAttribute("Description", "Increased magic power"),
                    new XAttribute("Type", "StatBoost"),
                    new XAttribute("DurationTurns", "3"),
                    new XAttribute("MagicModifier", "0.35"),
                    new XAttribute("Category", "Buff"),
                    new XAttribute("ApplicationChance", "1.0")
                ));

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
