using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests.Rpg.Quests;

/// <summary>
/// Round-5 audit regression tests for QuestProgressEvaluator.
///
/// H10 — abandon + re-accept permanently soft-locked token-driven quests: the fresh
/// acceptance scope excluded prior QuestTokenAwarded, but dialogue token grants are
/// first-visit-EVER idempotent (the DialogueNodeVisits ledger never resets), so the
/// token could never be re-earned. DESIGN RULING (2026-07-12): quest tokens are
/// permanent world-knowledge facts — QuestTokenCollected counts the FULL committed
/// log, regardless of acceptance scope. Every other objective type stays scoped.
///
/// Quest LOWs fixed alongside:
/// - CharactersDefeatedByTrait honors runtime TraitAssigned/TraitRemoved, not just
///   template traits.
/// - ItemDelivered honors the objective's CharacterRef (sales to unrelated
///   characters no longer count).
/// - ItemCollected measures the PEAK net count (buy-sell-rebuy no longer
///   double-counts; a same-stage delivery doesn't un-complete the collect).
/// </summary>
public class QuestFlowRound5Tests
{
    private const string TestAvatarId = "avatar-round5";
    private const string GiverRef = "CACHE_KEEPER";
    private const string TokenRef = "TOKEN_CACHE_LOCATION";
    private const string QuestRef = "HIDDEN_CACHE_QUEST";

    private static World CreateTestWorld()
    {
        return new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };
    }

    private static void AddCharacter(World world, string refName, params CharacterTraitType[] traits)
    {
        world.CharactersLookup[refName] = new Character
        {
            RefName = refName,
            DisplayName = refName,
            Traits = traits.Select(t => new CharacterTrait { Name = t, Value = 1 }).ToArray()
        };
    }

    private static ArcTransaction Tx(ArcTransactionType type, long seq, Dictionary<string, string>? data = null) => new()
    {
        TransactionId = Guid.NewGuid(),
        Type = type,
        AvatarId = TestAvatarId,
        Status = TransactionStatus.Committed,
        SequenceNumber = seq,
        LocalTimestamp = DateTime.UtcNow,
        Data = data ?? new Dictionary<string, string>()
    };

    private static Quest CreateQuest() => new() { RefName = QuestRef, DisplayName = "Hidden Cache" };

    private static QuestStage Stage(string refName = "STAGE_1") => new() { RefName = refName };

    #region H10 — tokens bridge abandon/re-accept

    [Fact]
    public void HiddenCacheStyle_AbandonThenReAccept_TokenObjectiveStillCompletes()
    {
        // The HiddenCache shape: the giver's first-visit-only dialogue node grants
        // the cache-location token and accepts the quest. The player abandons and
        // re-accepts — the node never re-awards (visit ledger is permanent), so the
        // token MUST bridge into the new acceptance or the quest is soft-locked
        // forever.
        var tokenObjective = new QuestObjective
        {
            RefName = "LEARN_CACHE_LOCATION",
            Type = QuestObjectiveType.QuestTokenCollected,
            QuestTokenRef = TokenRef,
            Threshold = 1
        };

        var questData = new Dictionary<string, string>
        {
            ["QuestRef"] = QuestRef,
            ["QuestGiverRef"] = GiverRef
        };

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.DialogueStarted, 1, new() { ["CharacterRef"] = GiverRef }),
            Tx(ArcTransactionType.DialogueNodeVisited, 2, new()
            {
                ["CharacterRef"] = GiverRef,
                ["DialogueTreeRef"] = "GIVER_TREE",
                ["DialogueNodeId"] = "RUMOR_NODE"
            }),
            Tx(ArcTransactionType.QuestTokenAwarded, 3, new() { ["QuestTokenRef"] = TokenRef }),
            Tx(ArcTransactionType.QuestAccepted, 4, new(questData)),
            Tx(ArcTransactionType.DialogueCompleted, 5, new() { ["CharacterRef"] = GiverRef }),
            // Player abandons...
            Tx(ArcTransactionType.QuestAbandoned, 6, new() { ["QuestRef"] = QuestRef }),
            // ...talks to the giver again (node does NOT re-award: first-visit-ever)
            Tx(ArcTransactionType.DialogueStarted, 7, new() { ["CharacterRef"] = GiverRef }),
            Tx(ArcTransactionType.DialogueNodeVisited, 8, new()
            {
                ["CharacterRef"] = GiverRef,
                ["DialogueTreeRef"] = "GIVER_TREE",
                ["DialogueNodeId"] = "RUMOR_NODE"
            }),
            Tx(ArcTransactionType.QuestAccepted, 9, new(questData))
        };

        var world = CreateTestWorld();
        var quest = CreateQuest();
        var stage = new QuestStage
        {
            RefName = "FIND_CACHE",
            Objectives = new QuestStageObjectives { Objective = new[] { tokenObjective } }
        };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, tokenObjective, transactions, world));
        Assert.True(QuestProgressEvaluator.IsObjectiveComplete(quest, stage, tokenObjective, transactions, world));
        Assert.True(QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world));
    }

    [Fact]
    public void UncommittedTokenAward_DoesNotCount()
    {
        // The token bypass skips acceptance scoping, NOT the committed filter
        var tokenObjective = new QuestObjective
        {
            RefName = "LEARN_CACHE_LOCATION",
            Type = QuestObjectiveType.QuestTokenCollected,
            QuestTokenRef = TokenRef,
            Threshold = 1
        };

        var pending = Tx(ArcTransactionType.QuestTokenAwarded, 1, new() { ["QuestTokenRef"] = TokenRef });
        pending.Status = TransactionStatus.Pending;

        var transactions = new List<ArcTransaction>
        {
            pending,
            Tx(ArcTransactionType.QuestAccepted, 2, new() { ["QuestRef"] = QuestRef })
        };

        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), tokenObjective, transactions, CreateTestWorld()));
    }

    [Theory]
    [InlineData(QuestObjectiveType.CharacterDefeated)]
    [InlineData(QuestObjectiveType.TriggerActivated)]
    [InlineData(QuestObjectiveType.DialogueCompleted)]
    public void OtherObjectiveTypes_StillResetOnReAccept(QuestObjectiveType objectiveType)
    {
        // The token bridging is an exception for permanent facts only — repeatable
        // progress (defeats, trigger visits, conversations) must still reset when
        // the quest is abandoned and re-accepted.
        var objective = objectiveType switch
        {
            QuestObjectiveType.CharacterDefeated => new QuestObjective
            {
                RefName = "OBJ",
                Type = objectiveType,
                CharacterRef = "BANDIT",
                Threshold = 1
            },
            QuestObjectiveType.TriggerActivated => new QuestObjective
            {
                RefName = "OBJ",
                Type = objectiveType,
                TriggerRef = "CACHE_TRIGGER",
                Threshold = 1
            },
            _ => new QuestObjective
            {
                RefName = "OBJ",
                Type = objectiveType,
                Threshold = 1
            }
        };

        var progressTx = objectiveType switch
        {
            QuestObjectiveType.CharacterDefeated =>
                Tx(ArcTransactionType.CharacterDefeated, 2, new() { ["CharacterRef"] = "BANDIT" }),
            QuestObjectiveType.TriggerActivated =>
                Tx(ArcTransactionType.TriggerActivated, 2, new() { ["ArcTriggerRef"] = "CACHE_TRIGGER" }),
            _ => Tx(ArcTransactionType.DialogueCompleted, 2, new() { ["CharacterRef"] = "WITNESS" })
        };

        var questData = new Dictionary<string, string> { ["QuestRef"] = QuestRef };
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new(questData)),
            progressTx,
            Tx(ArcTransactionType.QuestAbandoned, 3, new(questData)),
            Tx(ArcTransactionType.QuestAccepted, 4, new(questData))
        };

        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), objective, transactions, CreateTestWorld()));
    }

    #endregion

    #region Accepting-session widening still governs SCOPED types

    // The token tests used to be the coverage for the accepting-dialogue-session
    // scope widening (events flushed before the nested AcceptQuestCommand get a
    // LOWER sequence number than QuestAccepted). Tokens are unscoped now, so pin
    // that behavior with a scoped type: DialogueNodeVisited.

    private static QuestObjective NodeVisitObjective() => new()
    {
        RefName = "VISIT_RUMOR_NODE",
        Type = QuestObjectiveType.DialogueNodeVisited,
        DialogueRef = "GIVER_TREE",
        NodeRef = "RUMOR_NODE",
        Threshold = 1
    };

    private static ArcTransaction NodeVisitTx(long seq) => Tx(ArcTransactionType.DialogueNodeVisited, seq, new()
    {
        ["CharacterRef"] = GiverRef,
        ["DialogueTreeRef"] = "GIVER_TREE",
        ["DialogueNodeId"] = "RUMOR_NODE"
    });

    [Fact]
    public void NodeVisitInAcceptingSession_BeforeAccept_CountsTowardScopedObjective()
    {
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.DialogueStarted, 1, new() { ["CharacterRef"] = GiverRef }),
            NodeVisitTx(2),
            Tx(ArcTransactionType.QuestAccepted, 3, new()
            {
                ["QuestRef"] = QuestRef,
                ["QuestGiverRef"] = GiverRef
            })
        };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), NodeVisitObjective(), transactions, CreateTestWorld()));
    }

    [Fact]
    public void NodeVisitFromPriorSealedSession_DoesNotCountTowardScopedObjective()
    {
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.DialogueStarted, 1, new() { ["CharacterRef"] = GiverRef }),
            NodeVisitTx(2),
            Tx(ArcTransactionType.DialogueCompleted, 3, new() { ["CharacterRef"] = GiverRef }),
            Tx(ArcTransactionType.DialogueStarted, 4, new() { ["CharacterRef"] = GiverRef }),
            Tx(ArcTransactionType.QuestAccepted, 5, new()
            {
                ["QuestRef"] = QuestRef,
                ["QuestGiverRef"] = GiverRef
            })
        };

        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), NodeVisitObjective(), transactions, CreateTestWorld()));
    }

    #endregion

    #region LOW — ItemCollected peak-net counting

    private static QuestObjective CollectObjective(int threshold = 1) => new()
    {
        RefName = "COLLECT_WIDGETS",
        Type = QuestObjectiveType.ItemCollected,
        ItemRef = "WIDGET",
        Threshold = threshold
    };

    private static ArcTransaction TradeTx(long seq, bool buying, int quantity, string itemRef = "WIDGET", string? instanceId = null)
    {
        var data = new Dictionary<string, string>
        {
            ["ItemRef"] = itemRef,
            ["IsBuying"] = buying.ToString(),
            ["Quantity"] = quantity.ToString()
        };
        if (instanceId != null)
            data["CharacterInstanceId"] = instanceId;
        return Tx(ArcTransactionType.ItemTraded, seq, data);
    }

    [Fact]
    public void ItemCollected_BuySellRebuy_DoesNotDoubleCount()
    {
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            TradeTx(2, buying: true, quantity: 1),
            TradeTx(3, buying: false, quantity: 1),
            TradeTx(4, buying: true, quantity: 1)
        };

        // Old counting summed the two buys to 2; the peak net held is 1
        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), CollectObjective(2), transactions, CreateTestWorld()));
    }

    [Fact]
    public void ItemCollected_SellingAfterCollecting_DoesNotUncompleteTheObjective()
    {
        // Same-stage "collect 5, deliver 5": the delivery (a trade away) must not
        // drop the collect objective back below its threshold — the measure is the
        // peak net count reached, not the final net.
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            TradeTx(2, buying: true, quantity: 5),
            TradeTx(3, buying: false, quantity: 5)
        };

        Assert.Equal(5, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), CollectObjective(5), transactions, CreateTestWorld()));
    }

    [Fact]
    public void ItemCollected_SellingPreOwnedItems_DoesNotGoNegative()
    {
        // Selling items acquired BEFORE the acceptance scope must not push the
        // running net negative and swallow later legitimate acquisitions
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            TradeTx(2, buying: false, quantity: 3),
            TradeTx(3, buying: true, quantity: 2)
        };

        Assert.Equal(2, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), CollectObjective(2), transactions, CreateTestWorld()));
    }

    [Fact]
    public void ItemCollected_LootAndPurchasesCombine()
    {
        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            Tx(ArcTransactionType.LootAwarded, 2, new() { ["Consumables"] = "WIDGET:3" }),
            TradeTx(3, buying: true, quantity: 2)
        };

        Assert.Equal(5, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), CollectObjective(5), transactions, CreateTestWorld()));
    }

    #endregion

    #region LOW — ItemDelivered recipient discrimination

    [Fact]
    public void ItemDelivered_WithCharacterRef_OnlySalesToThatCharacterCount()
    {
        var recipientInstance = Guid.NewGuid().ToString();
        var strangerInstance = Guid.NewGuid().ToString();

        var objective = new QuestObjective
        {
            RefName = "DELIVER_MEDICINE",
            Type = QuestObjectiveType.ItemDelivered,
            ItemRef = "MEDICINE",
            CharacterRef = "VILLAGE_DOCTOR",
            Threshold = 1
        };

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.CharacterSpawned, 1, new()
            {
                ["CharacterInstanceId"] = recipientInstance,
                ["CharacterRef"] = "VILLAGE_DOCTOR"
            }),
            Tx(ArcTransactionType.CharacterSpawned, 2, new()
            {
                ["CharacterInstanceId"] = strangerInstance,
                ["CharacterRef"] = "PASSING_MERCHANT"
            }),
            Tx(ArcTransactionType.QuestAccepted, 3, new() { ["QuestRef"] = QuestRef }),
            // Selling to the wrong character must not count...
            TradeTx(4, buying: false, quantity: 1, itemRef: "MEDICINE", instanceId: strangerInstance)
        };

        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), objective, transactions, CreateTestWorld()));

        // ...and delivering to the named recipient does
        transactions.Add(TradeTx(5, buying: false, quantity: 1, itemRef: "MEDICINE", instanceId: recipientInstance));

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), objective, transactions, CreateTestWorld()));
    }

    [Fact]
    public void ItemDelivered_WithoutCharacterRef_AnySaleCounts()
    {
        var objective = new QuestObjective
        {
            RefName = "OFFLOAD_CARGO",
            Type = QuestObjectiveType.ItemDelivered,
            ItemRef = "CARGO",
            Threshold = 2
        };

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            TradeTx(2, buying: false, quantity: 2, itemRef: "CARGO")
        };

        Assert.Equal(2, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), objective, transactions, CreateTestWorld()));
    }

    #endregion

    #region LOW — CharactersDefeatedByTrait honors runtime trait changes

    private static QuestObjective HostileDefeatObjective(int threshold = 1) => new()
    {
        RefName = "DEFEAT_HOSTILES",
        Type = QuestObjectiveType.CharactersDefeatedByTrait,
        Trait = CharacterTraitType.Hostile,
        TraitSpecified = true,
        Threshold = threshold
    };

    [Fact]
    public void CharactersDefeatedByTrait_RuntimeTraitAssigned_Counts()
    {
        // A peaceful character turned Hostile at play time (dialogue AssignTrait)
        // must count toward the trait objective even though its TEMPLATE lacks it
        var world = CreateTestWorld();
        AddCharacter(world, "TURNCOAT", CharacterTraitType.Friendly);

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            Tx(ArcTransactionType.TraitAssigned, 2, new()
            {
                ["CharacterRef"] = "TURNCOAT",
                ["TraitType"] = "Hostile"
            }),
            Tx(ArcTransactionType.CharacterDefeated, 3, new() { ["CharacterRef"] = "TURNCOAT" })
        };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), HostileDefeatObjective(), transactions, world));
    }

    [Fact]
    public void CharactersDefeatedByTrait_TraitRemovedBeforeDefeat_DoesNotCount()
    {
        // The inverse: a template-Hostile character pacified before the defeat
        var world = CreateTestWorld();
        AddCharacter(world, "PACIFIED_BANDIT", CharacterTraitType.Hostile);

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            Tx(ArcTransactionType.TraitRemoved, 2, new()
            {
                ["CharacterRef"] = "PACIFIED_BANDIT",
                ["TraitType"] = "Hostile"
            }),
            Tx(ArcTransactionType.CharacterDefeated, 3, new() { ["CharacterRef"] = "PACIFIED_BANDIT" })
        };

        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), HostileDefeatObjective(), transactions, world));
    }

    [Fact]
    public void CharactersDefeatedByTrait_TraitAssignedBeforeAcceptance_StillApplies()
    {
        // Trait changes are world state, not quest progress: an assignment from
        // BEFORE the acceptance still describes the character at the (scoped) defeat
        var world = CreateTestWorld();
        AddCharacter(world, "OLD_ENEMY", CharacterTraitType.Friendly);

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.TraitAssigned, 1, new()
            {
                ["CharacterRef"] = "OLD_ENEMY",
                ["TraitType"] = "Hostile"
            }),
            Tx(ArcTransactionType.QuestAccepted, 2, new() { ["QuestRef"] = QuestRef }),
            Tx(ArcTransactionType.CharacterDefeated, 3, new() { ["CharacterRef"] = "OLD_ENEMY" })
        };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), HostileDefeatObjective(), transactions, world));
    }

    [Fact]
    public void CharactersDefeatedByTrait_TemplateTrait_StillCounts()
    {
        // The template path is unchanged when no runtime trait event exists
        var world = CreateTestWorld();
        AddCharacter(world, "PLAIN_BANDIT", CharacterTraitType.Hostile);

        var transactions = new List<ArcTransaction>
        {
            Tx(ArcTransactionType.QuestAccepted, 1, new() { ["QuestRef"] = QuestRef }),
            Tx(ArcTransactionType.CharacterDefeated, 2, new() { ["CharacterRef"] = "PLAIN_BANDIT" })
        };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateQuest(), Stage(), HostileDefeatObjective(), transactions, world));
    }

    #endregion
}
