using System.Linq;
using Ambient.Domain;
using Ambient.Domain.Contracts;

namespace Ambient.Rpg.Engine.Domain.Battle;

/// <summary>
/// Current state of the battle.
/// </summary>
public enum BattleState
{
    NotStarted,
    AvatarTurn,
    CompanionTurn,
    EnemyTurn,
    /// <summary>
    /// Waiting for avatar to choose a defensive reaction.
    /// Enemy has telegraphed their attack; avatar has a time window to respond.
    /// </summary>
    AwaitingReaction,
    Victory,
    Defeat,
    Fled
}

/// <summary>
/// Core turn-based battle engine.
/// Framework-agnostic - handles all combat logic independently of UI.
/// Server and client use this same logic to ensure consistent combat resolution.
/// The engine executes decisions made by IBattleMind implementations.
/// </summary>
public class BattleEngine
{
    // Balance constants for damage calculation
    private const float WEAPON_DAMAGE_MULTIPLIER = 2.5f;  // Weapons scale with Strength
    private const float SPELL_DAMAGE_MULTIPLIER = 3.0f;   // Spells scale with Magic
    private const float BASE_DAMAGE_MINIMUM = 0.05f;      // Minimum 5% damage from weapon/spell attacks

    // Hand slot constants - these three slots have special mutual exclusivity rules
    private const string MainHandSlot = "MainHand";
    private const string OffHandSlot = "OffHand";
    private const string BothHandsSlot = "BothHands";

    private readonly Combatant _avatar;
    private readonly List<Combatant> _companions;  // Party members (excluding avatar)
    private readonly Combatant _enemy;
    private readonly ICombatAI? _enemyMind;  // Tactical AI for enemy
    private readonly ICombatAI? _companionMind;  // AI for companion turns
    private readonly Random _random;
    private readonly IWorld _world;  // Needed for affinity lookups and item resolution

    private int _turnNumber;
    private int _currentCompanionIndex;  // Which companion's turn it is
    private readonly List<CombatEvent> _actionHistory = new();

    public BattleState State { get; private set; }
    public List<string> CombatLog { get; } = new();
    public List<string> AvatarAffinityRefs { get; private set; } = new();
    public IReadOnlyList<CombatEvent> ActionHistory => _actionHistory.AsReadOnly();

    /// <summary>
    /// The pending attack awaiting avatar reaction (only valid when State == AwaitingReaction).
    /// </summary>
    public PendingAttack? PendingAttack { get; private set; }

    /// <summary>
    /// The enemy's opening action from <see cref="StartBattle"/> — including FAILED
    /// openings (fumbles), which never enter <see cref="ActionHistory"/>. Persisting
    /// only successful openings left battles with zero turn transactions, which the
    /// state query read as a wedged EnemyTurn.
    /// </summary>
    public CombatEvent? OpeningAction { get; private set; }

    /// <summary>
    /// Attack tells available for this battle (loaded from enemy/character data).
    /// </summary>
    private readonly Dictionary<string, AttackTell> _attackTells = new();

    /// <summary>
    /// All party members (avatar + companions) for UI display.
    /// </summary>
    public IReadOnlyList<Combatant> Party => new[] { _avatar }.Concat(_companions).ToList().AsReadOnly();

    /// <summary>
    /// Current companion taking their turn (null if not companion turn).
    /// </summary>
    public Combatant? CurrentCompanion => State == BattleState.CompanionTurn && _currentCompanionIndex < _companions.Count
        ? _companions[_currentCompanionIndex]
        : null;

    /// <summary>
    /// Create a new battle engine.
    /// </summary>
    /// <param name="avatar">The avatar combatant</param>
    /// <param name="enemy">The enemy combatant</param>
    /// <param name="enemyMind">AI brain for enemy tactical decisions (optional for player-vs-player)</param>
    /// <param name="world">World data for affinity matchups and item lookups (required for advanced combat)</param>
    /// <param name="randomSeed">Optional seed for deterministic behavior in tests</param>
    /// <param name="companions">Optional party companions who fight alongside the avatar</param>
    /// <param name="companionMind">AI brain for companion tactical decisions (uses enemyMind if not provided)</param>
    public BattleEngine(Combatant avatar, Combatant enemy, ICombatAI? enemyMind = null, IWorld world = null, int? randomSeed = null,
        List<Combatant>? companions = null, ICombatAI? companionMind = null)
    {
        _avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
        _enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
        _companions = companions ?? new List<Combatant>();
        _enemyMind = enemyMind;
        _companionMind = companionMind ?? enemyMind;  // Companions use same AI as enemy if not specified
        _world = world;
        _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        State = BattleState.NotStarted;
        _turnNumber = 0;
        _currentCompanionIndex = 0;
    }

    /// <summary>
    /// Start the battle and determine turn order.
    /// </summary>
    public void StartBattle()
    {
        if (State != BattleState.NotStarted)
            return;

        CombatLog.Add("=== BATTLE START ===");
        if (_companions.Count > 0)
        {
            var partyNames = string.Join(", ", _companions.Select(c => c.DisplayName));
            CombatLog.Add($"{_avatar.DisplayName} (with {partyNames}) vs {_enemy.DisplayName}!");
        }
        else
        {
            CombatLog.Add($"{_avatar.DisplayName} vs {_enemy.DisplayName}!");
        }

        // Opponent always initiates the interaction
        State = BattleState.EnemyTurn;
        CombatLog.Add($"{_enemy.DisplayName} initiates combat!");
        OpeningAction = ExecuteEnemyTurn();

        _turnNumber = 1;
    }

    /// <summary>
    /// Resume a battle mid-fight from externally reconstructed combatant state.
    /// Persisted turn results are folded into the combatants by the caller
    /// (see ExecuteBattleTurnHandler.ReconstructBattleState); no turns are
    /// re-simulated, so replay cannot double-apply damage or diverge from the
    /// recorded history. Command boundaries always sit at the avatar's turn.
    /// </summary>
    public void ResumeBattle(int turnNumber)
    {
        if (State != BattleState.NotStarted)
            return;

        State = BattleState.AvatarTurn;
        _turnNumber = Math.Max(1, turnNumber);
        CombatLog.Add($"=== BATTLE RESUMED (turn {_turnNumber}) ===");
    }

    /// <summary>
    /// Execute the enemy's turn using their IBattleMind tactical AI.
    /// </summary>
    public CombatEvent ExecuteEnemyTurn()
    {
        if (State != BattleState.EnemyTurn)
        {
            return new CombatEvent
            {
                Success = false,
                Message = "Not enemy's turn"
            };
        }

        // Process status effects at start of enemy's turn
        ProcessStatusEffects(_enemy);

        // Check if enemy died from DoT
        if (!_enemy.IsAlive)
        {
            CheckBattleEnd();
            // Fully stamped: this event is persisted as a turn transaction, and the
            // reconstruction/persistence fold reads the health/energy fields back
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack, // Placeholder — no action taken
                ActorName = _enemy.DisplayName,
                TargetName = _enemy.DisplayName,
                Success = true,
                Message = $"{_enemy.DisplayName} succumbed to status effects!",
                TurnNumber = _turnNumber,
                IsAvatarTurn = false,
                TargetHealthAfter = _enemy.Health,
                ActorEnergyAfter = _enemy.Stamina
            };
        }

        // Select target from party (avatar + alive companions)
        var target = SelectEnemyTarget();

        if (_enemyMind == null)
        {
            // Fallback: basic attack if no AI provided. Routed through ExecuteDecision
            // so action-blocking status effects (Stun) and event stamping apply on
            // this path too — the direct ExecuteAttack call let a stunned enemy act.
            var fallbackAction = ExecuteDecision(_enemy, target, new CombatAction { ActionType = ActionType.Attack });
            RecordAction(fallbackAction);

            if (fallbackAction.Success)
            {
                CheckBattleEnd();
            }
            else if (State == BattleState.EnemyTurn)
            {
                // Mirror the AI path below: a blocked enemy action yields the turn
                CombatLog.Add($"{_enemy.DisplayName} fumbles and loses the turn!");
                YieldFumbledEnemyTurn();
            }

            return fallbackAction;
        }

        // AI decides what to do based on observable battle state
        var snapshot = CreateBattleSnapshot(forEnemy: true);
        var decision = _enemyMind.DecideTurn(snapshot);

        // If the AI chose an OFFENSIVE action and tells are available, enter the
        // reaction phase instead of dealing damage immediately — gives the avatar a
        // chance to react. Defensive casts (heals/buffs) execute directly: routing
        // them through the tell path used to hijack an enemy self-heal into a
        // phantom telegraphed ATTACK on the player.
        // A stunned enemy cannot act at all: don't let its attack sidestep the stun
        // check by telegraphing — ExecuteDecision below turns it into a lost turn.
        if (_attackTells.Count > 0 && IsOffensiveAction(decision) &&
            !HasStatusEffectOfType(_enemy, StatusEffectType.Stun))
        {
            var tell = GetRandomTellForEnemy(_enemy);
            if (tell != null)
            {
                // Calculate what the damage WOULD be without defense
                var baseDamage = CalculateBaseDamageForTell(_enemy, target, decision);

                if (BeginAttackWithTell(_enemy, target, tell.RefName, baseDamage))
                {
                    // Committing to a telegraphed attack is an offensive action —
                    // it drops any defensive stance from the enemy's previous turn
                    // (mirrors the clearing in ExecuteDecision for direct attacks)
                    _enemy.IsDefending = false;
                    _enemy.IsAdjusting = false;

                    // Battle is now in AwaitingReaction state — return the tell event
                    return new CombatEvent
                    {
                        ActionType = BattleActionType.Attack,
                        ActorName = _enemy.DisplayName,
                        TargetName = target.DisplayName,
                        Damage = baseDamage,
                        Success = true,
                        IsDefending = true, // Signals awaiting reaction
                        Message = tell.TellText,
                        TurnNumber = _turnNumber,
                        IsAvatarTurn = false
                    };
                }
            }
        }

        // No tells available or tell failed — execute directly as before
        var action = ExecuteDecision(_enemy, target, decision);
        RecordAction(action);

        if (action.Success)
        {
            CheckBattleEnd();
        }
        else if (State == BattleState.EnemyTurn)
        {
            // A failed enemy action still yields the turn — the state machine used
            // to stay wedged in EnemyTurn forever when the AI's decision failed
            CombatLog.Add($"{_enemy.DisplayName} fumbles and loses the turn!");
            YieldFumbledEnemyTurn();
        }

        return action;
    }

    /// <summary>
    /// Ends a fumbled (blocked/failed) enemy turn: EndOfTurn effects still tick —
    /// a lost turn is still a turn — then the avatar is up unless the tick was lethal.
    /// </summary>
    private void YieldFumbledEnemyTurn()
    {
        ProcessEndOfTurnStatusEffects(_enemy);
        if (TryConcludeFromHealth())
            return;

        State = BattleState.AvatarTurn;
        _turnNumber++;
    }

    /// <summary>
    /// Select which party member the enemy will target.
    /// Currently uses simple logic: random alive party member, weighted toward avatar.
    /// </summary>
    private Combatant SelectEnemyTarget()
    {
        // Build list of alive targets
        var aliveTargets = new List<Combatant>();
        if (_avatar.IsAlive)
            aliveTargets.Add(_avatar);
        aliveTargets.AddRange(_companions.Where(c => c.IsAlive));

        if (aliveTargets.Count == 0)
            return _avatar;  // Shouldn't happen, but fallback

        if (aliveTargets.Count == 1)
            return aliveTargets[0];

        // Weight toward avatar (50% chance to target avatar, 50% split among companions)
        if (_avatar.IsAlive && _random.NextDouble() < 0.5)
            return _avatar;

        // Random from all alive targets
        return aliveTargets[_random.Next(aliveTargets.Count)];
    }

    /// <summary>
    /// Execute a companion's turn using AI.
    /// </summary>
    public CombatEvent ExecuteCompanionTurn()
    {
        if (State != BattleState.CompanionTurn || _currentCompanionIndex >= _companions.Count)
        {
            return new CombatEvent
            {
                Success = false,
                Message = "Not companion's turn"
            };
        }

        var companion = _companions[_currentCompanionIndex];

        // Skip if companion is dead
        if (!companion.IsAlive)
        {
            AdvanceCompanionTurn();
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack, // Placeholder — no action taken
                ActorName = companion.DisplayName,
                TargetName = companion.DisplayName,
                Success = true,
                Message = $"{companion.DisplayName} is defeated and cannot act",
                TurnNumber = _turnNumber,
                IsAvatarTurn = false,
                TargetHealthAfter = companion.Health,
                ActorEnergyAfter = companion.Stamina
            };
        }

        CombatLog.Add($"--- {companion.DisplayName}'s turn ---");

        // Process status effects at start of companion's turn
        ProcessStatusEffects(companion);

        // Check if companion died from DoT
        if (!companion.IsAlive)
        {
            AdvanceCompanionTurn();
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack, // Placeholder — no action taken
                ActorName = companion.DisplayName,
                TargetName = companion.DisplayName,
                Success = true,
                Message = $"{companion.DisplayName} succumbed to status effects!",
                TurnNumber = _turnNumber,
                IsAvatarTurn = false,
                TargetHealthAfter = companion.Health,
                ActorEnergyAfter = companion.Stamina
            };
        }

        CombatEvent action;
        if (_companionMind == null)
        {
            // Fallback: basic attack
            action = ExecuteAttack(companion, _enemy);
        }
        else
        {
            // AI decides (companions always target the enemy)
            var snapshot = CreateCompanionBattleSnapshot(companion);
            var decision = _companionMind.DecideTurn(snapshot);
            action = ExecuteDecision(companion, _enemy, decision);
        }

        RecordAction(action);

        if (action.Success)
        {
            CheckBattleEnd();
        }

        // Move to next companion or next phase
        if (State == BattleState.CompanionTurn)  // Not ended by CheckBattleEnd
        {
            // PHASE 6: Process EndOfTurn status effects for companion before advancing
            ProcessEndOfTurnStatusEffects(companion);
            AdvanceCompanionTurn();
        }

        return action;
    }

    /// <summary>
    /// Advance to next companion or to enemy turn.
    /// </summary>
    private void AdvanceCompanionTurn()
    {
        _currentCompanionIndex++;

        // Skip dead companions
        while (_currentCompanionIndex < _companions.Count && !_companions[_currentCompanionIndex].IsAlive)
        {
            _currentCompanionIndex++;
        }

        if (_currentCompanionIndex >= _companions.Count)
        {
            // All companions have acted, enemy's turn
            State = BattleState.EnemyTurn;
            CombatLog.Add($"--- {_enemy.DisplayName}'s turn ---");
        }
    }

    /// <summary>
    /// Create battle snapshot from a companion's perspective.
    /// </summary>
    private BattleView CreateCompanionBattleSnapshot(Combatant companion)
    {
        // Companion sees enemy but with hidden capabilities
        var observableEnemy = new Combatant
        {
            RefName = _enemy.RefName,
            DisplayName = _enemy.DisplayName,
            Health = _enemy.Health,
            Stamina = _enemy.Stamina,
            Strength = GetEffectiveStrength(_enemy),
            Defense = GetEffectiveDefense(_enemy),
            Speed = GetEffectiveSpeed(_enemy),
            Magic = GetEffectiveMagic(_enemy),
            AffinityRef = _enemy.AffinityRef,
            IsDefending = _enemy.IsDefending,
            Capabilities = null  // Hidden
        };

        return new BattleView
        {
            Self = companion,
            Opponent = observableEnemy,
            History = _actionHistory.ToList(),
            TurnNumber = _turnNumber
        };
    }

    /// <summary>
    /// Process avatar status effects at the start of their turn.
    /// Should be called by the UI/handler before presenting avatar options.
    /// </summary>
    public void ProcessAvatarTurnStart()
    {
        if (State != BattleState.AvatarTurn) return;

        ProcessStatusEffects(_avatar);

        // Check if avatar died from DoT
        if (!_avatar.IsAlive)
        {
            CheckBattleEnd();
        }
    }

    /// <summary>
    /// Execute an avatar decision (for AI-controlled avatars or UI-driven choices).
    /// </summary>
    public CombatEvent ExecuteAvatarDecision(CombatAction decision)
    {
        if (State != BattleState.AvatarTurn)
        {
            return new CombatEvent
            {
                Success = false,
                Message = "Not avatar's turn"
            };
        }

        // Check if avatar died from status effects before their action
        if (!_avatar.IsAlive)
        {
            CheckBattleEnd();
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack, // Placeholder — no action taken
                ActorName = _avatar.DisplayName,
                TargetName = _avatar.DisplayName,
                Success = false,
                Message = $"{_avatar.DisplayName} succumbed to status effects!",
                TurnNumber = _turnNumber,
                IsAvatarTurn = true,
                TargetHealthAfter = _avatar.Health,
                ActorEnergyAfter = _avatar.Stamina
            };
        }

        var action = ExecuteDecision(_avatar, _enemy, decision);
        RecordAction(action);

        // A failed action (stunned, failed flee roll, not enough energy, ...) still
        // consumes the avatar's turn: companions and the enemy respond either way
        // (BATTLE.md turn flow steps 3-4). Gating this on action.Success left the
        // state wedged in AvatarTurn, which skipped the enemy response entirely —
        // a failed flee was a consequence-free retry until the roll succeeded, and
        // stunning the AVATAR silently cost the ENEMY its turns. A successful flee
        // (State == Fled) bypasses this via the state check.
        if (State == BattleState.AvatarTurn)
        {
            CheckBattleEnd();
        }

        return action;
    }

    /// <summary>
    /// Get current battle snapshot for AI decision-making.
    /// Opponent's capabilities are hidden (set to null).
    /// </summary>
    public BattleView GetAvatarSnapshot()
    {
        return CreateBattleSnapshot(forEnemy: false);
    }

    /// <summary>
    /// Create battle snapshot for AI tactical decision-making.
    /// </summary>
    private BattleView CreateBattleSnapshot(bool forEnemy)
    {
        var self = forEnemy ? _enemy : _avatar;
        var opponent = forEnemy ? _avatar : _enemy;

        // Create observable opponent (hide their capabilities/inventory)
        // Stats shown are effective stats (base * stance multipliers)
        var observableOpponent = new Combatant
        {
            RefName = opponent.RefName,
            DisplayName = opponent.DisplayName,
            Health = opponent.Health,
            Stamina = opponent.Stamina,
            Strength = GetEffectiveStrength(opponent),
            Defense = GetEffectiveDefense(opponent),
            Speed = GetEffectiveSpeed(opponent),
            Magic = GetEffectiveMagic(opponent),
            AffinityRef = opponent.AffinityRef,
            IsDefending = opponent.IsDefending,
            Capabilities = null  // Hidden - you don't know their inventory!
        };

        return new BattleView
        {
            Self = self,
            Opponent = observableOpponent,
            History = _actionHistory.ToList(),  // Copy of history
            TurnNumber = _turnNumber
        };
    }

    private CombatEvent ExecuteDecision(Combatant actor, Combatant target, CombatAction decision)
    {
        CombatEvent result;

        // PHASE 3: Check for Stun - prevents ALL actions
        if (HasStatusEffectOfType(actor, StatusEffectType.Stun))
        {
            CombatLog.Add($"💫 {actor.DisplayName} is stunned and cannot act!");
            result = new CombatEvent
            {
                ActionType = BattleActionType.Attack, // Placeholder
                ActorName = actor.DisplayName,
                TargetName = target.DisplayName,
                Success = false,
                Message = $"{actor.DisplayName} is stunned and cannot act!"
            };
        }
        // PHASE 3: Check for Silence - prevents spell casting
        else if (decision.ActionType == ActionType.CastSpell && HasStatusEffectOfType(actor, StatusEffectType.Silence))
        {
            CombatLog.Add($"🔇 {actor.DisplayName} is silenced and cannot cast spells!");
            result = new CombatEvent
            {
                ActionType = BattleActionType.SpecialAttack,
                ActorName = actor.DisplayName,
                TargetName = target.DisplayName,
                Success = false,
                Message = $"{actor.DisplayName} is silenced and cannot cast spells!"
            };
        }
        // PHASE 3: Check for Root - prevents fleeing
        else if (decision.ActionType == ActionType.Flee && HasStatusEffectOfType(actor, StatusEffectType.Root))
        {
            CombatLog.Add($"🌿 {actor.DisplayName} is rooted and cannot flee!");
            result = new CombatEvent
            {
                ActionType = BattleActionType.Flee,
                ActorName = actor.DisplayName,
                TargetName = target.DisplayName,
                Success = false,
                Message = $"{actor.DisplayName} is rooted and cannot flee!"
            };
        }
        else
        {
            Equipment? weapon = null;
            Spell? spell = null;
            Consumable? consumable = null;

            if (decision.Parameter != null && _world != null)
            {
                switch (decision.ActionType)
                {
                    case ActionType.Attack:
                        weapon = _world.GetEquipmentByRefName(decision.Parameter);
                        break;

                    case ActionType.CastSpell:
                        spell = _world.GetSpellByRefName(decision.Parameter);
                        break;

                    case ActionType.UseConsumable:
                        consumable = _world.GetConsumableByRefName(decision.Parameter);
                        break;
                    case ActionType.AdjustLoadout:
                    case ActionType.ChangeLoadout:
                        break;
                }
            }

            // Clear defensive states when taking offensive actions (attack, spell, consumable, flee)
            // Defensive actions (Defend, AdjustLoadout, ChangeLoadout) set their own states
            if (decision.ActionType == ActionType.Attack ||
                decision.ActionType == ActionType.CastSpell ||
                decision.ActionType == ActionType.UseConsumable ||
                decision.ActionType == ActionType.Flee)
            {
                actor.IsDefending = false;
                actor.IsAdjusting = false;
            }

            result = decision.ActionType switch
            {
                ActionType.Attack => weapon == null ? ExecuteAttack(actor, target) : ExecuteWeaponAttack(actor, target, weapon),
                ActionType.CastSpell => ExecuteSpellAttack(actor, target, spell!),
                ActionType.UseConsumable => ExecuteUseConsumable(actor, target, consumable!),
                ActionType.Defend => ExecuteDefend(actor),
                ActionType.Flee => ExecuteFlee(actor),
                ActionType.AdjustLoadout => ExecuteAdjustLoadout(actor, decision.Parameter),
                ActionType.ChangeLoadout => ExecuteChangeLoadout(actor, decision.Parameter),
                _ => throw new NotImplementedException("Unknown Action")
            };
        }

        // Stamp the transaction-logging fields — persisted turn transactions carry these,
        // and replay/state reconstruction read them back. Failure events (stun/silence/
        // root above, "not enough energy" from the executors) MUST be stamped too: they
        // are persisted like any other turn, and an un-stamped event (empty Target,
        // TargetHealthAfter=0) used to zero a living combatant during reconstruction.
        result.TurnNumber = _turnNumber;
        result.DecisionType = decision.ActionType;
        result.ItemRefName ??= decision.Parameter;
        result.IsAvatarTurn = ReferenceEquals(actor, _avatar);
        result.TargetHealthAfter = target.Health;
        result.ActorEnergyAfter = actor.Stamina;

        return result;
    }

    private void RecordAction(CombatEvent action)
    {
        if (action.Success)
        {
            _actionHistory.Add(action);
        }
    }

    // ============================================================================
    // STAT MULTIPLIER HELPERS
    // ============================================================================

    /// <summary>
    /// Get effective Strength stat with archetype bias, stance multiplier, and status effects applied.
    /// </summary>
    private float GetEffectiveStrength(Combatant combatant)
    {
        var effectiveStrength = combatant.Strength;

        // Apply archetype bias (small ±10% adjustments)
        if (combatant.ArchetypeBias != null)
            effectiveStrength *= combatant.ArchetypeBias.Strength;

        // Apply stance multiplier
        if (_world != null && combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) && !string.IsNullOrEmpty(stanceRef))
        {
            var stance = _world.TryGetCombatStanceByRefName(stanceRef);
            if (stance?.Effects != null)
                effectiveStrength *= stance.Effects.Strength;
        }

        // Apply status effect modifiers
        effectiveStrength *= GetStatusEffectStatModifier(combatant, "Strength");

        effectiveStrength += GetEquipmentPassiveModifier(combatant, "Strength");

        return ApplyCombatStatModifier(combatant, "Strength", effectiveStrength);
    }

    /// <summary>
    /// Applies the additive buff/debuff delta from spells and consumables on top of
    /// the multiplier pipeline, floored at 10% of the multiplied value so debuffs
    /// weaken but never zero out a stat (mirrors the status-effect floor).
    /// </summary>
    private static float ApplyCombatStatModifier(Combatant combatant, string statName, float value)
    {
        if (!combatant.CombatStatModifiers.TryGetValue(statName, out var delta) || delta == 0f)
            return value;

        return Math.Max(value * 0.1f, value + delta);
    }

    /// <summary>
    /// Sum of the passive stat modifiers from currently equipped items, scaled by
    /// condition ("stat modifiers applied while equipped" per Equipment.xsd — worn
    /// armor's Defense finally does something). Computed live from CombatProfile so
    /// mid-battle loadout changes take effect immediately and nothing goes stale.
    /// </summary>
    private float GetEquipmentPassiveModifier(Combatant combatant, string statName)
    {
        if (_world == null || combatant.CombatProfile == null)
            return 0f;

        var total = 0f;
        foreach (var equipmentRef in combatant.CombatProfile.Values)
        {
            if (string.IsNullOrEmpty(equipmentRef))
                continue;

            // Non-equipment CombatProfile entries (e.g. the Stance slot) resolve to null
            var equipment = _world.TryGetEquipmentByRefName(equipmentRef);
            if (equipment?.Effects == null)
                continue;

            var value = statName switch
            {
                "Strength" => equipment.Effects.Strength,
                "Defense" => equipment.Effects.Defense,
                "Speed" => equipment.Effects.Speed,
                "Magic" => equipment.Effects.Magic,
                _ => 0f
            };
            if (value == 0f)
                continue;

            var condition = 1f;
            var entry = combatant.Capabilities?.Equipment?.FirstOrDefault(e => e.EquipmentRef == equipmentRef);
            if (entry != null)
                condition = entry.Condition;

            total += value * condition;
        }

        return total;
    }

    /// <summary>
    /// Get effective Defense stat with archetype bias, stance multiplier, and status effects applied.
    /// </summary>
    private float GetEffectiveDefense(Combatant combatant)
    {
        var effectiveDefense = combatant.Defense;

        // Apply archetype bias (small ±10% adjustments)
        if (combatant.ArchetypeBias != null)
            effectiveDefense *= combatant.ArchetypeBias.Defense;

        // Apply stance multiplier
        if (_world != null && combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) && !string.IsNullOrEmpty(stanceRef))
        {
            var stance = _world.TryGetCombatStanceByRefName(stanceRef);
            if (stance?.Effects != null)
                effectiveDefense *= stance.Effects.Defense;
        }

        // Apply status effect modifiers
        effectiveDefense *= GetStatusEffectStatModifier(combatant, "Defense");

        effectiveDefense += GetEquipmentPassiveModifier(combatant, "Defense");

        return ApplyCombatStatModifier(combatant, "Defense", effectiveDefense);
    }

    /// <summary>
    /// Get effective Speed stat with archetype bias, stance multiplier, and status effects applied.
    /// </summary>
    private float GetEffectiveSpeed(Combatant combatant)
    {
        var effectiveSpeed = combatant.Speed;

        // Apply archetype bias (small ±10% adjustments)
        if (combatant.ArchetypeBias != null)
            effectiveSpeed *= combatant.ArchetypeBias.Speed;

        // Apply stance multiplier
        if (_world != null && combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) && !string.IsNullOrEmpty(stanceRef))
        {
            var stance = _world.TryGetCombatStanceByRefName(stanceRef);
            if (stance?.Effects != null)
                effectiveSpeed *= stance.Effects.Speed;
        }

        // Apply status effect modifiers
        effectiveSpeed *= GetStatusEffectStatModifier(combatant, "Speed");

        effectiveSpeed += GetEquipmentPassiveModifier(combatant, "Speed");

        return ApplyCombatStatModifier(combatant, "Speed", effectiveSpeed);
    }

    /// <summary>
    /// Get effective Magic stat with archetype bias, stance multiplier, and status effects applied.
    /// </summary>
    private float GetEffectiveMagic(Combatant combatant)
    {
        var effectiveMagic = combatant.Magic;

        // Apply archetype bias (small ±10% adjustments)
        if (combatant.ArchetypeBias != null)
            effectiveMagic *= combatant.ArchetypeBias.Magic;

        // Apply stance multiplier
        if (_world != null && combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) && !string.IsNullOrEmpty(stanceRef))
        {
            var stance = _world.TryGetCombatStanceByRefName(stanceRef);
            if (stance?.Effects != null)
                effectiveMagic *= stance.Effects.Magic;
        }

        // Apply status effect modifiers
        effectiveMagic *= GetStatusEffectStatModifier(combatant, "Magic");

        effectiveMagic += GetEquipmentPassiveModifier(combatant, "Magic");

        return ApplyCombatStatModifier(combatant, "Magic", effectiveMagic);
    }

    // ============================================================================
    // COMBAT ACTIONS
    // ============================================================================

    private CombatEvent ExecuteAttack(Combatant attacker, Combatant defender)
    {
        // PHASE 3: Check accuracy (Blind effects reduce hit chance)
        var accuracy = GetAccuracyModifier(attacker);
        if (_random.NextDouble() > accuracy)
        {
            CombatLog.Add($"👁️ {attacker.DisplayName}'s attack misses due to reduced accuracy!");
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack,
                ActorName = attacker.DisplayName,
                TargetName = defender.DisplayName,
                Damage = 0,
                Success = true, // Action succeeded but missed
                Message = $"{attacker.DisplayName}'s attack misses!"
            };
        }

        // Calculate damage: Strength - (Defense / 2), with random variance
        // Apply stance multipliers to both attacker's strength and defender's defense
        var effectiveStrength = GetEffectiveStrength(attacker);
        var effectiveDefense = GetEffectiveDefense(defender);
        var baseDamage = effectiveStrength - effectiveDefense / 2f;
        var variance = _random.Next(80, 121) / 100f; // 80% to 120%
        var damage = Math.Max(0.01f, baseDamage * variance); // Minimum 1% damage, not 100%

        // Critical hit chance based on Speed (with stance multiplier).
        // Stats are normalized 0-1: full Speed = the 30% cap (the old /100 was
        // leftover 0-100-scale arithmetic that capped real crit chance at ~1%)
        var effectiveSpeed = GetEffectiveSpeed(attacker);
        var critChance = Math.Min(0.3f, effectiveSpeed * 0.3f);
        var isCritical = _random.NextDouble() < critChance;

        if (isCritical)
        {
            damage *= 1.5f;
            CombatLog.Add($"💥 CRITICAL HIT!");
        }

        // Apply defending bonus
        if (defender.IsDefending)
        {
            damage *= 0.5f;
            CombatLog.Add($"{defender.DisplayName}'s defense reduces incoming damage!");
        }
        else if (defender.IsAdjusting)
        {
            damage *= 0.85f;  // 15% reduction
            CombatLog.Add($"{defender.DisplayName}'s defensive positioning reduces damage!");
        }

        // PHASE 5: Apply Vulnerable status effect (increases damage taken)
        var vulnerabilityMultiplier = GetVulnerabilityMultiplier(defender);
        if (vulnerabilityMultiplier > 1.0f)
        {
            damage *= vulnerabilityMultiplier;
            CombatLog.Add($"💔 {defender.DisplayName} is vulnerable! ({vulnerabilityMultiplier:F1}x damage taken)");
        }

        defender.Health = Math.Max(0, defender.Health - damage);
        CombatLog.Add($"{attacker.DisplayName} attacks for {damage * 100:F1}% damage!");
        CombatLog.Add($"{defender.DisplayName} HP: {defender.HealthPercent:F1}%");

        return new CombatEvent
        {
            ActionType = BattleActionType.Attack,
            ActorName = attacker.DisplayName,
            TargetName = defender.DisplayName,
            Damage = damage,
            IsCritical = isCritical,
            Success = true,
            Message = $"{attacker.DisplayName} attacks!"
        };
    }

    private CombatEvent ExecuteWeaponAttack(Combatant attacker, Combatant defender, Equipment weapon)
    {
        if (_world == null)
        {
            CombatLog.Add($"Cannot use weapon attacks - world data not available!");
            return new CombatEvent
            {
                Success = false,
                Message = "World data required for weapon attacks"
            };
        }

        // VERIFY: Weapon must be equipped in a slot (MainHand, OffHand, or BothHands)
        var isEquipped = false;
        if (attacker.CombatProfile != null)
        {
            isEquipped = attacker.CombatProfile.TryGetValue(MainHandSlot, out var mainHandItem) && mainHandItem == weapon.RefName
                      || attacker.CombatProfile.TryGetValue(OffHandSlot, out var offHandItem) && offHandItem == weapon.RefName
                      || attacker.CombatProfile.TryGetValue(BothHandsSlot, out var bothHandsItem) && bothHandsItem == weapon.RefName;
        }

        if (!isEquipped)
        {
            CombatLog.Add($"{attacker.DisplayName} tried to attack with {weapon.DisplayName}, but it's not equipped!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Weapon '{weapon.DisplayName}' is not equipped in a hand slot"
            };
        }

        // VALIDATION: Check MinimumStats - weapon may have stat requirements
        if (weapon.MinimumStats != null)
        {
            var failedRequirement = CheckMinimumStats(attacker, weapon.MinimumStats, weapon.DisplayName);
            if (failedRequirement != null)
            {
                return failedRequirement;
            }
        }

        // PHASE 3: Check accuracy (Blind effects reduce hit chance)
        var accuracy = GetAccuracyModifier(attacker);
        if (_random.NextDouble() > accuracy)
        {
            CombatLog.Add($"👁️ {attacker.DisplayName}'s attack with {weapon.DisplayName} misses due to reduced accuracy!");
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack,
                ActorName = attacker.DisplayName,
                TargetName = defender.DisplayName,
                Damage = 0,
                Success = true, // Action succeeded but missed
                Message = $"{attacker.DisplayName}'s attack with {weapon.DisplayName} misses!"
            };
        }

        // Find weapon's condition from attacker's capabilities
        var weaponCondition = 1.0f;
        if (attacker.Capabilities?.Equipment != null)
        {
            var equipped = attacker.Capabilities.Equipment.FirstOrDefault(e => e.EquipmentRef == weapon.RefName);
            if (equipped != null)
                weaponCondition = equipped.Condition;
        }

        // Calculate base damage: Strength (with stance multiplier) scaled by weapon
        // multiplier, reduced by the defender's Defense (armor finally matters —
        // weapon attacks used to bypass Defense entirely)
        var effectiveStrength = GetEffectiveStrength(attacker);
        var weaponDefense = GetEffectiveDefense(defender);
        var baseDamage = Math.Max(0f, effectiveStrength * WEAPON_DAMAGE_MULTIPLIER - weaponDefense / 2f);

        // Apply affinity multiplier to base damage only
        // (effect damage already includes affinity from EffectApplier)
        var affinityMultiplier = EffectApplier.CalculateAffinityMultiplier(
            weapon.AffinityRef ?? attacker.AffinityRef,
            defender.AffinityRef,
            _world);

        baseDamage *= affinityMultiplier;

        if (affinityMultiplier > 1.0f)
        {
            CombatLog.Add($"Affinity advantage! ({affinityMultiplier:F1}x damage)");
        }
        else if (affinityMultiplier < 1.0f)
        {
            CombatLog.Add($"Affinity resistance! ({affinityMultiplier:F1}x damage)");
        }

        // Critical hit calculation - base chance from speed + weapon CriticalHitBonus
        // (normalized 0-1 stats: full Speed = the 30% base cap)
        var effectiveSpeed = GetEffectiveSpeed(attacker);
        var baseCritChance = Math.Min(0.3f, effectiveSpeed * 0.3f);
        var critChance = Math.Min(0.5f, baseCritChance + weapon.CriticalHitBonus); // Cap at 50%
        var isCritical = _random.NextDouble() < critChance;

        // Apply weapon effects using EffectApplier
        var effects = EffectApplier.ApplyEffects(
            weapon.Effects ?? new EffectAttributes(),
            weapon.AffinityRef,
            weaponCondition,
            attacker.AffinityRef,
            defender.AffinityRef,
            isOffensive: true,
            _world,
            weapon.DisplayName);

        // Sum up Health damage from effects (should be negative)
        var effectDamage = 0.0;
        foreach (var effect in effects)
        {
            if (effect.StatName == "Health" && !effect.AppliedToAttacker)
            {
                effectDamage += effect.Change;  // Already negative for offensive
            }
        }

        // Total damage = base + effect damage
        var totalDamage = Math.Max(BASE_DAMAGE_MINIMUM, (float)(baseDamage + Math.Abs(effectDamage)));

        // Apply critical hit multiplier
        if (isCritical)
        {
            totalDamage *= 1.5f;
            CombatLog.Add($"💥 CRITICAL HIT!");
        }

        // Apply defending bonus
        if (defender.IsDefending)
        {
            totalDamage *= 0.5f;
            CombatLog.Add($"{defender.DisplayName}'s defense reduces incoming damage!");
        }
        else if (defender.IsAdjusting)
        {
            totalDamage *= 0.85f;  // 15% reduction
            CombatLog.Add($"{defender.DisplayName}'s defensive positioning reduces damage!");
        }

        // PHASE 5: Apply Vulnerable status effect (increases damage taken)
        var vulnerabilityMultiplier = GetVulnerabilityMultiplier(defender);
        if (vulnerabilityMultiplier > 1.0f)
        {
            totalDamage *= vulnerabilityMultiplier;
            CombatLog.Add($"💔 {defender.DisplayName} is vulnerable! ({vulnerabilityMultiplier:F1}x damage taken)");
        }

        // Apply damage
        defender.Health = Math.Max(0, defender.Health - totalDamage);

        CombatLog.Add($"{attacker.DisplayName} attacks with {weapon.DisplayName} for {totalDamage * 100:F1}% damage!");
        CombatLog.Add($"{defender.DisplayName} HP: {defender.HealthPercent:F1}%");

        // Degrade weapon condition slightly
        if (attacker.Capabilities?.Equipment != null && weapon.DurabilityLoss > 0)
        {
            var equipped = attacker.Capabilities.Equipment.FirstOrDefault(e => e.EquipmentRef == weapon.RefName);
            if (equipped != null)
            {
                equipped.Condition = Math.Max(0f, equipped.Condition - weapon.DurabilityLoss);
                if (equipped.Condition < 0.3f)
                {
                    CombatLog.Add($"[!] {weapon.DisplayName} is badly damaged!");
                }
            }
        }

        // Apply status effect from weapon (if defined)
        string? appliedStatusEffect = null;
        if (!string.IsNullOrEmpty(weapon.StatusEffectRef) && weapon.StatusEffectChance > 0)
        {
            // Check if status effect should only apply on critical hits
            var shouldApply = !weapon.StatusEffectOnCritOnly || isCritical;
            if (shouldApply)
            {
                appliedStatusEffect = TryApplyStatusEffect(
                    weapon.StatusEffectRef,
                    weapon.StatusEffectChance,
                    defender,
                    _turnNumber,
                    weapon.DisplayName);
            }
        }

        return new CombatEvent
        {
            ActionType = BattleActionType.Attack,
            ActorName = attacker.DisplayName,
            TargetName = defender.DisplayName,
            Damage = totalDamage,
            IsCritical = isCritical,
            Success = true,
            Message = $"{attacker.DisplayName} attacks with {weapon.DisplayName}!",
            StatusEffectApplied = appliedStatusEffect
        };
    }

    private CombatEvent ExecuteSpellAttack(Combatant attacker, Combatant defender, Spell spell)
    {
        if (_world == null)
        {
            CombatLog.Add($"Cannot cast spells - world data not available!");
            return new CombatEvent
            {
                Success = false,
                Message = "World data required for spell attacks"
            };
        }

        // VALIDATION: Check RequiresEquipped - spell may require specific equipment category
        if (spell.RequiresEquippedSpecified && attacker.CombatProfile != null)
        {
            var requiredCategory = spell.RequiresEquipped;
            var hasRequiredEquipment = false;

            // Check all hand slots for the required equipment category
            foreach (var slot in new[] { MainHandSlot, OffHandSlot, BothHandsSlot })
            {
                if (attacker.CombatProfile.TryGetValue(slot, out var equippedRef) && !string.IsNullOrEmpty(equippedRef))
                {
                    var equipment = _world.TryGetEquipmentByRefName(equippedRef);
                    if (equipment != null && equipment.Category == requiredCategory)
                    {
                        hasRequiredEquipment = true;
                        break;
                    }
                }
            }

            if (!hasRequiredEquipment)
            {
                CombatLog.Add($"{attacker.DisplayName} cannot cast {spell.DisplayName} - requires {requiredCategory} equipped!");
                return new CombatEvent
                {
                    Success = false,
                    Message = $"Requires {requiredCategory} equipped to cast {spell.DisplayName}"
                };
            }
        }

        // VALIDATION: Check MinimumStats - spell may have stat requirements
        if (spell.MinimumStats != null)
        {
            var failedRequirement = CheckMinimumStats(attacker, spell.MinimumStats, spell.DisplayName);
            if (failedRequirement != null)
            {
                return failedRequirement;
            }
        }

        // Find spell's condition from attacker's capabilities
        var spellCondition = 1.0f;
        if (attacker.Capabilities?.Spells != null)
        {
            var known = attacker.Capabilities.Spells.FirstOrDefault(s => s.SpellRef == spell.RefName);
            if (known != null)
                spellCondition = known.Condition;
        }

        // Defensive spells (the XSD default UseType) heal/restore the caster instead of
        // attacking, so base damage and affinity matchups only apply to offensive spells.
        var isOffensive = spell.UseType == ItemUseType.Offensive;

        var baseDamage = 0.0f;
        if (isOffensive)
        {
            // Calculate base damage: Magic (with stance multiplier) scaled by spell
            // multiplier, reduced by a quarter of the defender's Defense (magic
            // partially bypasses armor, consistent with Defend being less effective
            // against spells than physical attacks)
            var effectiveMagic = GetEffectiveMagic(attacker);
            var spellDefense = GetEffectiveDefense(defender);
            baseDamage = Math.Max(0f, effectiveMagic * SPELL_DAMAGE_MULTIPLIER - spellDefense / 4f);

            // Apply affinity multiplier to base damage only
            // (effect damage already includes affinity from EffectApplier)
            var affinityMultiplier = EffectApplier.CalculateAffinityMultiplier(
                spell.AffinityRef ?? attacker.AffinityRef,
                defender.AffinityRef,
                _world);

            baseDamage *= affinityMultiplier;

            if (affinityMultiplier > 1.0f)
            {
                CombatLog.Add($"Affinity advantage! ({affinityMultiplier:F1}x damage)");
            }
            else if (affinityMultiplier < 1.0f)
            {
                CombatLog.Add($"Affinity resistance! ({affinityMultiplier:F1}x damage)");
            }
        }

        // Apply spell effects using EffectApplier
        // Use spell's UseType to determine if offensive (damage) or defensive (healing/buff)
        var effects = EffectApplier.ApplyEffects(
            spell.Effects ?? new EffectAttributes(),
            spell.AffinityRef,
            spellCondition,
            attacker.AffinityRef,
            defender.AffinityRef,
            isOffensive: isOffensive,
            _world,
            spell.DisplayName);

        // Route resource effects (Stamina and Mana share one energy pool). Stat
        // buff/debuff modifiers are applied AFTER the energy check below — applying
        // them here let a failed cast ("Not enough energy!") stack battle-long stat
        // modifiers for free, infinitely.
        var effectDamage = 0.0;      // negative Health on the defender (extra damage)
        var healing = 0.0;           // positive Health to the caster via defensive use
        var selfHealthDelta = 0.0;   // caster Health riding on the cast (self-heal or cost)
        var energyRestore = 0.0;     // positive Stamina/Mana back to the caster
        var enemyEnergyDrain = 0.0;  // negative Stamina/Mana on the defender
        var manaCost = 0.0;
        var staminaCost = 0.0;

        foreach (var effect in effects)
        {
            if (effect.StatName == "Health")
            {
                if (!effect.AppliedToAttacker)
                {
                    if (isOffensive)
                        effectDamage += effect.Change;  // negative: damage on the defender
                    else
                        healing += effect.Change;       // positive: healing (target = caster)
                }
                else
                {
                    selfHealthDelta += effect.Change;
                }
            }
            else if (effect.StatName == "Stamina" || effect.StatName == "Mana")
            {
                if (effect.AppliedToAttacker)
                {
                    if (effect.Change < 0)
                    {
                        if (effect.StatName == "Mana")
                            manaCost += Math.Abs(effect.Change);
                        else
                            staminaCost += Math.Abs(effect.Change);
                    }
                    else
                    {
                        energyRestore += effect.Change;
                    }
                }
                else if (effect.Change < 0)
                {
                    enemyEnergyDrain += effect.Change;
                }
                else
                {
                    // Defensive restores refill the caster's shared energy pool
                    energyRestore += effect.Change;
                }
            }
        }

        // Check if attacker has enough Energy (using Energy for both Mana and Stamina)
        var totalCost = manaCost + staminaCost;
        if (attacker.Stamina < totalCost)
        {
            CombatLog.Add($"{attacker.DisplayName} doesn't have enough energy to cast {spell.DisplayName}!");
            return new CombatEvent
            {
                Success = false,
                Message = "Not enough energy!"
            };
        }

        // Apply energy cost
        attacker.Stamina = Math.Max(0, attacker.Stamina - (float)totalCost);

        // Buff/debuff payloads: Strength/Defense/Speed/Magic effects become additive
        // combat modifiers. EffectApplier routes by side: on offensive items negative
        // values debuff the defender and positive values buff the caster; on
        // defensive items positive values buff the caster and negatives are costs.
        ApplyCombatStatEffects(effects, attacker, isOffensive ? defender : attacker, spell.DisplayName);

        var totalDamage = 0.0f;
        var totalHealing = 0.0f;
        if (isOffensive)
        {
            // Total damage = base + effect damage
            totalDamage = Math.Max(BASE_DAMAGE_MINIMUM, (float)(baseDamage + Math.Abs(effectDamage)));

            // Apply defending bonus (less effective against spells)
            if (defender.IsDefending)
            {
                totalDamage *= 0.7f;  // Spells only reduced to 70% instead of 50%
                CombatLog.Add($"{defender.DisplayName}'s defense partially reduces spell damage!");
            }
            else if (defender.IsAdjusting)
            {
                totalDamage *= 0.90f;  // 10% reduction against spells (less effective than physical defense)
                CombatLog.Add($"{defender.DisplayName}'s defensive positioning slightly reduces spell damage!");
            }

            // PHASE 5: Apply Vulnerable status effect (increases damage taken)
            var spellVulnerabilityMultiplier = GetVulnerabilityMultiplier(defender);
            if (spellVulnerabilityMultiplier > 1.0f)
            {
                totalDamage *= spellVulnerabilityMultiplier;
                CombatLog.Add($"💔 {defender.DisplayName} is vulnerable! ({spellVulnerabilityMultiplier:F1}x damage taken)");
            }

            // Apply damage
            defender.Health = Math.Max(0, defender.Health - totalDamage);

            // Self effects riding on the attack (e.g. Bloodlust heals/energizes the caster)
            if (selfHealthDelta != 0)
            {
                attacker.Health = Math.Clamp(attacker.Health + (float)selfHealthDelta, 0, Combatant.MAX_STAT);
            }
            if (energyRestore > 0)
            {
                attacker.Stamina = Math.Min(Combatant.MAX_STAT, attacker.Stamina + (float)energyRestore);
            }
            if (enemyEnergyDrain < 0)
            {
                defender.Stamina = Math.Max(0, defender.Stamina + (float)enemyEnergyDrain);
                CombatLog.Add($"{defender.DisplayName} loses {Math.Abs(enemyEnergyDrain) * 100:F0}% energy!");
            }

            CombatLog.Add($"{attacker.DisplayName} casts {spell.DisplayName} for {totalDamage * 100:F1}% damage!");
            if (totalCost > 0)
            {
                CombatLog.Add($"({totalCost * 100:F1}% energy used)");
            }
            CombatLog.Add($"{defender.DisplayName} HP: {defender.HealthPercent:F1}%");
        }
        else
        {
            // Defensive: heal/restore the caster (selfHealthDelta carries any
            // authored self-cost, e.g. blood-magic style trade-offs)
            totalHealing = (float)healing;
            var netHealthChange = totalHealing + (float)selfHealthDelta;
            if (netHealthChange != 0)
            {
                attacker.Health = Math.Clamp(attacker.Health + netHealthChange, 0, Combatant.MAX_STAT);
            }
            if (energyRestore > 0)
            {
                attacker.Stamina = Math.Min(Combatant.MAX_STAT, attacker.Stamina + (float)energyRestore);
            }

            CombatLog.Add(totalHealing > 0
                ? $"{attacker.DisplayName} casts {spell.DisplayName}, restoring {totalHealing * 100:F1}% health!"
                : $"{attacker.DisplayName} casts {spell.DisplayName}!");
            if (totalCost > 0)
            {
                CombatLog.Add($"({totalCost * 100:F1}% energy used)");
            }
            CombatLog.Add($"{attacker.DisplayName} HP: {attacker.HealthPercent:F1}%");
        }

        // Degrade spell condition slightly
        if (attacker.Capabilities?.Spells != null && spell.DurabilityLoss > 0)
        {
            var known = attacker.Capabilities.Spells.FirstOrDefault(s => s.SpellRef == spell.RefName);
            if (known != null)
            {
                known.Condition = Math.Max(0f, known.Condition - spell.DurabilityLoss);
                if (known.Condition < 0.3f)
                {
                    CombatLog.Add($"[!] {spell.DisplayName} knowledge is fading!");
                }
            }
        }

        // Apply status effect from spell (if defined)
        string? appliedStatusEffect = null;
        if (!string.IsNullOrEmpty(spell.StatusEffectRef))
        {
            var effectTarget = isOffensive ? defender : attacker;
            appliedStatusEffect = TryApplyStatusEffect(
                spell.StatusEffectRef,
                spell.StatusEffectChance,
                effectTarget,
                _turnNumber,
                spell.DisplayName);
        }

        // Handle spell cleansing status effects
        if (spell.CleansesStatusEffects)
        {
            var cleanseTarget = spell.CleanseTargetSelf ? attacker : defender;
            CleanseStatusEffects(cleanseTarget, spell.DisplayName);
        }

        return new CombatEvent
        {
            ActionType = BattleActionType.SpecialAttack,  // Using SpecialAttack type for spells
            ActorName = attacker.DisplayName,
            TargetName = isOffensive ? defender.DisplayName : attacker.DisplayName,
            Damage = totalDamage,
            Healing = totalHealing,
            IsCritical = false,
            Success = true,
            Message = $"{attacker.DisplayName} casts {spell.DisplayName}!",
            StatusEffectApplied = appliedStatusEffect
        };
    }

    /// <summary>
    /// Applies Strength/Defense/Speed/Magic effect results as additive combat
    /// modifiers (battle-scoped buffs/debuffs). Caster-side effects (negative,
    /// AppliedToAttacker) land on the actor; target-side effects land on the
    /// given effect target — the caster for defensive use, the defender for
    /// offensive (where EffectApplier already inverted the sign into a debuff).
    /// </summary>
    private void ApplyCombatStatEffects(EffectApplier.EffectResult[] effects, Combatant actor, Combatant effectTarget, string sourceName)
    {
        foreach (var effect in effects)
        {
            if (effect.StatName is not ("Strength" or "Defense" or "Speed" or "Magic"))
                continue;

            var recipient = effect.AppliedToAttacker ? actor : effectTarget;
            recipient.CombatStatModifiers.TryGetValue(effect.StatName, out var current);
            recipient.CombatStatModifiers[effect.StatName] = current + (float)effect.Change;

            var sign = effect.Change >= 0 ? "+" : "";
            CombatLog.Add($"{recipient.DisplayName}: {sign}{effect.Change * 100:F0}% {effect.StatName} ({sourceName})");
        }
    }

    private CombatEvent ExecuteUseConsumable(Combatant user, Combatant target, Consumable consumable)
    {
        if (_world == null)
        {
            CombatLog.Add($"Cannot use consumables - world data not available!");
            return new CombatEvent
            {
                Success = false,
                Message = "World data required for consumable use"
            };
        }

        // Check if user has this consumable
        if (user.Capabilities?.Consumables == null)
        {
            CombatLog.Add($"{user.DisplayName} has no consumables!");
            return new CombatEvent
            {
                Success = false,
                Message = "No consumables available"
            };
        }

        var entry = user.Capabilities.Consumables.FirstOrDefault(c => c.ConsumableRef == consumable.RefName);
        if (entry == null || entry.Quantity <= 0)
        {
            CombatLog.Add($"{user.DisplayName} doesn't have {consumable.DisplayName}!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Don't have {consumable.DisplayName}"
            };
        }

        // Determine if offensive (targets opponent) or defensive (targets self)
        var isOffensive = consumable.UseType == ItemUseType.Offensive;
        var effectTarget = isOffensive ? target : user;

        // Apply consumable effects using consumable's UseType
        var effects = EffectApplier.ApplyEffects(
            consumable.Effects ?? new EffectAttributes(),
            consumable.AffinityRef,
            1.0f,  // Consumables don't degrade
            user.AffinityRef,
            effectTarget.AffinityRef,
            isOffensive: isOffensive,
            _world,
            consumable.DisplayName);

        // Buff/debuff payloads become additive combat modifiers on the effect target
        ApplyCombatStatEffects(effects, user, effectTarget, consumable.DisplayName);

        // Apply resource effects. EffectApplier routes by side: on offensive items
        // negative values harm the target (thrown weapons, poisons) and positive
        // values benefit the user; on defensive items positives benefit the target
        // (the user in self-use flows) and negatives are costs to the user.
        var totalHealthChange = 0.0f;
        foreach (var effect in effects)
        {
            if (effect.StatName == "Health")
            {
                var change = (float)effect.Change;
                if (effect.AppliedToAttacker)
                {
                    user.Health = Math.Clamp(user.Health + change, 0, Combatant.MAX_STAT);
                }
                else
                {
                    effectTarget.Health = Math.Clamp(effectTarget.Health + change, 0, Combatant.MAX_STAT);
                    totalHealthChange += change;
                }
            }
            else if (effect.StatName == "Stamina" || effect.StatName == "Mana")
            {
                if (effect.AppliedToAttacker)
                {
                    // Cost (negative) or self-restore (positive) to the user
                    user.Stamina = Math.Clamp(user.Stamina + (float)effect.Change, 0, Combatant.MAX_STAT);
                }
                else
                {
                    // Restore (positive) or drain (negative) on the effect target
                    effectTarget.Stamina = Math.Clamp(effectTarget.Stamina + (float)effect.Change, 0, Combatant.MAX_STAT);
                }
            }
        }

        // Decrement quantity
        entry.Quantity--;

        // Log results
        CombatLog.Add($"{user.DisplayName} uses {consumable.DisplayName} on {effectTarget.DisplayName}!");

        if (isOffensive && totalHealthChange < 0)
        {
            CombatLog.Add($"Dealt {Math.Abs(totalHealthChange) * 100:F1}% damage!");
        }
        else if (!isOffensive && totalHealthChange > 0)
        {
            CombatLog.Add($"Restored {totalHealthChange * 100:F1}% health!");
        }

        CombatLog.Add($"{effectTarget.DisplayName} HP: {effectTarget.HealthPercent:F1}%");

        // PHASE 4: Apply status effect from consumable (if defined)
        string? appliedStatusEffect = null;
        if (!string.IsNullOrEmpty(consumable.StatusEffectRef))
        {
            // Status effect target follows the same logic as regular effects
            appliedStatusEffect = TryApplyStatusEffect(
                consumable.StatusEffectRef,
                consumable.StatusEffectChance,
                effectTarget,
                _turnNumber,
                consumable.DisplayName);
        }

        // PHASE 4: Handle consumable cleansing status effects
        if (consumable.CleansesStatusEffects)
        {
            var cleanseTarget = consumable.CleanseTargetSelf ? user : target;
            CleanseStatusEffects(cleanseTarget, consumable.DisplayName);
        }

        return new CombatEvent
        {
            ActionType = BattleActionType.UseItem,
            ActorName = user.DisplayName,
            TargetName = effectTarget.DisplayName,
            Damage = isOffensive ? Math.Abs(totalHealthChange) : 0,
            Healing = !isOffensive ? totalHealthChange : 0,
            Success = true,
            Message = $"{user.DisplayName} uses {consumable.DisplayName}!",
            StatusEffectApplied = appliedStatusEffect
        };
    }

    private CombatEvent ExecuteAdjustLoadout(Combatant actor, string? parameter)
    {
        // Quick tactical adjustment - single slot change with bigger defense bonus
        CombatLog.Add($"[Turn {_turnNumber}] {actor.DisplayName} adjusts loadout");

        if (string.IsNullOrWhiteSpace(parameter))
        {
            return new CombatEvent
            {
                ActionType = BattleActionType.AdjustLoadout,
                ActorName = actor.DisplayName,
                TargetName = actor.DisplayName,
                Success = false,
                Message = "No loadout adjustment specified"
            };
        }

        // Parse single change: "Slot:Value" (e.g., "MainHand:IronSword" or "Stance:Defensive" or "Affinity:Fire")
        var parts = parameter.Split(':');
        if (parts.Length != 2)
        {
            CombatLog.Add($"  → Invalid format: {parameter}");
            return new CombatEvent
            {
                ActionType = BattleActionType.AdjustLoadout,
                ActorName = actor.DisplayName,
                TargetName = actor.DisplayName,
                Success = false,
                Message = "Invalid adjustment format"
            };
        }

        var slot = parts[0].Trim();
        var value = parts[1].Trim();

        // Apply change to CombatProfile
        if (actor.CombatProfile == null)
            actor.CombatProfile = new Dictionary<string, string>();

        // Use hand slot validation for mutual exclusivity (BothHands <-> MainHand/OffHand)
        if (slot == MainHandSlot || slot == OffHandSlot || slot == BothHandsSlot)
        {
            if (!TryApplyHandSlotEquipment(actor, slot, value, out var errorMessage))
            {
                return new CombatEvent
                {
                    ActionType = BattleActionType.AdjustLoadout,
                    ActorName = actor.DisplayName,
                    TargetName = actor.DisplayName,
                    Success = false,
                    Message = errorMessage ?? "Failed to equip item"
                };
            }
        }
        else
        {
            actor.CombatProfile[slot] = value;
            CombatLog.Add($"  → {slot} set to {value}");
        }

        // Quick adjustment provides defensive benefits (staying guarded)
        actor.IsAdjusting = true;
        actor.IsDefending = false;  // Can't be both defending and adjusting
        CombatLog.Add($"  → Quick adjustment provides defensive positioning (15% damage reduction)!");

        const float healthRestore = .05f;
        ApplyBonusHealthRestore(actor, healthRestore);

        return new CombatEvent
        {
            ActionType = BattleActionType.AdjustLoadout,
            ActorName = actor.DisplayName,
            TargetName = actor.DisplayName,
            Success = true,
            Message = $"{actor.DisplayName} adjusts {slot}",
            IsDefending = true,
            Healing = healthRestore
        };
    }

    private CombatEvent ExecuteChangeLoadout(Combatant actor, string? parameter)
    {
        // Full loadout reconfiguration - taking time to reorganize
        CombatLog.Add($"[Turn {_turnNumber}] {actor.DisplayName} reconfigures loadout");

        if (string.IsNullOrWhiteSpace(parameter))
        {
            return new CombatEvent
            {
                ActionType = BattleActionType.ChangeLoadout,
                ActorName = actor.DisplayName,
                TargetName = actor.DisplayName,
                Success = false,
                Message = "No loadout changes specified"
            };
        }

        // Parse multiple changes: "Slot:Value,Slot:Value,Slot:Value"
        // Example: "MainHand:IronSword,Affinity:Fire,Stance:Defensive"
        var changes = parameter.Split(',');
        var appliedChanges = 0;

        if (actor.CombatProfile == null)
            actor.CombatProfile = new Dictionary<string, string>();

        foreach (var change in changes)
        {
            var parts = change.Trim().Split(':');
            if (parts.Length != 2)
            {
                CombatLog.Add($"  → Skipping invalid change: {change}");
                continue;
            }

            var slot = parts[0].Trim();
            var value = parts[1].Trim();

            // Handle special case: Affinity is stored in AffinityRef, not CombatProfile
            if (slot == "Affinity")
            {
                actor.AffinityRef = value;
                CombatLog.Add($"  → {slot} set to {value}");
                appliedChanges++;
            }
            // Use hand slot validation for mutual exclusivity (BothHands <-> MainHand/OffHand)
            else if (slot == MainHandSlot || slot == OffHandSlot || slot == BothHandsSlot)
            {
                if (TryApplyHandSlotEquipment(actor, slot, value, out _))
                {
                    appliedChanges++;
                }
                // If it fails, just skip this change (logged in helper method)
            }
            else
            {
                actor.CombatProfile[slot] = value;
                CombatLog.Add($"  → {slot} set to {value}");
                appliedChanges++;
            }
        }

        if (appliedChanges == 0)
        {
            return new CombatEvent
            {
                ActionType = BattleActionType.ChangeLoadout,
                ActorName = actor.DisplayName,
                TargetName = actor.DisplayName,
                Success = false,
                Message = "No valid changes applied"
            };
        }

        // Full reconfiguration provides defensive positioning
        actor.IsAdjusting = true;
        actor.IsDefending = false;
        CombatLog.Add($"  → Full reconfiguration provides defensive positioning (15% damage reduction)!");

        return new CombatEvent
        {
            ActionType = BattleActionType.ChangeLoadout,
            ActorName = actor.DisplayName,
            TargetName = actor.DisplayName,
            Success = true,
            Message = $"{actor.DisplayName} reconfigures loadout ({appliedChanges} changes)",
            IsDefending = true
        };
    }

    private void ApplyBonusHealthRestore(Combatant actor, float healthRestore)
    {
        actor.Health = Math.Min(Combatant.MAX_STAT, actor.Health + healthRestore);
        CombatLog.Add($"  → Losing turn restores {healthRestore * 100:F0}% health!");
    }

    private CombatEvent ExecuteDefend(Combatant combatant)
    {
        combatant.IsDefending = true;
        combatant.IsAdjusting = false;  // Can't be both defending and adjusting
        CombatLog.Add($"{combatant.DisplayName} braces for impact!");

        // Restore some energy when defending (10% of max)
        var energyRestore = 0.1f;
        combatant.Stamina = Math.Min(Combatant.MAX_STAT, combatant.Stamina + energyRestore);
        CombatLog.Add($"{combatant.DisplayName} recovers {energyRestore * 100:F0}% energy!");

        // PHASE 5: Apply OnDefend status effects from equipped items
        string? appliedStatusEffect = null;
        if (_world != null && combatant.CombatProfile != null)
        {
            foreach (var (slot, equipmentRef) in combatant.CombatProfile)
            {
                if (string.IsNullOrEmpty(equipmentRef)) continue;

                var equipment = _world.TryGetEquipmentByRefName(equipmentRef);
                if (equipment?.OnDefendStatusEffectRef != null)
                {
                    var result = TryApplyStatusEffect(
                        equipment.OnDefendStatusEffectRef,
                        equipment.OnDefendStatusEffectChance,
                        combatant,
                        _turnNumber,
                        equipment.DisplayName);

                    if (result != null)
                    {
                        appliedStatusEffect = result;
                        CombatLog.Add($"{equipment.DisplayName} triggers {result} while defending!");
                    }
                }
            }
        }

        return new CombatEvent
        {
            ActionType = BattleActionType.Defend,
            ActorName = combatant.DisplayName,
            TargetName = combatant.DisplayName,
            Healing = energyRestore,
            IsDefending = true,
            Success = true,
            Message = $"{combatant.DisplayName} defends!",
            StatusEffectApplied = appliedStatusEffect
        };
    }

    private CombatEvent ExecuteFlee(Combatant fleer)
    {
        // Base 50% + up to +45% from Speed (normalized 0-1; the old /200 was
        // leftover 0-100-scale arithmetic that gave at most +0.5%)
        var fleeChance = Math.Min(0.95, 0.5 + GetEffectiveSpeed(fleer) * 0.45f);

        if (_random.NextDouble() < fleeChance)
        {
            CombatLog.Add($"{fleer.DisplayName} successfully fled from battle!");
            CombatLog.Add($"💨 {_enemy.DisplayName} is now disengaged and won't immediately pursue.");
            State = BattleState.Fled;

            // Set traits: enemy gets Disengaged (won't re-aggro immediately) and Victorious
            return new CombatEvent
            {
                ActionType = BattleActionType.Flee,
                ActorName = fleer.DisplayName,
                TargetName = _enemy.DisplayName,
                Success = true,
                Message = "Fled successfully!",
                TraitToAssign = "Disengaged",
                TraitTargetCharacterRef = _enemy.RefName
            };
        }
        else
        {
            CombatLog.Add($"{fleer.DisplayName} failed to escape!");

            return new CombatEvent
            {
                ActionType = BattleActionType.Flee,
                ActorName = fleer.DisplayName,
                Success = false,
                Message = "Failed to flee!"
            };
        }
    }

    private void CheckBattleEnd()
    {
        if (TryConcludeFromHealth())
            return;

        // Switch turns: Avatar -> Companions -> Enemy -> Avatar
        if (State == BattleState.AvatarTurn)
        {
            // PHASE 6: Process EndOfTurn status effects for avatar before switching.
            // The tick can be lethal — checking health only BEFORE it left a 0-HP
            // corpse fighting on until the next action re-ran the checks.
            ProcessEndOfTurnStatusEffects(_avatar);
            if (TryConcludeFromHealth())
                return;

            // After avatar, companions go (if any alive)
            var aliveCompanions = _companions.Where(c => c.IsAlive).ToList();
            if (aliveCompanions.Count > 0)
            {
                State = BattleState.CompanionTurn;
                _currentCompanionIndex = 0;
                // Skip to first alive companion
                while (_currentCompanionIndex < _companions.Count && !_companions[_currentCompanionIndex].IsAlive)
                {
                    _currentCompanionIndex++;
                }
            }
            else
            {
                // No companions, enemy turn
                State = BattleState.EnemyTurn;
                CombatLog.Add($"--- {_enemy.DisplayName}'s turn ---");
            }
        }
        else if (State == BattleState.EnemyTurn)
        {
            // PHASE 6: Process EndOfTurn status effects for enemy before switching
            // (same lethality rule as the avatar's tick above)
            ProcessEndOfTurnStatusEffects(_enemy);
            if (TryConcludeFromHealth())
                return;

            State = BattleState.AvatarTurn;
            CombatLog.Add($"--- {_avatar.DisplayName}'s turn ---");
            _turnNumber++;
        }
        // CompanionTurn advancement is handled in AdvanceCompanionTurn()
    }

    /// <summary>
    /// Concludes the battle if either principal combatant is down.
    /// Returns true when the battle ended (State is Victory or Defeat).
    /// </summary>
    private bool TryConcludeFromHealth()
    {
        if (_enemy.Health <= 0)
        {
            State = BattleState.Victory;
            CombatLog.Add("=== VICTORY ===");
            CombatLog.Add($"{_enemy.DisplayName} has been defeated!");
            return true;
        }

        // Avatar down = defeat (companions flee without leader)
        if (_avatar.Health <= 0)
        {
            State = BattleState.Defeat;
            CombatLog.Add("=== DEFEAT ===");
            CombatLog.Add($"{_avatar.DisplayName} has been defeated...");
            if (_companions.Count > 0)
            {
                CombatLog.Add("Your companions flee without their leader!");
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the current avatar combatant (for UI binding).
    /// </summary>
    public Combatant GetAvatar() => _avatar;

    /// <summary>
    /// Get companion combatants (for UI binding).
    /// </summary>
    public IReadOnlyList<Combatant> GetCompanions() => _companions.AsReadOnly();

    /// <summary>
    /// Get the current enemy combatant (for UI binding).
    /// </summary>
    public Combatant GetEnemy() => _enemy;

    /// <summary>
    /// Get the world data (for equipment/spell/consumable lookups).
    /// </summary>
    public IWorld GetWorld() => _world;

    /// <summary>
    /// Get the current turn number.
    /// </summary>
    public int GetTurnNumber() => _turnNumber;

    /// <summary>
    /// Set the avatar's available affinities for battle.
    /// </summary>
    public void SetAvatarAffinities(List<string> affinityRefs)
    {
        AvatarAffinityRefs = affinityRefs ?? new List<string>();
    }

    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================

    /// <summary>
    /// Check if combatant meets minimum stat requirements for an item.
    /// Returns a failed CombatEvent if requirements not met, null if OK.
    /// </summary>
    private CombatEvent? CheckMinimumStats(Combatant combatant, EffectAttributes minimumStats, string itemName)
    {
        // Check each stat that has a minimum requirement (values > 0 are requirements)
        if (minimumStats.Strength > 0 && combatant.Strength < minimumStats.Strength)
        {
            CombatLog.Add($"{combatant.DisplayName} lacks the Strength to use {itemName}!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Requires {minimumStats.Strength * 100:F0}% Strength"
            };
        }

        if (minimumStats.Defense > 0 && combatant.Defense < minimumStats.Defense)
        {
            CombatLog.Add($"{combatant.DisplayName} lacks the Defense to use {itemName}!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Requires {minimumStats.Defense * 100:F0}% Defense"
            };
        }

        if (minimumStats.Speed > 0 && combatant.Speed < minimumStats.Speed)
        {
            CombatLog.Add($"{combatant.DisplayName} lacks the Speed to use {itemName}!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Requires {minimumStats.Speed * 100:F0}% Speed"
            };
        }

        if (minimumStats.Magic > 0 && combatant.Magic < minimumStats.Magic)
        {
            CombatLog.Add($"{combatant.DisplayName} lacks the Magic to use {itemName}!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Requires {minimumStats.Magic * 100:F0}% Magic"
            };
        }

        // Check energy (Mana/Stamina mapped to Energy in battle)
        var requiredEnergy = Math.Max(minimumStats.Mana, minimumStats.Stamina);
        if (requiredEnergy > 0 && combatant.Stamina < requiredEnergy)
        {
            CombatLog.Add($"{combatant.DisplayName} lacks the Energy to use {itemName}!");
            return new CombatEvent
            {
                Success = false,
                Message = $"Requires {requiredEnergy * 100:F0}% Energy"
            };
        }

        return null; // All requirements met
    }

    // ============================================================================
    // STATUS EFFECT HELPERS
    // ============================================================================

    /// <summary>
    /// Attempt to apply a status effect to a target with probability check.
    /// Returns the applied status effect RefName, or null if not applied.
    /// </summary>
    private string? TryApplyStatusEffect(string statusEffectRef, float chance, Combatant target, int currentTurn, string sourceName)
    {
        if (_world == null) return null;

        // Probability check
        if (_random.NextDouble() > chance)
        {
            return null; // Effect didn't trigger
        }

        // Look up the status effect definition
        var statusEffect = _world.TryGetStatusEffectByRefName(statusEffectRef);
        if (statusEffect == null)
        {
            // Status effect not found in catalog - silently ignore (not a hard failure)
            return null;
        }

        // Check if target already has this effect
        var existing = target.ActiveStatusEffects.FirstOrDefault(e => e.StatusEffectRef == statusEffectRef);
        if (existing != null)
        {
            // Already has effect - check stacking rules
            if (statusEffect.MaxStacks > 0 && existing.Stacks < statusEffect.MaxStacks)
            {
                existing.Stacks++;
                existing.RemainingTurns = statusEffect.DurationTurns; // Refresh duration
                CombatLog.Add($"🔥 {target.DisplayName}'s {statusEffect.DisplayName} intensifies! (x{existing.Stacks})");
            }
            else if (statusEffect.MaxStacks == 0)
            {
                // Refresh duration only
                existing.RemainingTurns = statusEffect.DurationTurns;
            }
            // If at max stacks, just refresh duration
            else
            {
                existing.RemainingTurns = statusEffect.DurationTurns;
            }
        }
        else
        {
            // Add new status effect
            target.ActiveStatusEffects.Add(new ActiveStatusEffect
            {
                StatusEffectRef = statusEffectRef,
                RemainingTurns = statusEffect.DurationTurns,
                Stacks = 1,
                AppliedOnTurn = currentTurn
            });
            CombatLog.Add($"✨ {target.DisplayName} is afflicted with {statusEffect.DisplayName} from {sourceName}!");
        }

        return statusEffectRef;
    }

    /// <summary>
    /// Remove all cleansable status effects from a target.
    /// </summary>
    private void CleanseStatusEffects(Combatant target, string sourceName)
    {
        if (_world == null) return;

        var cleansedCount = 0;
        for (int i = target.ActiveStatusEffects.Count - 1; i >= 0; i--)
        {
            var active = target.ActiveStatusEffects[i];
            var statusEffect = _world.TryGetStatusEffectByRefName(active.StatusEffectRef);

            // Only cleanse if the effect is marked as cleansable (or if we can't find definition, allow cleanse)
            if (statusEffect == null || statusEffect.Cleansable)
            {
                target.ActiveStatusEffects.RemoveAt(i);
                cleansedCount++;
            }
        }

        if (cleansedCount > 0)
        {
            CombatLog.Add($"✨ {sourceName} cleanses {cleansedCount} status effect(s) from {target.DisplayName}!");
        }
    }

    /// <summary>
    /// Process status effects at the start of a combatant's turn.
    /// Applies damage-over-time, stat modifiers, and decrements durations.
    /// </summary>
    public void ProcessStatusEffects(Combatant combatant) => ProcessStatusEffectsWithTiming(combatant, ApplicationMethod.StartOfTurn);

    /// <summary>
    /// PHASE 6: Process status effects at end of a combatant's turn.
    /// Only processes effects with EndOfTurn application method.
    /// </summary>
    public void ProcessEndOfTurnStatusEffects(Combatant combatant) => ProcessStatusEffectsWithTiming(combatant, ApplicationMethod.EndOfTurn);

    /// <summary>
    /// PHASE 6: Process status effects with specific timing.
    /// Only applies periodic effects (DoT) for status effects matching the timing.
    /// Duration decrement happens at StartOfTurn for all effects.
    /// </summary>
    private void ProcessStatusEffectsWithTiming(Combatant combatant, ApplicationMethod timing)
    {
        if (_world == null) return;

        for (int i = combatant.ActiveStatusEffects.Count - 1; i >= 0; i--)
        {
            var active = combatant.ActiveStatusEffects[i];
            var statusEffect = _world.TryGetStatusEffectByRefName(active.StatusEffectRef);

            if (statusEffect == null)
            {
                // Invalid status effect reference - remove it
                combatant.ActiveStatusEffects.RemoveAt(i);
                continue;
            }

            // Expire effects whose duration was fully consumed on a PREVIOUS turn.
            // Removal is deliberately one pass behind the decrement below: the
            // effect must stay active through the whole turn its final charge is
            // spent on, because the action-block checks (Stun/Silence/Root in
            // ExecuteDecision) and stat reads run AFTER this tick. Decrementing
            // and removing in the same pass made DurationTurns=N cover only N-1
            // of the victim's actions — the shipped DurationTurns="1" Stun
            // expired here before the stun check ever saw it, a complete no-op.
            // DurationTurns=0 is authored as "permanent until cleansed"
            // (StatusEffects.xsd) — those effects never tick down or expire here.
            if (timing == ApplicationMethod.StartOfTurn &&
                statusEffect.DurationTurns > 0 &&
                active.RemainingTurns <= 0)
            {
                combatant.ActiveStatusEffects.RemoveAt(i);
                CombatLog.Add($"✨ {statusEffect.DisplayName} wears off from {combatant.DisplayName}");
                continue;
            }

            // PHASE 6: Only apply periodic effects (DoT) if timing matches
            if (statusEffect.ApplicationMethod == timing && statusEffect.DamagePerTurn != 0)
            {
                var dotDamage = (statusEffect.DamagePerTurn / 100f) * active.Stacks;
                combatant.Health = Math.Clamp(combatant.Health - dotDamage, 0, Combatant.MAX_STAT);

                if (dotDamage > 0)
                {
                    CombatLog.Add($"🔥 {combatant.DisplayName} takes {dotDamage * 100:F1}% damage from {statusEffect.DisplayName}!");
                }
                else
                {
                    CombatLog.Add($"💚 {combatant.DisplayName} heals {Math.Abs(dotDamage) * 100:F1}% from {statusEffect.DisplayName}!");
                }
            }

            // Duration decrement only happens at StartOfTurn (to avoid double-counting
            // across the StartOfTurn/EndOfTurn passes). Reaching 0 here does NOT
            // remove the effect yet — it stays active for the remainder of this turn
            // (so the turn it was spent on still counts) and is removed at the top
            // of the victim's next StartOfTurn pass above.
            if (timing == ApplicationMethod.StartOfTurn && statusEffect.DurationTurns > 0)
            {
                active.RemainingTurns--;
            }
        }
    }

    /// <summary>
    /// Get combined stat modifier from all active status effects.
    /// Returns a multiplier (1.0 = no change, 0.8 = 20% reduction, 1.2 = 20% increase).
    /// </summary>
    public float GetStatusEffectStatModifier(Combatant combatant, string statName)
    {
        if (_world == null) return 1.0f;

        var modifier = 1.0f;
        foreach (var active in combatant.ActiveStatusEffects)
        {
            var statusEffect = _world.TryGetStatusEffectByRefName(active.StatusEffectRef);
            if (statusEffect == null) continue;

            // Get the appropriate modifier based on stat name
            var effectModifier = statName switch
            {
                "Strength" => statusEffect.StrengthModifier,
                "Defense" => statusEffect.DefenseModifier,
                "Speed" => statusEffect.SpeedModifier,
                "Magic" => statusEffect.MagicModifier,
                _ => 0f
            };

            // Apply modifier scaled by stacks (additive per stack)
            modifier += effectModifier * active.Stacks;
        }

        return Math.Max(0.1f, modifier); // Minimum 10% of stat
    }

    /// <summary>
    /// Check if a combatant has an active status effect of the specified type.
    /// </summary>
    public bool HasStatusEffectOfType(Combatant combatant, StatusEffectType type)
    {
        if (_world == null) return false;

        foreach (var active in combatant.ActiveStatusEffects)
        {
            var statusEffect = _world.TryGetStatusEffectByRefName(active.StatusEffectRef);
            if (statusEffect != null && statusEffect.Type == type)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get combined accuracy modifier from all active status effects (for Blind effects).
    /// Returns a multiplier (1.0 = normal, 0.5 = 50% accuracy, etc.).
    /// </summary>
    public float GetAccuracyModifier(Combatant combatant)
    {
        if (_world == null) return 1.0f;

        var modifier = 1.0f;
        foreach (var active in combatant.ActiveStatusEffects)
        {
            var statusEffect = _world.TryGetStatusEffectByRefName(active.StatusEffectRef);
            if (statusEffect == null) continue;

            // Apply accuracy modifier (typically negative for Blind effects)
            modifier += statusEffect.AccuracyModifier * active.Stacks;
        }

        return Math.Max(0.1f, modifier); // Minimum 10% accuracy
    }

    /// <summary>
    /// Get damage taken multiplier based on Vulnerable status effects.
    /// Returns a multiplier (1.0 = normal, 1.5 = 50% more damage taken, etc.).
    /// </summary>
    public float GetVulnerabilityMultiplier(Combatant defender)
    {
        if (_world == null) return 1.0f;

        var multiplier = 1.0f;
        foreach (var active in defender.ActiveStatusEffects)
        {
            var statusEffect = _world.TryGetStatusEffectByRefName(active.StatusEffectRef);
            if (statusEffect == null) continue;

            // Vulnerable effects increase damage taken
            // Using DefenseModifier as a negative value increases damage (e.g., -0.25 = +25% damage taken)
            if (statusEffect.Type == StatusEffectType.Vulnerable)
            {
                // DefenseModifier of -0.25 means 25% more damage taken (1.0 - (-0.25) = 1.25)
                multiplier -= statusEffect.DefenseModifier * active.Stacks;
            }
        }

        return Math.Max(0.5f, multiplier); // Cap at minimum 50% damage (can't be immune)
    }

    /// <summary>
    /// Check if equipment is a two-handed weapon.
    /// Two-handed weapons have SlotRef="BothHands" and block MainHand/OffHand when equipped.
    /// </summary>
    public bool IsTwoHandedWeapon(Equipment? equipment)
    {
        if (equipment == null) return false;
        return equipment.SlotRef == BothHandsSlot;
    }

    /// <summary>
    /// Validates and applies equipment to a hand slot, handling BothHands/MainHand/OffHand mutual exclusivity.
    /// - Equipping to BothHands: clears MainHand and OffHand
    /// - Equipping to MainHand or OffHand: clears BothHands
    /// Returns true if the equipment was successfully applied, false if validation failed.
    /// </summary>
    private bool TryApplyHandSlotEquipment(Combatant actor, string slot, string equipmentRef, out string? errorMessage)
    {
        errorMessage = null;

        // Only process hand slots for mutual exclusivity
        if (slot != MainHandSlot && slot != OffHandSlot && slot != BothHandsSlot)
        {
            actor.CombatProfile[slot] = equipmentRef;
            return true;
        }

        var equipment = _world?.TryGetEquipmentByRefName(equipmentRef);

        // Handle BothHands slot - clears MainHand and OffHand
        if (slot == BothHandsSlot)
        {
            if (actor.CombatProfile.Remove(MainHandSlot))
                CombatLog.Add($"  → {MainHandSlot} cleared for two-handed weapon");
            if (actor.CombatProfile.Remove(OffHandSlot))
                CombatLog.Add($"  → {OffHandSlot} cleared for two-handed weapon");

            actor.CombatProfile[BothHandsSlot] = equipmentRef;
            CombatLog.Add($"  → Two-handed weapon {equipment?.DisplayName ?? equipmentRef} equipped");
            return true;
        }

        // Handle MainHand or OffHand slot - clears BothHands if occupied
        if (actor.CombatProfile.TryGetValue(BothHandsSlot, out var bothHandsRef) && !string.IsNullOrEmpty(bothHandsRef))
        {
            var twoHandedEquip = _world?.TryGetEquipmentByRefName(bothHandsRef);
            actor.CombatProfile.Remove(BothHandsSlot);
            CombatLog.Add($"  → {twoHandedEquip?.DisplayName ?? bothHandsRef} unequipped to free hands");
        }

        // Normal one-handed equip
        actor.CombatProfile[slot] = equipmentRef;
        CombatLog.Add($"  → {slot} set to {equipment?.DisplayName ?? equipmentRef}");
        return true;
    }

    /// <summary>
    /// Whether the decision is an OFFENSIVE action that should trigger tells.
    /// Any CastSpell used to qualify, which telegraphed enemy heals/buffs as
    /// attacks and resolved them as phantom damage on the player; defensive
    /// casts (the XSD default UseType) must execute directly instead.
    /// </summary>
    private bool IsOffensiveAction(CombatAction decision)
    {
        if (decision.ActionType == ActionType.Attack)
            return true;

        if (decision.ActionType != ActionType.CastSpell)
            return false;

        var spell = decision.Parameter != null ? _world?.TryGetSpellByRefName(decision.Parameter) : null;
        return spell?.UseType == ItemUseType.Offensive;
    }

    /// <summary>
    /// Calculate the base damage an enemy attack WOULD deal without any defense modifiers.
    /// Used by the tell system so the avatar knows the stakes before choosing a reaction.
    /// </summary>
    private float CalculateBaseDamageForTell(Combatant attacker, Combatant target, CombatAction decision)
    {
        float baseDamage;
        string? affinityRef = attacker.AffinityRef;

        if (!string.IsNullOrEmpty(decision.Parameter) && _world != null)
        {
            // Use spell formula if the AI chose a spell (mirrors ExecuteSpellAttack
            // including the Defense term, so the telegraphed stakes match reality)
            if (decision.ActionType == ActionType.CastSpell)
            {
                var effectiveMagic = GetEffectiveMagic(attacker);
                baseDamage = Math.Max(0f, effectiveMagic * SPELL_DAMAGE_MULTIPLIER - GetEffectiveDefense(target) / 4f);
                var spell = _world.GetSpellByRefName(decision.Parameter);
                affinityRef = spell?.AffinityRef ?? attacker.AffinityRef;
            }
            else
            {
                // Use weapon attack formula if the AI chose a weapon (mirrors
                // ExecuteWeaponAttack including the Defense term)
                var weapon = _world.TryGetEquipmentByRefName(decision.Parameter);
                if (weapon != null)
                {
                    var effectiveStrength = GetEffectiveStrength(attacker);
                    baseDamage = Math.Max(0f, effectiveStrength * WEAPON_DAMAGE_MULTIPLIER - GetEffectiveDefense(target) / 2f);
                    affinityRef = weapon.AffinityRef ?? attacker.AffinityRef;
                }
                else
                {
                    // Fallback: basic attack formula
                    var strength = GetEffectiveStrength(attacker);
                    var defense = GetEffectiveDefense(target);
                    baseDamage = Math.Max(0.01f, strength - defense / 2f);
                }
            }
        }
        else
        {
            // Fallback: basic attack formula
            var strength = GetEffectiveStrength(attacker);
            var defense = GetEffectiveDefense(target);
            baseDamage = Math.Max(0.01f, strength - defense / 2f);
        }

        // Apply affinity so the preview matches actual damage
        if (_world != null)
        {
            baseDamage *= EffectApplier.CalculateAffinityMultiplier(
                affinityRef, target.AffinityRef, _world);
        }
        return baseDamage;
    }

    #region Combat Reaction System (Expedition 33-inspired)

    /// <summary>
    /// Register an attack tell for use in this battle.
    /// Typically loaded from enemy/character definitions.
    /// </summary>
    public void RegisterAttackTell(AttackTell tell)
    {
        _attackTells[tell.RefName] = tell;
    }

    /// <summary>
    /// Register all attack tells from world data.
    /// Call after construction but before StartBattle to enable the reaction system.
    /// </summary>
    public void RegisterTellsFromWorld(IWorld world)
    {
        if (world?.Gameplay?.AttackTells == null)
            return;

        foreach (var tell in world.Gameplay.AttackTells)
        {
            if (!string.IsNullOrEmpty(tell.RefName))
            {
                RegisterAttackTell(CombatReactionMapper.FromDomain(tell));
            }
        }
    }

    /// <summary>
    /// Begin an attack with a telegraph, entering the reaction phase.
    /// Call this instead of directly executing an attack to enable avatar reactions.
    /// </summary>
    /// <param name="attacker">The attacking combatant</param>
    /// <param name="target">The target of the attack</param>
    /// <param name="tellRefName">Reference name of the attack tell to use</param>
    /// <param name="baseDamage">The base damage before reaction modifiers</param>
    /// <returns>True if reaction phase started, false if tell not found or invalid state</returns>
    public bool BeginAttackWithTell(Combatant attacker, Combatant target, string tellRefName, float baseDamage)
    {
        if (!_attackTells.TryGetValue(tellRefName, out var tell))
        {
            CombatLog.Add($"Warning: Attack tell '{tellRefName}' not found, executing without reaction phase.");
            return false;
        }

        PendingAttack = new PendingAttack
        {
            Attacker = attacker,
            Target = target,
            Tell = tell,
            BaseDamage = baseDamage,
            TellShownAt = DateTime.UtcNow
        };

        State = BattleState.AwaitingReaction;
        CombatLog.Add($"{tell.TellText}");
        CombatLog.Add($"   [DODGE] [BLOCK] [PARRY] [BRACE] - {tell.ReactionWindowMs / 1000.0:F1}s to react!");

        return true;
    }

    /// <summary>
    /// Resolve the pending attack with the avatar's chosen defense reaction.
    /// </summary>
    /// <param name="reaction">The defense reaction chosen by the player</param>
    /// <returns>The result of the reaction, or null if no pending attack</returns>
    public ReactionResult? ResolveReaction(AvatarDefenseType reaction)
    {
        if (State != BattleState.AwaitingReaction || PendingAttack == null)
        {
            return null;
        }

        var pending = PendingAttack;
        var timedOut = pending.IsExpired;

        // If timed out, force None reaction
        if (timedOut)
        {
            reaction = AvatarDefenseType.None;
            CombatLog.Add("Time's up!");
        }

        var outcome = pending.Tell.GetOutcome(reaction);
        var finalDamage = pending.BaseDamage * outcome.DamageMultiplier;

        // Apply damage
        pending.Target.Health = Math.Max(0, pending.Target.Health - finalDamage);

        // Build narrative
        var narrativeText = outcome.ResponseText;
        if (string.IsNullOrEmpty(narrativeText))
        {
            narrativeText = reaction switch
            {
                AvatarDefenseType.Dodge => finalDamage == 0 ? "You evade the attack!" : $"You dodge but take {finalDamage * 100:F1}% damage.",
                AvatarDefenseType.Block => $"You block, taking {finalDamage * 100:F1}% damage.",
                AvatarDefenseType.Parry => outcome.EnablesCounter ? "You parry and prepare to counter!" : $"You deflect, taking {finalDamage * 100:F1}% damage.",
                AvatarDefenseType.Brace => $"You brace for impact, taking {finalDamage * 100:F1}% damage.",
                _ => $"You take {finalDamage * 100:F1}% damage!"
            };
        }

        CombatLog.Add($"{narrativeText}");

        // Handle counter-attack
        float? counterDamage = null;
        if (outcome.EnablesCounter && pending.Target.IsAlive)
        {
            counterDamage = pending.BaseDamage * outcome.CounterMultiplier;
            pending.Attacker.Health = Math.Max(0, pending.Attacker.Health - counterDamage.Value);
            CombatLog.Add($"Counter-attack hits {pending.Attacker.DisplayName} for {counterDamage.Value * 100:F1}% damage!");
        }

        // Apply defense effects (e.g., stamina recovery from skilled defense)
        float staminaGained = 0f;
        if (outcome.Effects != null && outcome.Effects.Stamina > 0)
        {
            var staminaGain = outcome.Effects.Stamina;
            var previousEnergy = pending.Target.Stamina;
            pending.Target.Stamina = Math.Min(Combatant.MAX_STAT, pending.Target.Stamina + staminaGain);
            staminaGained = pending.Target.Stamina - previousEnergy;

            if (staminaGained > 0)
            {
                CombatLog.Add($"✨ Skilled defense! (+{staminaGained * 100:F0}% stamina)");
            }
            else
            {
                CombatLog.Add($"✨ Skilled defense! (stamina already full)");
            }
        }

        var result = new ReactionResult
        {
            ChosenReaction = reaction,
            Outcome = outcome,
            FinalDamage = finalDamage,
            NarrativeText = narrativeText,
            CounterDamage = counterDamage,
            EffectsApplied = outcome.Effects,
            StaminaGained = staminaGained,
            WasOptimal = reaction == pending.Tell.OptimalDefense,
            WasSecondary = reaction == pending.Tell.SecondaryDefense,
            TimedOut = timedOut
        };

        // Record in action history
        _actionHistory.Add(new CombatEvent
        {
            ActionType = BattleActionType.Attack,
            ActorName = pending.Attacker.DisplayName,
            TargetName = pending.Target.DisplayName,
            Damage = finalDamage,
            Success = true,
            Message = narrativeText
        });

        // Clear pending attack and check battle end
        PendingAttack = null;
        CheckBattleEnd();

        // If battle didn't end, move to appropriate next state
        if (State == BattleState.AwaitingReaction)
        {
            // Move to avatar turn after enemy attack resolves
            State = BattleState.AvatarTurn;
            _turnNumber++;
        }

        return result;
    }

    /// <summary>
    /// Check if the reaction window has expired and auto-resolve if so.
    /// Call this periodically during the reaction phase.
    /// </summary>
    /// <returns>The result if auto-resolved due to timeout, null otherwise</returns>
    public ReactionResult? CheckReactionTimeout()
    {
        if (State != BattleState.AwaitingReaction || PendingAttack == null)
            return null;

        if (PendingAttack.IsExpired)
        {
            return ResolveReaction(AvatarDefenseType.None);
        }

        return null;
    }

    /// <summary>
    /// Get a random attack tell for an enemy based on their equipped weapon.
    /// Filters tells by weapon category to ensure thematic consistency.
    /// </summary>
    public AttackTell? GetRandomTellForEnemy(Combatant enemy)
    {
        if (_attackTells.Count == 0)
            return null;

        // Determine enemy's weapon category from their equipped weapon
        string? weaponCategory = GetEquippedWeaponCategory(enemy);

        // Filter tells by weapon category
        var compatibleTells = _attackTells.Values
            .Where(t => t.IsCompatibleWithWeapon(weaponCategory))
            .ToList();

        // If no compatible tells found, fall back to universal tells only
        if (compatibleTells.Count == 0)
        {
            compatibleTells = _attackTells.Values
                .Where(t => string.IsNullOrWhiteSpace(t.WeaponCategories))
                .ToList();
        }

        // If still no tells, return any tell as last resort
        if (compatibleTells.Count == 0)
        {
            var allTells = _attackTells.Values.ToList();
            return allTells[_random.Next(allTells.Count)];
        }

        return compatibleTells[_random.Next(compatibleTells.Count)];
    }

    /// <summary>
    /// Get the weapon category for a combatant's equipped weapon.
    /// Checks hand slots: BothHands first (two-handed), then MainHand, OffHand.
    /// </summary>
    private string? GetEquippedWeaponCategory(Combatant combatant)
    {
        if (_world == null || combatant.CombatProfile == null)
            return null;

        // Check hand slots in priority order - BothHands first for two-handed weapons
        var weaponSlots = new[] { BothHandsSlot, MainHandSlot, OffHandSlot };

        foreach (var slot in weaponSlots)
        {
            if (combatant.CombatProfile.TryGetValue(slot, out var equipmentRef) &&
                !string.IsNullOrEmpty(equipmentRef))
            {
                var equipment = _world.TryGetEquipmentByRefName(equipmentRef);
                if (equipment != null)
                {
                    return equipment.Category.ToString();
                }
            }
        }

        // No weapon equipped - treat as Unarmed
        return "Unarmed";
    }

    #endregion
}
