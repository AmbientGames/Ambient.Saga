using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests.Rpg.Quests;

/// <summary>
/// Unit tests for QuestProgressEvaluator which computes quest progress from transaction logs.
/// </summary>
public class QuestProgressEvaluatorTests
{
    private const string TestAvatarId = "avatar-123";
    private const string TestArcRef = "TestArc";

    private World CreateTestWorld()
    {
        return new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };
    }

    /// <summary>
    /// Adds a character template to the world so trait-based objective
    /// evaluation (which resolves the defeated character's template) can see it.
    /// </summary>
    private static void AddCharacter(World world, string refName, params CharacterTraitType[] traits)
    {
        var character = new Character
        {
            RefName = refName,
            DisplayName = refName,
            Traits = traits.Select(t => new CharacterTrait { Name = t, Value = 1 }).ToArray()
        };
        world.CharactersLookup[refName] = character;
    }

    private ArcTransaction CreateTransaction(ArcTransactionType type, Dictionary<string, string>? data = null, long sequenceNumber = 0)
    {
        var transaction = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = TestAvatarId,
            Status = TransactionStatus.Committed,
            SequenceNumber = sequenceNumber,
            LocalTimestamp = DateTime.UtcNow,
            Data = data ?? new Dictionary<string, string>()
        };

        return transaction;
    }

    [Fact]
    public void EvaluateObjectiveProgress_AfterAbandonAndReAccept_DoesNotInheritPriorProgress()
    {
        // Defeat 3 bandits, abandon, re-accept: the old transactions must not
        // pre-complete the objective of the NEW acceptance
        var quest = new Quest { RefName = "BANDIT_QUEST", DisplayName = "Bandit Quest" };
        var stage = new QuestStage { RefName = "HUNT" };
        var objective = new QuestObjective
        {
            RefName = "DEFEAT_BANDITS",
            Type = QuestObjectiveType.CharacterDefeated,
            CharacterRef = "Bandit",
            Threshold = 3
        };

        var questData = new Dictionary<string, string> { ["QuestRef"] = "BANDIT_QUEST" };
        var defeatData = new Dictionary<string, string> { ["CharacterRef"] = "Bandit" };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.QuestAccepted, new(questData), sequenceNumber: 1),
            CreateTransaction(ArcTransactionType.CharacterDefeated, new(defeatData), sequenceNumber: 2),
            CreateTransaction(ArcTransactionType.CharacterDefeated, new(defeatData), sequenceNumber: 3),
            CreateTransaction(ArcTransactionType.CharacterDefeated, new(defeatData), sequenceNumber: 4),
            CreateTransaction(ArcTransactionType.QuestAbandoned, new(questData), sequenceNumber: 5),
            CreateTransaction(ArcTransactionType.QuestAccepted, new(questData), sequenceNumber: 6)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, new World());

        Assert.Equal(0, progress);

        // ...and a defeat after the re-accept counts normally
        transactions.Add(CreateTransaction(ArcTransactionType.CharacterDefeated, new(defeatData), sequenceNumber: 7));
        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, new World()));
    }

    [Fact]
    public void CharactersDefeatedAtTrigger_SpawnBeforeAcceptance_DefeatAfter_StillCounts()
    {
        // Spawning is quest-independent: a guardian spawned before the quest was
        // accepted must still count when defeated after acceptance, or the
        // objective soft-locks for non-respawning characters.
        var quest = new Quest { RefName = "GUARDIAN_QUEST", DisplayName = "Guardian Quest" };
        var stage = new QuestStage { RefName = "FIGHT" };
        var objective = new QuestObjective
        {
            RefName = "DEFEAT_SITE_GUARDIAN",
            Type = QuestObjectiveType.CharactersDefeatedAtTrigger,
            TriggerRef = "TRIGGER_SITE",
            Threshold = 1
        };

        var instanceId = Guid.NewGuid().ToString();
        var transactions = new List<ArcTransaction>
        {
            // Player walks past the site BEFORE accepting the quest
            CreateTransaction(ArcTransactionType.CharacterSpawned, new Dictionary<string, string>
            {
                ["CharacterInstanceId"] = instanceId,
                ["CharacterRef"] = "SITE_GUARDIAN",
                ["ArcTriggerRef"] = "TRIGGER_SITE"
            }, sequenceNumber: 1),
            CreateTransaction(ArcTransactionType.QuestAccepted, new Dictionary<string, string>
            {
                ["QuestRef"] = "GUARDIAN_QUEST"
            }, sequenceNumber: 2),
            // ...then kills the already-spawned guardian
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterInstanceId"] = instanceId,
                ["CharacterRef"] = "SITE_GUARDIAN"
            }, sequenceNumber: 3)
        };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, CreateTestWorld()));

        // ...and a defeat at a DIFFERENT site never counts
        var otherSiteObjective = new QuestObjective
        {
            RefName = "DEFEAT_OTHER_GUARDIAN",
            Type = QuestObjectiveType.CharactersDefeatedAtTrigger,
            TriggerRef = "TRIGGER_ELSEWHERE",
            Threshold = 1
        };
        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, otherSiteObjective, transactions, CreateTestWorld()));
    }

    private Quest CreateTestQuest()
    {
        // Note: This will use generated classes once XSD is regenerated
        // For now, we'll create a simple quest structure
        return new Quest
        {
            RefName = "TEST_QUEST",
            DisplayName = "Test Quest"
        };
    }

    #region CharacterDefeated Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_CharacterDefeated_CountsCorrectly()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        // Create objective: Defeat 5 hostiles (trait-based; the evaluator resolves
        // each defeated character's template and checks the trait)
        AddCharacter(world, "FROST_DRAGON", CharacterTraitType.Hostile);
        AddCharacter(world, "FIRE_DRAGON", CharacterTraitType.Hostile);
        AddCharacter(world, "SHADOW_DRAGON", CharacterTraitType.Hostile);
        AddCharacter(world, "VILLAGE_ELDER", CharacterTraitType.Friendly);

        var objective = new QuestObjective
        {
            RefName = "DEFEAT_DRAGONS",
            Type = QuestObjectiveType.CharactersDefeatedByTrait,
            Trait = CharacterTraitType.Hostile,
            TraitSpecified = true,
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "HUNT",
            DisplayName = "Hunt Dragons"
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "FROST_DRAGON"
            }),
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "FIRE_DRAGON"
            }),
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "SHADOW_DRAGON"
            }),
            // A friendly kill must NOT count toward the hostile objective
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "VILLAGE_ELDER"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(3, progress);
    }

    [Fact]
    public void IsObjectiveComplete_WhenThresholdMet_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "DEFEAT_BOSS",
            Type = QuestObjectiveType.CharacterDefeated,
            CharacterRef = "DRAGON_BOSS",
            Threshold = 1
        };

        var stage = new QuestStage
        {
            RefName = "BOSS_FIGHT",
            DisplayName = "Boss Fight"
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "DRAGON_BOSS"
            })
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsObjectiveComplete(quest, stage, objective, transactions, world);

        // Assert
        Assert.True(isComplete);
    }

    [Fact]
    public void IsObjectiveComplete_WhenThresholdNotMet_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        AddCharacter(world, "FROST_DRAGON", CharacterTraitType.Hostile);

        var objective = new QuestObjective
        {
            RefName = "DEFEAT_DRAGONS",
            Type = QuestObjectiveType.CharactersDefeatedByTrait,
            Trait = CharacterTraitType.Hostile,
            TraitSpecified = true,
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "HUNT",
            DisplayName = "Hunt Dragons"
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "FROST_DRAGON"
            })
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsObjectiveComplete(quest, stage, objective, transactions, world);

        // Assert
        Assert.False(isComplete);
    }

    #endregion

    #region DialogueCompleted Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_DialogueCompleted_CountsCorrectly()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "TALK_WITNESSES",
            Type = QuestObjectiveType.DialogueCompleted,
            Threshold = 3
        };

        var stage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation"
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_1"
            }),
            CreateTransaction(ArcTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_2"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(2, progress);
    }

    #endregion

    #region ItemCollected Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_ItemCollected_CountsQuantity()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "COLLECT_HERBS",
            Type = QuestObjectiveType.ItemCollected,
            ItemRef = "MOONFLOWER",
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "GATHER",
            DisplayName = "Gather Herbs"
        };

        var transactions = new List<ArcTransaction>
        {
            // Production LootAwarded shape: packed per-family lists ("Ref:Quantity")
            CreateTransaction(ArcTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["Consumables"] = "MOONFLOWER:3"
            }),
            CreateTransaction(ArcTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["Consumables"] = "MOONFLOWER:2"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(5, progress);
    }

    #endregion

    #region IsStageComplete Tests

    [Fact]
    public void IsStageComplete_AllObjectivesComplete_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var stage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation",
            Objectives = new QuestStageObjectives
            {
                Objective = new[]
                {
                    new QuestObjective
                    {
                        RefName = "TALK_WITNESSES",
                        Type = QuestObjectiveType.DialogueCompleted,
                        Threshold = 2,
                        Optional = false
                    },
                    new QuestObjective
                    {
                        RefName = "FIND_WEAPON",
                        Type = QuestObjectiveType.ItemCollected,
                        ItemRef = "BLOODY_KNIFE",
                        Threshold = 1,
                        Optional = false
                    }
                }
            }
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_1"
            }),
            CreateTransaction(ArcTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_2"
            }),
            CreateTransaction(ArcTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["Consumables"] = "BLOODY_KNIFE:1"
            })
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world);

        // Assert
        Assert.True(isComplete);
    }

    [Fact]
    public void IsStageComplete_OptionalObjectiveIncomplete_StillReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var stage = new QuestStage
        {
            RefName = "ESCORT",
            DisplayName = "Escort Mission",
            Objectives = new QuestStageObjectives
            {
                Objective = new[]
                {
                    new QuestObjective
                    {
                        RefName = "REACH_TOWN",
                        Type = QuestObjectiveType.LocationReached,
                        LocationRef = "TOWN",
                        Threshold = 1,
                        Optional = false
                    },
                    new QuestObjective
                    {
                        RefName = "DEFEAT_BANDITS",
                        Type = QuestObjectiveType.CharactersDefeatedByTrait,
                        Trait = CharacterTraitType.Hostile,
                        TraitSpecified = true,
                        Threshold = 5,
                        Optional = true
                    }
                }
            }
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.TriggerActivated, new Dictionary<string, string>
            {
                ["ArcTriggerRef"] = "TOWN"
            })
            // Bandits not defeated - but it's optional
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world);

        // Assert
        Assert.True(isComplete);
    }

    [Fact]
    public void IsStageComplete_RequiredObjectiveIncomplete_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var stage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation",
            Objectives = new QuestStageObjectives
            {
                Objective = new[]
                {
                    new QuestObjective
                    {
                        RefName = "TALK_WITNESSES",
                        Type = QuestObjectiveType.DialogueCompleted,
                        Threshold = 3,
                        Optional = false
                    },
                    new QuestObjective
                    {
                        RefName = "FIND_WEAPON",
                        Type = QuestObjectiveType.ItemCollected,
                        ItemRef = "BLOODY_KNIFE",
                        Threshold = 1,
                        Optional = false
                    }
                }
            }
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_1"
            })
            // Only 1 witness talked to (need 3), weapon not found
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world);

        // Assert
        Assert.False(isComplete);
    }

    #endregion

    #region GetNextStage Tests

    [Fact]
    public void GetNextStage_WithNextStageAttribute_ReturnsNextStage()
    {
        // Arrange
        var quest = CreateTestQuest();

        var currentStage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation",
            NextStage = "ACCUSATION"
        };

        var transactions = new List<ArcTransaction>();

        // Act
        var nextStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

        // Assert
        Assert.Equal("ACCUSATION", nextStage);
    }

    [Fact]
    public void GetNextStage_WithBranchChoice_ReturnsBranchNextStage()
    {
        // Arrange
        var quest = CreateTestQuest();

        var currentStage = new QuestStage
        {
            RefName = "CHOOSE_SIDE",
            DisplayName = "Choose Your Side",
            Branches = new QuestStageBranches
            {
                Branch = new[]
                {
                    new QuestBranch
                    {
                        RefName = "JOIN_REBELS",
                        DisplayName = "Join Rebels",
                        NextStage = "REBEL_MISSIONS"
                    },
                    new QuestBranch
                    {
                        RefName = "JOIN_EMPIRE",
                        DisplayName = "Join Empire",
                        NextStage = "EMPIRE_MISSIONS"
                    }
                }
            }
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.QuestBranchChosen, new Dictionary<string, string>
            {
                ["QuestRef"] = "TEST_QUEST",
                ["StageRef"] = "CHOOSE_SIDE",
                ["BranchRef"] = "JOIN_REBELS"
            })
        };

        // Act
        var nextStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

        // Assert
        Assert.Equal("REBEL_MISSIONS", nextStage);
    }

    [Fact]
    public void GetNextStage_NoNextStage_ReturnsNull()
    {
        // Arrange
        var quest = CreateTestQuest();

        var currentStage = new QuestStage
        {
            RefName = "FINAL_STAGE",
            DisplayName = "Final Stage"
            // NextStage = null (quest complete)
        };

        var transactions = new List<ArcTransaction>();

        // Act
        var nextStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

        // Assert
        Assert.Null(nextStage);
    }

    #endregion

    #region Acceptance Scope / Quest Token Tests

    // DESIGN RULING (2026-07-12): quest tokens are permanent world-knowledge facts.
    // Their producers (dialogue GiveQuestToken) are first-visit-EVER idempotent —
    // the DialogueNodeVisits ledger never resets — so a token excluded by any
    // acceptance scoping could never be re-earned and QuestTokenCollected
    // objectives soft-locked forever (abandon + re-accept, or a token from a prior
    // conversation). QuestTokenCollected therefore counts the FULL committed log,
    // regardless of acceptance scope; every OTHER objective type stays scoped to
    // the current acceptance (see QuestFlowRound5Tests for both sides, and for the
    // accepting-dialogue-session widening that still governs scoped types).

    private const string GiverRef = "QUEST_GIVER";
    private const string TokenRef = "RUMOR_TOKEN";
    private const string TokenQuestRef = "TOKEN_QUEST";

    private static Quest CreateTokenQuest() => new()
    {
        RefName = TokenQuestRef,
        DisplayName = "Token Quest"
    };

    private static QuestObjective CreateTokenObjective() => new()
    {
        RefName = "COLLECT_RUMOR",
        Type = QuestObjectiveType.QuestTokenCollected,
        QuestTokenRef = TokenRef,
        Threshold = 1
    };

    private ArcTransaction DialogueStartedTx(long seq, string characterRef = GiverRef) =>
        CreateTransaction(ArcTransactionType.DialogueStarted, new Dictionary<string, string>
        {
            ["CharacterRef"] = characterRef,
            ["DialogueTreeRef"] = "GIVER_TREE"
        }, seq);

    private ArcTransaction DialogueCompletedTx(long seq, string characterRef = GiverRef) =>
        CreateTransaction(ArcTransactionType.DialogueCompleted, new Dictionary<string, string>
        {
            ["CharacterRef"] = characterRef,
            ["DialogueTreeRef"] = "GIVER_TREE"
        }, seq);

    private ArcTransaction NodeVisitedTx(long seq, string nodeId) =>
        CreateTransaction(ArcTransactionType.DialogueNodeVisited, new Dictionary<string, string>
        {
            ["CharacterRef"] = GiverRef,
            ["DialogueTreeRef"] = "GIVER_TREE",
            ["DialogueNodeId"] = nodeId
        }, seq);

    private ArcTransaction TokenAwardedTx(long seq) =>
        CreateTransaction(ArcTransactionType.QuestTokenAwarded, new Dictionary<string, string>
        {
            ["QuestTokenRef"] = TokenRef,
            ["SourceRef"] = GiverRef
        }, seq);

    private ArcTransaction QuestAcceptedTx(long seq, string giverRef = GiverRef) =>
        CreateTransaction(ArcTransactionType.QuestAccepted, new Dictionary<string, string>
        {
            ["QuestRef"] = TokenQuestRef,
            ["QuestGiverRef"] = giverRef
        }, seq);

    private ArcTransaction QuestAbandonedTx(long seq) =>
        CreateTransaction(ArcTransactionType.QuestAbandoned, new Dictionary<string, string>
        {
            ["QuestRef"] = TokenQuestRef
        }, seq);

    [Fact]
    public void EvaluateObjectiveProgress_TokenGrantedBySameSessionBeforeAccept_CountsTowardObjective()
    {
        // Node grants the token, a later node in the SAME session accepts the quest
        // (session still open when the acceptance is recorded)
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(10),
            NodeVisitedTx(11, "RUMOR_NODE"),
            TokenAwardedTx(12),
            NodeVisitedTx(13, "ACCEPT_NODE"),
            QuestAcceptedTx(14)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_TokenGrantedByTerminalAcceptNode_CountsTowardObjective()
    {
        // Terminal accept node: EndDialogue writes DialogueCompleted BEFORE the
        // handler dispatches AcceptQuestCommand, so the completion precedes the
        // acceptance in the log even though the acceptance came from this session
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(10),
            NodeVisitedTx(11, "ACCEPT_NODE"),
            TokenAwardedTx(12),
            DialogueCompletedTx(13),
            QuestAcceptedTx(14)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_TokenFromPriorCompletedSession_StillCounts()
    {
        // Token earned in a fully completed earlier conversation; the quest is
        // accepted in a NEW session. The token grant is first-visit-EVER idempotent,
        // so excluding it would soft-lock the objective — permanent facts count.
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(1),
            NodeVisitedTx(2, "RUMOR_NODE"),
            TokenAwardedTx(3),
            DialogueCompletedTx(4),
            DialogueStartedTx(5),
            NodeVisitedTx(6, "ACCEPT_NODE"),
            QuestAcceptedTx(7)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_TokenFromPriorSession_NonDialogueAcceptance_StillCounts()
    {
        // Session with the giver ends, unrelated gameplay happens, then the quest is
        // accepted outside dialogue — the already-earned token still counts (it can
        // never be re-earned, so it must bridge into the acceptance)
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(1),
            TokenAwardedTx(2),
            DialogueCompletedTx(3),
            CreateTransaction(ArcTransactionType.TriggerActivated, new Dictionary<string, string>
            {
                ["ArcTriggerRef"] = "QUEST_BOARD"
            }, 4),
            QuestAcceptedTx(5)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_AbandonAndReAccept_TokenFromFirstAcceptanceStillCounts()
    {
        // H10 regression: abandon + re-accept starts a fresh scope for every other
        // objective type, but the token was granted by a first-visit-EVER node —
        // if the re-acceptance excluded it, the quest soft-locked permanently
        // (EverestTrail HiddenCache_Quest). Tokens alone bridge abandon/re-accept.
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(1),
            NodeVisitedTx(2, "ACCEPT_NODE"),
            TokenAwardedTx(3),
            QuestAcceptedTx(4),
            DialogueCompletedTx(5),
            QuestAbandonedTx(6),
            DialogueStartedTx(7),
            NodeVisitedTx(8, "ACCEPT_NODE"),
            QuestAcceptedTx(9)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_DirectReAcceptAfterAbandon_TokenStillCounts()
    {
        // Abandon and re-accept happen OUTSIDE dialogue right after the session
        // ended — the permanently-idempotent token still bridges into the new run
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(1),
            TokenAwardedTx(2),
            QuestAcceptedTx(3),
            DialogueCompletedTx(4),
            QuestAbandonedTx(5),
            QuestAcceptedTx(6)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_TokensCountRegardlessOfAcceptance()
    {
        // No DialogueStarted anywhere: the pre-acceptance token counts (permanent
        // fact), and a post-acceptance token adds to it
        var transactions = new List<ArcTransaction>
        {
            TokenAwardedTx(1),
            QuestAcceptedTx(2)
        };

        var quest = CreateTokenQuest();
        var stage = new QuestStage { RefName = "STAGE_1" };

        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            quest, stage, CreateTokenObjective(), transactions, CreateTestWorld()));

        transactions.Add(TokenAwardedTx(3));

        Assert.Equal(2, QuestProgressEvaluator.EvaluateObjectiveProgress(
            quest, stage, CreateTokenObjective(), transactions, CreateTestWorld()));
    }

    [Fact]
    public void EvaluateObjectiveProgress_OtherCharactersSessionBetweenGiverSessionAndAccept_UsesGiverSession()
    {
        // The giver's session is still open while a nested conversation with another
        // character starts and completes (e.g. proximity NPC chatter) — the OTHER
        // character's completion must not disqualify the giver's open session
        var transactions = new List<ArcTransaction>
        {
            DialogueStartedTx(1),
            TokenAwardedTx(2),
            DialogueStartedTx(3, "BYSTANDER"),
            DialogueCompletedTx(4, "BYSTANDER"),
            NodeVisitedTx(5, "ACCEPT_NODE"),
            QuestAcceptedTx(6)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTokenQuest(), new QuestStage { RefName = "STAGE_1" }, CreateTokenObjective(), transactions, CreateTestWorld());

        Assert.Equal(1, progress);
    }

    #endregion

    #region LocationReached Arc Scoping Tests

    // Shipped LocationReached objectives carry ArcRef (the TARGET arc) and no
    // LocationRef. The evaluator receives the avatar's CROSS-ARC log (see
    // CrossArcQuestTransactionLog), so "reach the location" must mean "activate one
    // of the target arc's triggers" — NOT "any trigger anywhere", which would let
    // one unrelated activation walk an entire discovery chain to completion.

    private static World CreateTwoArcWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate { Gameplay = new GameplayComponents() }
        };
        world.ArcTriggersLookup["TargetArc"] = new List<ArcTrigger>
        {
            new ArcTrigger { RefName = "TARGET_ARC_ENTRANCE" },
            new ArcTrigger { RefName = "TARGET_ARC_PLAZA" }
        };
        world.ArcTriggersLookup["OtherArc"] = new List<ArcTrigger>
        {
            new ArcTrigger { RefName = "OTHER_ARC_ENTRANCE" }
        };
        return world;
    }

    private static QuestObjective CreateReachTargetArcObjective() => new()
    {
        RefName = "REACH_TARGET",
        Type = QuestObjectiveType.LocationReached,
        ArcRef = "TargetArc",
        Threshold = 1
    };

    private ArcTransaction TriggerActivatedTx(string triggerRef, long seq = 0) =>
        CreateTransaction(ArcTransactionType.TriggerActivated, new Dictionary<string, string>
        {
            ["ArcTriggerRef"] = triggerRef
        }, seq);

    [Fact]
    public void LocationReached_WithArcRef_TriggerFromDifferentArc_DoesNotCount()
    {
        var transactions = new List<ArcTransaction>
        {
            TriggerActivatedTx("OTHER_ARC_ENTRANCE")
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTestQuest(), new QuestStage { RefName = "TRAVEL" },
            CreateReachTargetArcObjective(), transactions, CreateTwoArcWorld());

        Assert.Equal(0, progress);
    }

    [Fact]
    public void LocationReached_WithArcRef_TriggerFromTargetArc_Counts()
    {
        var transactions = new List<ArcTransaction>
        {
            TriggerActivatedTx("OTHER_ARC_ENTRANCE", 1),
            TriggerActivatedTx("TARGET_ARC_PLAZA", 2)
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTestQuest(), new QuestStage { RefName = "TRAVEL" },
            CreateReachTargetArcObjective(), transactions, CreateTwoArcWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void LocationReached_WithArcRef_UnresolvableArc_FailsClosed()
    {
        // A bad arc ref must not degrade to "any trigger counts" — that would
        // silently reintroduce the unconditioned completion this scoping prevents.
        // The strict content gate is responsible for catching the bad ref.
        var objective = new QuestObjective
        {
            RefName = "REACH_NOWHERE",
            Type = QuestObjectiveType.LocationReached,
            ArcRef = "ArcThatDoesNotExist",
            Threshold = 1
        };

        var transactions = new List<ArcTransaction>
        {
            TriggerActivatedTx("TARGET_ARC_ENTRANCE")
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTestQuest(), new QuestStage { RefName = "TRAVEL" },
            objective, transactions, CreateTwoArcWorld());

        Assert.Equal(0, progress);
    }

    [Fact]
    public void LocationReached_NoDiscriminator_AnyTriggerCounts_LegacyBehavior()
    {
        // Stock content (Ise/Kagoshima main-chain and CONNECT quests) authors
        // LocationReached with neither LocationRef nor ArcRef; those worlds
        // must keep completing on any trigger activation or they brick on load.
        var objective = new QuestObjective
        {
            RefName = "REACH_ANYWHERE",
            Type = QuestObjectiveType.LocationReached,
            Threshold = 1
        };

        var transactions = new List<ArcTransaction>
        {
            TriggerActivatedTx("OTHER_ARC_ENTRANCE")
        };

        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTestQuest(), new QuestStage { RefName = "TRAVEL" },
            objective, transactions, CreateTwoArcWorld());

        Assert.Equal(1, progress);
    }

    [Fact]
    public void LocationReached_WithLocationRef_OnlyThatTriggerCounts()
    {
        // An authored LocationRef is the finest-grained discriminator and wins
        // outright: another trigger from the SAME arc must not satisfy it.
        var objective = new QuestObjective
        {
            RefName = "REACH_PLAZA",
            Type = QuestObjectiveType.LocationReached,
            ArcRef = "TargetArc",
            LocationRef = "TARGET_ARC_PLAZA",
            Threshold = 1
        };

        var wrongTrigger = new List<ArcTransaction> { TriggerActivatedTx("TARGET_ARC_ENTRANCE") };
        Assert.Equal(0, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTestQuest(), new QuestStage { RefName = "TRAVEL" },
            objective, wrongTrigger, CreateTwoArcWorld()));

        wrongTrigger.Add(TriggerActivatedTx("TARGET_ARC_PLAZA", 2));
        Assert.Equal(1, QuestProgressEvaluator.EvaluateObjectiveProgress(
            CreateTestQuest(), new QuestStage { RefName = "TRAVEL" },
            objective, wrongTrigger, CreateTwoArcWorld()));
    }

    #endregion

    #region CurrencyCollected Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_CurrencyCollected_SumsPositiveAmounts()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "COLLECT_GOLD",
            Type = QuestObjectiveType.CurrencyCollected,
            Threshold = 1000
        };

        var stage = new QuestStage
        {
            RefName = "EARNING",
            DisplayName = "Earn Gold"
        };

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "500"
            }),
            CreateTransaction(ArcTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "300"
            }),
            // Negative amount (spending) should NOT count
            CreateTransaction(ArcTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "-100"
            }),
            CreateTransaction(ArcTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "250"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(1050, progress); // 500 + 300 + 250 = 1050 (ignoring -100)
    }

    #endregion

}
