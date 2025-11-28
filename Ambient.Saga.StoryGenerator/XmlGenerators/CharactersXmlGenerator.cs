using Ambient.Saga.StoryGenerator.Models;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates Characters XML content.
/// Extracted from StoryGenerator.GenerateCharactersXml()
/// </summary>
public class CharactersXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Characters";

    public CharactersXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "Characters",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Create context.Theme-aware content generator for equipment, spells, and dialogue
                var contentGenerator = new ThemeAwareContentGenerator(context.Theme);

                // Generate characters from placements
                foreach (var placement in context.Narrative.CharacterPlacements)
                {
                    var character = new XElement(ns + "Character",
                        new XAttribute("RefName", placement.CharacterRefName),
                        new XAttribute("DisplayName", placement.DisplayName),
                        new XAttribute("Description", $"Character at {placement.Location.DisplayName}")
                    );

                    // Add elements in XSD order (from CharacterBase): Stats, Capabilities, then Character-specific elements

                    // 1. Stats (REQUIRED by CharacterBase)
                    var stats = new XElement(ns + "Stats");
                    if (placement.CharacterType == "Boss")
                    {
                        stats.Add(new XAttribute("Health", "0.8"));
                        stats.Add(new XAttribute("Stamina", "1.0"));
                        stats.Add(new XAttribute("Mana", "1.0"));
                        stats.Add(new XAttribute("Strength", "0.15"));
                        stats.Add(new XAttribute("Defense", "0.10"));
                        stats.Add(new XAttribute("Speed", "0.05"));
                        stats.Add(new XAttribute("Magic", "0.08"));
                        stats.Add(new XAttribute("Credits", "50"));
                    }
                    else if (placement.CharacterType == "Merchant")
                    {
                        stats.Add(new XAttribute("Health", "0.5"));
                        stats.Add(new XAttribute("Stamina", "1.0"));
                        stats.Add(new XAttribute("Mana", "1.0"));
                        stats.Add(new XAttribute("Strength", "0.05"));
                        stats.Add(new XAttribute("Defense", "0.05"));
                        stats.Add(new XAttribute("Speed", "0.05"));
                        stats.Add(new XAttribute("Magic", "0.05"));
                        stats.Add(new XAttribute("Credits", "1000"));
                    }
                    else // NPC or QuestGiver
                    {
                        stats.Add(new XAttribute("Health", "0.6"));
                        stats.Add(new XAttribute("Stamina", "1.0"));
                        stats.Add(new XAttribute("Mana", "1.0"));
                        stats.Add(new XAttribute("Strength", "0.05"));
                        stats.Add(new XAttribute("Defense", "0.05"));
                        stats.Add(new XAttribute("Speed", "0.05"));
                        stats.Add(new XAttribute("Magic", "0.05"));
                        stats.Add(new XAttribute("Credits", "10"));
                    }
                    character.Add(stats);

                    // 2. Capabilities (REQUIRED by CharacterBase)
                    var capabilities = new XElement(ns + "Capabilities");

                    // Add context.Theme-appropriate equipment and spells
                    if (placement.CharacterType == "Boss" || placement.CharacterType == "Merchant" || placement.CharacterType == "Guard")
                    {
                        // Select context.Theme-appropriate equipment
                        var selectedEquipment = contentGenerator.SelectCharacterEquipment(
                            placement.CharacterType,
                            placement.DifficultyTier ?? "Normal");

                        if (selectedEquipment.Count > 0)
                        {
                            var equipment = new XElement(ns + "Equipment");
                            foreach (var (equipRef, condition) in selectedEquipment)
                            {
                                equipment.Add(new XElement(ns + "Entry",
                                    new XAttribute("EquipmentRef", equipRef),
                                    new XAttribute("Condition", condition.ToString("F1"))
                                ));
                            }
                            capabilities.Add(equipment);
                        }

                        // Select context.Theme-appropriate spells
                        var selectedSpells = contentGenerator.SelectCharacterSpells(
                            placement.CharacterType,
                            placement.DifficultyTier ?? "Normal");

                        if (selectedSpells.Count > 0)
                        {
                            var spells = new XElement(ns + "Spells");
                            foreach (var (spellRef, condition) in selectedSpells)
                            {
                                spells.Add(new XElement(ns + "Entry",
                                    new XAttribute("SpellRef", spellRef),
                                    new XAttribute("Condition", condition.ToString("F1"))
                                ));
                            }
                            capabilities.Add(spells);
                        }
                    }

                    character.Add(capabilities);

                    // Now add Character-specific elements in order: Interactable, Traits, InitiationBehavior, AIMetadata

                    // 3. Interactable (skipped - we don't use it)

                    // 2. Traits (based on character type)
                    if (placement.CharacterType == "Boss")
                    {
                        character.Add(new XElement(ns + "Traits",
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "BossFight"),
                                new XAttribute("Value", "1")
                            ),
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "Hostile"),
                                new XAttribute("Value", "1")
                            ),
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "TacticalRetreat"),
                                new XAttribute("Value", "30")
                            ),
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "LastStand"),
                                new XAttribute("Value", "15")
                            )
                        ));

                        // Add mid-battle dialogue triggers
                        var battleDialogue = new XElement(ns + "BattleDialogue",
                            // Opening taunt (Turn 1)
                            new XElement(ns + "Trigger",
                                new XAttribute("Condition", "TurnNumber"),
                                new XAttribute("Value", "1"),
                                new XAttribute("DialogueTreeRef", $"DIALOGUE_{placement.CharacterRefName}"),
                                new XAttribute("StartNodeId", "battle_opening"),
                                new XAttribute("OnceOnly", "true")
                            ),
                            // First blood (75% HP)
                            new XElement(ns + "Trigger",
                                new XAttribute("Condition", "HealthBelow"),
                                new XAttribute("Value", "0.75"),
                                new XAttribute("DialogueTreeRef", $"DIALOGUE_{placement.CharacterRefName}"),
                                new XAttribute("StartNodeId", "battle_first_blood"),
                                new XAttribute("OnceOnly", "true")
                            ),
                            // Berserk mode (50% HP)
                            new XElement(ns + "Trigger",
                                new XAttribute("Condition", "HealthBelow"),
                                new XAttribute("Value", "0.50"),
                                new XAttribute("DialogueTreeRef", $"DIALOGUE_{placement.CharacterRefName}"),
                                new XAttribute("StartNodeId", "battle_berserk"),
                                new XAttribute("OnceOnly", "true")
                            ),
                            // Tactical retreat (30% HP)
                            new XElement(ns + "Trigger",
                                new XAttribute("Condition", "HealthBelow"),
                                new XAttribute("Value", "0.30"),
                                new XAttribute("DialogueTreeRef", $"DIALOGUE_{placement.CharacterRefName}"),
                                new XAttribute("StartNodeId", "battle_retreat"),
                                new XAttribute("OnceOnly", "true")
                            ),
                            // Last stand (15% HP)
                            new XElement(ns + "Trigger",
                                new XAttribute("Condition", "HealthBelow"),
                                new XAttribute("Value", "0.15"),
                                new XAttribute("DialogueTreeRef", $"DIALOGUE_{placement.CharacterRefName}"),
                                new XAttribute("StartNodeId", "battle_last_stand"),
                                new XAttribute("OnceOnly", "true")
                            ),
                            // On Defeat
                            new XElement(ns + "Trigger",
                                new XAttribute("Condition", "OnDefeat"),
                                new XAttribute("Value", "0"),
                                new XAttribute("DialogueTreeRef", $"DIALOGUE_{placement.CharacterRefName}"),
                                new XAttribute("StartNodeId", "battle_defeated"),
                                new XAttribute("OnceOnly", "true")
                            )
                        );
                        character.Add(battleDialogue);
                    }
                    else if (placement.CharacterType == "Merchant")
                    {
                        // Randomly assign bargaining difficulty (Shrewd: 20, Fair: 50, Generous: 70)
                        var random = new Random();
                        var willingToBargainValues = new[] { 20, 50, 70 };
                        var willingToBargainValue = willingToBargainValues[random.Next(willingToBargainValues.Length)];

                        character.Add(new XElement(ns + "Traits",
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "WillTrade"),
                                new XAttribute("Value", "1")
                            ),
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "Friendly"),
                                new XAttribute("Value", "1")
                            ),
                            new XElement(ns + "Trait",
                                new XAttribute("Name", "WillingToBargain"),
                                new XAttribute("Value", willingToBargainValue.ToString())
                            )
                        ));
                    }

                    // 3. InitiationBehavior with context.Theme-aware greeting
                    var greeting = contentGenerator.GenerateCharacterGreeting(
                        placement.CharacterType,
                        placement.Location.DisplayName,
                        placement.Personality);

                    character.Add(new XElement(ns + "InitiationBehavior",
                        new XAttribute("InitialState", "Neutral"),
                        new XElement(ns + "InitialGreeting", greeting)
                    ));

                    // 4. AIMetadata
                    character.Add(new XElement(ns + "AIMetadata",
                        new XAttribute("Personality", placement.Personality),
                        new XAttribute("Role", placement.NarrativeRole),
                        placement.MentionsSagaArcRefs.Select(sagaRef => new XElement(ns + "MentionsSagaArcRef", sagaRef))
                    ));

                    root.Add(character);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
