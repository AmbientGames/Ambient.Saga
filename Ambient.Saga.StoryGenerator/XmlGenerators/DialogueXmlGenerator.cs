using Ambient.Saga.StoryGenerator.Models;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator.XmlGenerators;

/// <summary>
/// Generates Dialogue XML content.
/// Extracted from StoryGenerator.GenerateDialogueXml()
/// </summary>
public class DialogueXmlGenerator : IXmlContentGenerator
{

    public string GeneratorName => "Dialogue";

    public DialogueXmlGenerator()
    {
    }

    public void GenerateXml(GenerationContext context, string outputPath)
    {
                XNamespace ns = "Ambient.Domain";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                var root = new XElement(ns + "DialogueTrees",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", "Ambient.Domain ..\\..\\..\\..\\DefinitionXsd\\Gameplay\\Gameplay.xsd")
                );

                // Create context.Theme-aware content generator
                var contentGenerator = new ThemeAwareContentGenerator(context.Theme);

                // Generate dialogue trees for each character
                foreach (var placement in context.Narrative.CharacterPlacements)
                {
                    var dialogueTree = new XElement(ns + "DialogueTree",
                        new XAttribute("RefName", $"DIALOGUE_{placement.CharacterRefName}"),
                        new XAttribute("DisplayName", $"{placement.DisplayName} Dialogue"),
                        new XAttribute("Description", $"Dialogue tree for {placement.DisplayName}"),
                        new XAttribute("StartNodeId", "greeting")
                    );

                    // Greeting node
                    var greetingNode = new XElement(ns + "Node",
                        new XAttribute("NodeId", "greeting"),
                        new XElement(ns + "Text", placement.InitialGreeting),
                        new XElement(ns + "Choice",
                            new XAttribute("Text", "[AI: Generate friendly response]"),
                            new XAttribute("NextNodeId", "friendly")
                        ),
                        new XElement(ns + "Choice",
                            new XAttribute("Text", "[AI: Ask for information]"),
                            new XAttribute("NextNodeId", "info")
                        )
                    );

                    // Friendly path
                    var friendlyNode = new XElement(ns + "Node",
                        new XAttribute("NodeId", "friendly"),
                        new XElement(ns + "Text", $"[AI: {placement.DisplayName} responds warmly, mentions nearby locations: {string.Join(", ", placement.MentionsSagaArcRefs.Take(2))}]"),
                        new XElement(ns + "Choice",
                            new XAttribute("Text", "[AI: Continue conversation]"),
                            new XAttribute("NextNodeId", "end")
                        )
                    );

                    // Info path
                    var infoNode = new XElement(ns + "Node",
                        new XAttribute("NodeId", "info"),
                        new XElement(ns + "Text", $"[AI: {placement.DisplayName} provides lore about {placement.Location.DisplayName}]"),
                        new XElement(ns + "Choice",
                            new XAttribute("Text", "[AI: Thank them]"),
                            new XAttribute("NextNodeId", "end")
                        )
                    );

                    // End node
                    var endNode = new XElement(ns + "Node",
                        new XAttribute("NodeId", "end"),
                        new XElement(ns + "Text", "[AI: Farewell message]")
                    );

                    // Add type-specific actions
                    if (placement.CharacterType == "Merchant")
                    {
                        greetingNode.Add(new XElement(ns + "Choice",
                            new XAttribute("Text", "I'd like to trade"),
                            new XAttribute("NextNodeId", "trade_greeting")
                        ));

                        // Trade greeting - offer bargaining option
                        var tradeGreetingNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "trade_greeting"),
                            new XElement(ns + "Text", "[AI: Merchant welcomes customer and mentions their wares]"),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Let me see your wares (open trade)"),
                                new XAttribute("NextNodeId", "open_trade")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Can we negotiate on price?"),
                                new XAttribute("NextNodeId", "attempt_bargain")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "I'll come back later"),
                                new XAttribute("NextNodeId", "end")
                            )
                        );
                        dialogueTree.Add(tradeGreetingNode);

                        // Attempt bargain
                        var attemptBargainNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "attempt_bargain"),
                            new XElement(ns + "Text", "[AI: Merchant considers the request based on WillingToBargain trait]"),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Ask for a big discount (requires BargainSkill 30+)"),
                                new XAttribute("NextNodeId", "bargain_hard_check")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Ask for a modest discount (requires BargainSkill 15+)"),
                                new XAttribute("NextNodeId", "bargain_medium_check")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Just open the trade window"),
                                new XAttribute("NextNodeId", "open_trade")
                            )
                        );
                        dialogueTree.Add(attemptBargainNode);

                        // Hard bargain - success
                        var bargainHardSuccessNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "bargain_hard_check"),
                            new XElement(ns + "Condition",
                                new XAttribute("Type", "TraitComparison"),
                                new XAttribute("Trait", "BargainSkill"),
                                new XAttribute("Operator", "GreaterThanOrEqual"),
                                new XAttribute("Value", "30")
                            ),
                            new XElement(ns + "Text", "[AI: Merchant impressed by negotiation skill, agrees to 30% discount]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "NegotiatedDiscount"),
                                new XAttribute("TraitValue", "30")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "BargainSkill"),
                                new XAttribute("TraitValue", "35")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "OpenMerchantTrade"),
                                new XAttribute("CharacterRef", placement.CharacterRefName)
                            )
                        );
                        dialogueTree.Add(bargainHardSuccessNode);

                        // Hard bargain - failure
                        var bargainHardFailNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "bargain_hard_fail"),
                            new XElement(ns + "Text", "[AI: Merchant insulted by lowball offer, becomes unfriendly]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "SetCharacterState"),
                                new XAttribute("RefName", "Unfriendly")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "I apologize, can we try again?"),
                                new XAttribute("NextNodeId", "attempt_bargain")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Fine, I'm leaving"),
                                new XAttribute("NextNodeId", "end")
                            )
                        );
                        dialogueTree.Add(bargainHardFailNode);

                        // Medium bargain - success
                        var bargainMediumSuccessNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "bargain_medium_check"),
                            new XElement(ns + "Condition",
                                new XAttribute("Type", "TraitComparison"),
                                new XAttribute("Trait", "BargainSkill"),
                                new XAttribute("Operator", "GreaterThanOrEqual"),
                                new XAttribute("Value", "15")
                            ),
                            new XElement(ns + "Text", "[AI: Merchant agrees to reasonable 15% discount]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "NegotiatedDiscount"),
                                new XAttribute("TraitValue", "15")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "BargainSkill"),
                                new XAttribute("TraitValue", "20")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "OpenMerchantTrade"),
                                new XAttribute("CharacterRef", placement.CharacterRefName)
                            )
                        );
                        dialogueTree.Add(bargainMediumSuccessNode);

                        // Medium bargain - failure (small courtesy discount)
                        var bargainMediumFailNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "bargain_medium_fail"),
                            new XElement(ns + "Text", "[AI: Merchant can only offer small 5% courtesy discount]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "NegotiatedDiscount"),
                                new XAttribute("TraitValue", "5")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "OpenMerchantTrade"),
                                new XAttribute("CharacterRef", placement.CharacterRefName)
                            )
                        );
                        dialogueTree.Add(bargainMediumFailNode);

                        // Open trade (no discount)
                        var openTradeNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "open_trade"),
                            new XElement(ns + "Text", "[AI: Merchant shows their wares]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "OpenMerchantTrade"),
                                new XAttribute("CharacterRef", placement.CharacterRefName)
                            )
                        );
                        dialogueTree.Add(openTradeNode);
                    }
                    else if (placement.CharacterType == "Boss")
                    {
                        greetingNode.Add(new XElement(ns + "Choice",
                            new XAttribute("Text", "I challenge you!"),
                            new XAttribute("NextNodeId", "battle")
                        ));

                        var battleNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle"),
                            new XElement(ns + "Text", "[AI: Boss accepts challenge with intimidating response]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "StartBossBattle"),
                                new XAttribute("CharacterRef", placement.CharacterRefName)
                            )
                        );
                        dialogueTree.Add(battleNode);

                        // Mid-battle dialogue nodes (triggered by BattleDialogue system) - context.Theme-aware content

                        // Opening taunt (Turn 1)
                        var battleOpeningNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_opening"),
                            new XElement(ns + "Text", contentGenerator.GenerateBattleDialogue(
                                "battle_opening",
                                placement.DisplayName,
                                placement.Location.DisplayName)),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "ChangeStance"),
                                new XAttribute("RefName", "Offensive")
                            )
                        );
                        dialogueTree.Add(battleOpeningNode);

                        // First blood (75% HP)
                        var battleFirstBloodNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_first_blood"),
                            new XElement(ns + "Text", contentGenerator.GenerateBattleDialogue(
                                "battle_first_blood",
                                placement.DisplayName,
                                placement.Location.DisplayName))
                        );
                        dialogueTree.Add(battleFirstBloodNode);

                        // Berserk mode (50% HP)
                        var battleBerserkNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_berserk"),
                            new XElement(ns + "Text", contentGenerator.GenerateBattleDialogue(
                                "battle_berserk",
                                placement.DisplayName,
                                placement.Location.DisplayName)),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "Aggression"),
                                new XAttribute("TraitValue", "100")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "ChangeStance"),
                                new XAttribute("RefName", "Offensive")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "ChangeAffinity"),
                                new XAttribute("RefName", "Fire")
                            )
                        );
                        dialogueTree.Add(battleBerserkNode);

                        // Tactical retreat (30% HP)
                        var battleRetreatNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_retreat"),
                            new XElement(ns + "Text", contentGenerator.GenerateBattleDialogue(
                                "battle_retreat",
                                placement.DisplayName,
                                placement.Location.DisplayName)),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "ChangeStance"),
                                new XAttribute("RefName", "Defensive")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "HealSelf"),
                                new XAttribute("Amount", "15")
                            )
                        );
                        dialogueTree.Add(battleRetreatNode);

                        // Last stand (15% HP) - player choice!
                        var battleLastStandNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_last_stand"),
                            new XElement(ns + "Text", contentGenerator.GenerateBattleDialogue(
                                "battle_last_stand",
                                placement.DisplayName,
                                placement.Location.DisplayName)),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Let them surrender (gain Merciful trait)"),
                                new XAttribute("NextNodeId", "battle_accept_surrender")
                            ),
                            new XElement(ns + "Choice",
                                new XAttribute("Text", "Finish them! (gain Ruthless trait)"),
                                new XAttribute("NextNodeId", "battle_no_mercy")
                            )
                        );
                        dialogueTree.Add(battleLastStandNode);

                        // Accept surrender
                        var battleAcceptSurrenderNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_accept_surrender"),
                            new XElement(ns + "Text", $"[AI: {placement.DisplayName} expresses gratitude/confusion at being spared, yields and offers weapon/gold]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "EndBattle"),
                                new XAttribute("RefName", "Victory")
                            ),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "Merciful"),
                                new XAttribute("TraitValue", "1")
                            )
                        );
                        dialogueTree.Add(battleAcceptSurrenderNode);

                        // No mercy
                        var battleNoMercyNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_no_mercy"),
                            new XElement(ns + "Text", $"[AI: {placement.DisplayName} accepts fate with honor, makes desperate final attack]"),
                            new XElement(ns + "Action",
                                new XAttribute("Type", "AssignTrait"),
                                new XAttribute("Trait", "Ruthless"),
                                new XAttribute("TraitValue", "1")
                            )
                        );
                        dialogueTree.Add(battleNoMercyNode);

                        // On Defeat
                        var battleDefeatedNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "battle_defeated"),
                            new XElement(ns + "Text", contentGenerator.GenerateBattleDialogue(
                                "battle_defeated",
                                placement.DisplayName,
                                placement.Location.DisplayName))
                        );
                        dialogueTree.Add(battleDefeatedNode);
                    }
                    else if (placement.CharacterType == "QuestGiver")
                    {
                        greetingNode.Add(new XElement(ns + "Choice",
                            new XAttribute("Text", "Do you need help?"),
                            new XAttribute("NextNodeId", "quest")
                        ));

                        // Find corresponding token from this location
                        var tokenLink = context.Narrative.TokenChains.FirstOrDefault(t => t.Location == placement.Location);
                        var questNode = new XElement(ns + "Node",
                            new XAttribute("NodeId", "quest"),
                            new XElement(ns + "Text", $"[AI: Quest giver explains problem at {placement.Location.DisplayName}]")
                        );

                        if (tokenLink != null)
                        {
                            questNode.Add(new XElement(ns + "Action",
                                new XAttribute("Type", "GiveQuestToken"),
                                new XAttribute("RefName", tokenLink.TokenAwarded),
                                new XAttribute("Amount", "1")
                            ));
                        }

                        dialogueTree.Add(questNode);
                    }

                    dialogueTree.Add(greetingNode);
                    dialogueTree.Add(friendlyNode);
                    dialogueTree.Add(infoNode);
                    dialogueTree.Add(endNode);

                    root.Add(dialogueTree);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root
                );
                doc.Save(outputPath);
    }
}
