using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;

namespace Ambient.Saga.Engine.Tests.Rpg.Dialogue;

public class DialogueEngineTests
{
    private readonly MockDialogueStateProvider _state;
    private readonly DialogueEngine _engine;

    public DialogueEngineTests()
    {
        _state = new MockDialogueStateProvider();
        _engine = new DialogueEngine(_state);
    }

    private DialogueTree CreateSimpleTree()
    {
        return new DialogueTree
        {
            RefName = "simple_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Text = new[] { "Hello, traveler!" },
                    Choice = new[]
                    {
                        new DialogueChoice { Text = "Who are you?", NextNodeId = "who" },
                        new DialogueChoice { Text = "Goodbye", NextNodeId = "end" }
                    }
                },
                new DialogueNode
                {
                    NodeId = "who",
                    Text = new[] { "I am a merchant." },
                    NextNodeId = "end"
                },
                new DialogueNode
                {
                    NodeId = "end",
                    Text = new[] { "Farewell!" }
                }
            }
        };
    }

    #region Starting Dialogue

    [Fact]
    public void StartDialogue_NavigatesToStartNode()
    {
        var tree = CreateSimpleTree();

        var node = _engine.StartDialogue(tree);

        Assert.NotNull(node);
        Assert.Equal("start", node.NodeId);
        Assert.Equal(tree, _engine.CurrentTree);
        Assert.Equal(node, _engine.CurrentNode);
    }

    [Fact]
    public void StartDialogue_RecordsVisit()
    {
        var tree = CreateSimpleTree();

        _engine.StartDialogue(tree);

        Assert.Equal(1, _state.GetPlayerVisitCount("simple_dialogue"));
        Assert.True(_state.WasNodeVisited("simple_dialogue", "start"));
    }

    #endregion

    #region Selecting Choices

    [Fact]
    public void SelectChoice_NavigatesToTargetNode()
    {
        var tree = CreateSimpleTree();
        _engine.StartDialogue(tree);

        var choice = _engine.CurrentNode!.Choice[0]; // "Who are you?"
        var nextNode = _engine.SelectChoice(choice);

        Assert.NotNull(nextNode);
        Assert.Equal("who", nextNode.NodeId);
    }

    [Fact]
    public void SelectChoice_WithCost_DeductsCredits()
    {
        var tree = new DialogueTree
        {
            RefName = "paid_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Text = new[] { "Information costs 50 credits." },
                    Choice = new[]
                    {
                        new DialogueChoice
                        {
                            Text = "Pay 50 credits",
                            NextNodeId = "info",
                            Cost = 50
                        }
                    }
                },
                new DialogueNode
                {
                    NodeId = "info",
                    Text = new[] { "Here's the info!" }
                }
            }
        };

        _state.Credits = 100;
        _engine.StartDialogue(tree);

        var choice = _engine.CurrentNode!.Choice[0];
        _engine.SelectChoice(choice);

        Assert.Equal(50, _state.GetCredits());
    }

    [Fact]
    public void SelectChoice_InsufficientCredits_ThrowsException()
    {
        var tree = new DialogueTree
        {
            RefName = "paid_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Choice = new[]
                    {
                        new DialogueChoice
                        {
                            Text = "Pay 50 credits",
                            NextNodeId = "info",
                            Cost = 50
                        }
                    }
                },
                new DialogueNode { NodeId = "info" }
            }
        };

        _state.Credits = 10; // Not enough
        _engine.StartDialogue(tree);

        var choice = _engine.CurrentNode!.Choice[0];
        Assert.Throws<InvalidOperationException>(() => _engine.SelectChoice(choice));
    }

    #endregion

    #region Auto-Advancing

    [Fact]
    public void AdvanceDialogue_NavigatesToNextNode()
    {
        var tree = CreateSimpleTree();
        _engine.StartDialogue(tree);

        // Select "Who are you?" to get to "who" node
        _engine.SelectChoice(_engine.CurrentNode!.Choice[0]);

        // "who" node has NextNodeId="end", so auto-advance should work
        var nextNode = _engine.AdvanceDialogue();

        Assert.NotNull(nextNode);
        Assert.Equal("end", nextNode.NodeId);
    }

    [Fact]
    public void AdvanceDialogue_WithChoices_ThrowsException()
    {
        var tree = CreateSimpleTree();
        _engine.StartDialogue(tree); // Starts at node with choices

        Assert.Throws<InvalidOperationException>(() => _engine.AdvanceDialogue());
    }

    [Fact]
    public void AdvanceDialogue_AtEndOfTree_ReturnsNull()
    {
        var tree = CreateSimpleTree();
        _engine.StartDialogue(tree);
        _engine.SelectChoice(_engine.CurrentNode!.Choice[0]); // Go to "who"
        _engine.AdvanceDialogue(); // Go to "end"

        // "end" node has no NextNodeId and no choices
        var result = _engine.AdvanceDialogue();

        Assert.Null(result);
    }

    #endregion

    #region Conditions

    [Fact]
    public void StartDialogue_WithFailingConditions_SkipsNode()
    {
        var tree = new DialogueTree
        {
            RefName = "conditional_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Condition = new[]
                    {
                        new DialogueCondition
                        {
                            Type = DialogueConditionType.HasQuestToken,
                            RefName = "required_quest"
                        }
                    },
                    Text = new[] { "You have the quest!" },
                    NextNodeId = "fallback"
                },
                new DialogueNode
                {
                    NodeId = "fallback",
                    Text = new[] { "Fallback text" }
                }
            }
        };

        // Player doesn't have quest token, so start node should fail and skip to fallback
        var node = _engine.StartDialogue(tree);

        Assert.NotNull(node);
        Assert.Equal("fallback", node.NodeId);
    }

    [Fact]
    public void Conditions_WithORLogic_PassesIfAnyConditionTrue()
    {
        var tree = new DialogueTree
        {
            RefName = "or_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    ConditionLogic = ConditionLogic.OR,
                    Condition = new[]
                    {
                        new DialogueCondition
                        {
                            Type = DialogueConditionType.Credits,
                            Operator = ComparisonOperator.GreaterThan,
                            Value = "1000" // Player doesn't have this
                        },
                        new DialogueCondition
                        {
                            Type = DialogueConditionType.HasQuestToken,
                            RefName = "quest" // Player has this
                        }
                    },
                    Text = new[] { "Conditions passed!" }
                }
            }
        };

        _state.Credits = 10; // Not enough for first condition
        _state.AddQuestToken("quest"); // But has this

        var node = _engine.StartDialogue(tree);

        Assert.NotNull(node);
        Assert.Equal("start", node.NodeId);
    }

    #endregion

    #region Actions

    [Fact]
    public void StartDialogue_ExecutesActions()
    {
        var tree = new DialogueTree
        {
            RefName = "action_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Text = new[] { "Take this reward!" },
                    Action = new[]
                    {
                        new DialogueAction
                        {
                            Type = DialogueActionType.GiveConsumable,
                            RefName = "health_potion",
                            Amount = 3
                        },
                        new DialogueAction
                        {
                            Type = DialogueActionType.TransferCurrency,
                            Amount = 100
                        }
                    }
                }
            }
        };

        _engine.StartDialogue(tree);

        Assert.Equal(3, _state.GetConsumableQuantity("health_potion"));
        Assert.Equal(100, _state.GetCredits());
    }

    [Fact]
    public void Actions_RaiseEvents_AreCaptured()
    {
        var tree = new DialogueTree
        {
            RefName = "event_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Action = new[]
                    {
                        new DialogueAction
                        {
                            Type = DialogueActionType.OpenMerchantTrade,
                            CharacterRef = "merchant_01"
                        }
                    }
                }
            }
        };

        _engine.StartDialogue(tree);

        Assert.Single(_engine.PendingEvents);
        var evt = _engine.PendingEvents[0] as OpenMerchantTradeEvent;
        Assert.NotNull(evt);
        Assert.Equal("merchant_01", evt.CharacterRef);
    }

    #endregion

    #region Valid Choices

    [Fact]
    public void GetValidChoices_FiltersUnaffordableChoices()
    {
        var tree = new DialogueTree
        {
            RefName = "shop_dialogue",
            StartNodeId = "start",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "start",
                    Choice = new[]
                    {
                        new DialogueChoice { Text = "Buy cheap (10)", NextNodeId = "buy", Cost = 10 },
                        new DialogueChoice { Text = "Buy expensive (100)", NextNodeId = "buy", Cost = 100 },
                        new DialogueChoice { Text = "Leave", NextNodeId = "end" }
                    }
                },
                new DialogueNode { NodeId = "buy" },
                new DialogueNode { NodeId = "end" }
            }
        };

        _state.Credits = 50;
        _engine.StartDialogue(tree);

        var validChoices = _engine.GetValidChoices();

        Assert.Equal(2, validChoices.Length); // Cheap + Leave, but not Expensive
        Assert.Contains(validChoices, c => c.Text == "Buy cheap (10)");
        Assert.Contains(validChoices, c => c.Text == "Leave");
        Assert.DoesNotContain(validChoices, c => c.Text == "Buy expensive (100)");
    }

    #endregion

    #region End Dialogue

    [Fact]
    public void EndDialogue_ClearsState()
    {
        var tree = CreateSimpleTree();
        _engine.StartDialogue(tree);

        _engine.EndDialogue();

        Assert.Null(_engine.CurrentTree);
        Assert.Null(_engine.CurrentNode);
        Assert.Empty(_engine.PendingEvents);
    }

    #endregion

    #region Complex Scenario

    [Fact]
    public void ComplexDialogueFlow_WithConditionsActionsAndChoices()
    {
        var tree = new DialogueTree
        {
            RefName = "quest_dialogue",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Hello! Do you have what I need?" },
                    Choice = new[]
                    {
                        new DialogueChoice { Text = "Check quest", NextNodeId = "check_quest" }
                    }
                },
                new DialogueNode
                {
                    NodeId = "check_quest",
                    Condition = new[]
                    {
                        new DialogueCondition
                        {
                            Type = DialogueConditionType.HasMaterial,
                            RefName = "iron_ore",
                            Operator = ComparisonOperator.GreaterThanOrEqual,
                            Value = "10"
                        }
                    },
                    Text = new[] { "Perfect! Here's your reward." },
                    Action = new[]
                    {
                        new DialogueAction
                        {
                            Type = DialogueActionType.TakeMaterial,
                            RefName = "iron_ore",
                            Amount = 10
                        },
                        new DialogueAction
                        {
                            Type = DialogueActionType.TransferCurrency,
                            Amount = 500
                        },
                        new DialogueAction
                        {
                            Type = DialogueActionType.UnlockAchievement,
                            RefName = "quest_complete"
                        }
                    },
                    NextNodeId = "farewell"
                },
                new DialogueNode
                {
                    NodeId = "farewell",
                    Text = new[] { "Thank you!" }
                }
            }
        };

        // Setup: Player has materials
        _state.AddMaterial("iron_ore", 15);
        _state.Credits = 100;

        // Start dialogue
        var node1 = _engine.StartDialogue(tree);
        Assert.Equal("greeting", node1!.NodeId);

        // Select choice
        var node2 = _engine.SelectChoice(node1.Choice[0]);
        Assert.Equal("check_quest", node2!.NodeId);

        // Verify actions executed
        Assert.Equal(5, _state.GetMaterialQuantity("iron_ore")); // 15 - 10
        Assert.Equal(600, _state.GetCredits()); // 100 + 500
        Assert.True(_state.HasAchievement("quest_complete"));

        // Auto-advance to farewell
        var node3 = _engine.AdvanceDialogue();
        Assert.Equal("farewell", node3!.NodeId);
    }

    #endregion
}
