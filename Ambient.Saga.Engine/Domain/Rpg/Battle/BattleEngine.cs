using System.Linq;
using Ambient.Domain;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Current state of the battle.
/// </summary>
public enum BattleState
{
    NotStarted,
    PlayerTurn,
    CompanionTurn,
    EnemyTurn,
    /// <summary>
    /// Waiting for player to choose a defensive reaction.
    /// Enemy has telegraphed their attack; player has a time window to respond.
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

    private readonly Combatant _player;
    private readonly List<Combatant> _companions;  // Party members (excluding player)
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
    public List<string> PlayerAffinityRefs { get; private set; } = new();
    public IReadOnlyList<CombatEvent> ActionHistory => _actionHistory.AsReadOnly();

    /// <summary>
    /// The pending attack awaiting player reaction (only valid when State == AwaitingReaction).
    /// </summary>
    public PendingAttack? PendingAttack { get; private set; }

    /// <summary>
    /// Attack tells available for this battle (loaded from enemy/character data).
    /// </summary>
    private readonly Dictionary<string, AttackTell> _attackTells = new();

    /// <summary>
    /// All party members (player + companions) for UI display.
    /// </summary>
    public IReadOnlyList<Combatant> Party => new[] { _player }.Concat(_companions).ToList().AsReadOnly();

    /// <summary>
    /// Current companion taking their turn (null if not companion turn).
    /// </summary>
    public Combatant? CurrentCompanion => State == BattleState.CompanionTurn && _currentCompanionIndex < _companions.Count
        ? _companions[_currentCompanionIndex]
        : null;

    /// <summary>
    /// Create a new battle engine.
    /// </summary>
    /// <param name="player">The player combatant</param>
    /// <param name="enemy">The enemy combatant</param>
    /// <param name="enemyMind">AI brain for enemy tactical decisions (optional for player-vs-player)</param>
    /// <param name="world">World data for affinity matchups and item lookups (required for advanced combat)</param>
    /// <param name="randomSeed">Optional seed for deterministic behavior in tests</param>
    /// <param name="companions">Optional party companions who fight alongside the player</param>
    /// <param name="companionMind">AI brain for companion tactical decisions (uses enemyMind if not provided)</param>
    public BattleEngine(Combatant player, Combatant enemy, ICombatAI? enemyMind = null, IWorld world = null, int? randomSeed = null,
        List<Combatant>? companions = null, ICombatAI? companionMind = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
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
            CombatLog.Add($"{_player.DisplayName} (with {partyNames}) vs {_enemy.DisplayName}!");
        }
        else
        {
            CombatLog.Add($"{_player.DisplayName} vs {_enemy.DisplayName}!");
        }

        // Opponent always initiates the interaction
        State = BattleState.EnemyTurn;
        CombatLog.Add($"{_enemy.DisplayName} initiates combat!");
        ExecuteEnemyTurn();

        _turnNumber = 1;
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
            return new CombatEvent
            {
                Success = true,
                Message = $"{_enemy.DisplayName} succumbed to status effects!"
            };
        }

        // Select target from party (player + alive companions)
        var target = SelectEnemyTarget();

        if (_enemyMind == null)
        {
            // Fallback: basic attack if no AI provided
            var fallbackAction = ExecuteAttack(_enemy, target);
            RecordAction(fallbackAction);
            CheckBattleEnd();
            return fallbackAction;
        }

        // AI decides what to do based on observable battle state
        var snapshot = CreateBattleSnapshot(forEnemy: true);
        var decision = _enemyMind.DecideTurn(snapshot);

        // Execute the AI's decision against selected target
        var action = ExecuteDecision(_enemy, target, decision);
        RecordAction(action);

        if (action.Success)
        {
            CheckBattleEnd();
        }

        return action;
    }

    /// <summary>
    /// Select which party member the enemy will target.
    /// Currently uses simple logic: random alive party member, weighted toward player.
    /// </summary>
    private Combatant SelectEnemyTarget()
    {
        // Build list of alive targets
        var aliveTargets = new List<Combatant>();
        if (_player.IsAlive)
            aliveTargets.Add(_player);
        aliveTargets.AddRange(_companions.Where(c => c.IsAlive));

        if (aliveTargets.Count == 0)
            return _player;  // Shouldn't happen, but fallback

        if (aliveTargets.Count == 1)
            return aliveTargets[0];

        // Weight toward player (50% chance to target player, 50% split among companions)
        if (_player.IsAlive && _random.NextDouble() < 0.5)
            return _player;

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
                Success = true,
                Message = $"{companion.DisplayName} is defeated and cannot act"
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
                Success = true,
                Message = $"{companion.DisplayName} succumbed to status effects!"
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
            Energy = _enemy.Energy,
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
    /// Process player status effects at the start of their turn.
    /// Should be called by the UI/handler before presenting player options.
    /// </summary>
    public void ProcessPlayerTurnStart()
    {
        if (State != BattleState.PlayerTurn) return;

        ProcessStatusEffects(_player);

        // Check if player died from DoT
        if (!_player.IsAlive)
        {
            CheckBattleEnd();
        }
    }

    /// <summary>
    /// Execute a player decision (for AI-controlled players or UI-driven choices).
    /// </summary>
    public CombatEvent ExecutePlayerDecision(CombatAction decision)
    {
        if (State != BattleState.PlayerTurn)
        {
            return new CombatEvent
            {
                Success = false,
                Message = "Not player's turn"
            };
        }

        // Check if player died from status effects before their action
        if (!_player.IsAlive)
        {
            CheckBattleEnd();
            return new CombatEvent
            {
                Success = false,
                Message = $"{_player.DisplayName} succumbed to status effects!"
            };
        }

        var action = ExecuteDecision(_player, _enemy, decision);
        RecordAction(action);

        if (action.Success && State == BattleState.PlayerTurn)
        {
            CheckBattleEnd();
        }

        return action;
    }

    /// <summary>
    /// Get current battle snapshot for AI decision-making.
    /// Opponent's capabilities are hidden (set to null).
    /// </summary>
    public BattleView GetPlayerSnapshot()
    {
        return CreateBattleSnapshot(forEnemy: false);
    }

    /// <summary>
    /// Create battle snapshot for AI tactical decision-making.
    /// </summary>
    private BattleView CreateBattleSnapshot(bool forEnemy)
    {
        var self = forEnemy ? _enemy : _player;
        var opponent = forEnemy ? _player : _enemy;

        // Create observable opponent (hide their capabilities/inventory)
        // Stats shown are effective stats (base * stance multipliers)
        var observableOpponent = new Combatant
        {
            RefName = opponent.RefName,
            DisplayName = opponent.DisplayName,
            Health = opponent.Health,
            Energy = opponent.Energy,
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
        // PHASE 3: Check for Stun - prevents ALL actions
        if (HasStatusEffectOfType(actor, StatusEffectType.Stun))
        {
            CombatLog.Add($"💫 {actor.DisplayName} is stunned and cannot act!");
            return new CombatEvent
            {
                ActionType = BattleActionType.Attack, // Placeholder
                ActorName = actor.DisplayName,
                Success = false,
                Message = $"{actor.DisplayName} is stunned and cannot act!"
            };
        }

        // PHASE 3: Check for Silence - prevents spell casting
        if (decision.ActionType == ActionType.CastSpell && HasStatusEffectOfType(actor, StatusEffectType.Silence))
        {
            CombatLog.Add($"🔇 {actor.DisplayName} is silenced and cannot cast spells!");
            return new CombatEvent
            {
                ActionType = BattleActionType.SpecialAttack,
                ActorName = actor.DisplayName,
                Success = false,
                Message = $"{actor.DisplayName} is silenced and cannot cast spells!"
            };
        }

        // PHASE 3: Check for Root - prevents fleeing
        if (decision.ActionType == ActionType.Flee && HasStatusEffectOfType(actor, StatusEffectType.Root))
        {
            CombatLog.Add($"🌿 {actor.DisplayName} is rooted and cannot flee!");
            return new CombatEvent
            {
                ActionType = BattleActionType.Flee,
                ActorName = actor.DisplayName,
                Success = false,
                Message = $"{actor.DisplayName} is rooted and cannot flee!"
            };
        }

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

        return decision.ActionType switch
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

        return effectiveStrength;
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

        return effectiveDefense;
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

        return effectiveSpeed;
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

        return effectiveMagic;
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

        // Critical hit chance based on Speed (with stance multiplier)
        var effectiveSpeed = GetEffectiveSpeed(attacker);
        var critChance = Math.Min(0.3f, effectiveSpeed / 100f);
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

        // VERIFY: Weapon must be equipped in a slot (RightHand or LeftHand)
        var isEquipped = false;
        if (attacker.CombatProfile != null)
        {
            isEquipped = attacker.CombatProfile.TryGetValue("RightHand", out var rightHandItem) && rightHandItem == weapon.RefName
                      || attacker.CombatProfile.TryGetValue("LeftHand", out var leftHandItem) && leftHandItem == weapon.RefName;
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

        // Calculate base damage: Strength (with stance multiplier) scaled by weapon multiplier
        var effectiveStrength = GetEffectiveStrength(attacker);
        var baseDamage = effectiveStrength * WEAPON_DAMAGE_MULTIPLIER;

        // Critical hit calculation - base chance from speed + weapon CriticalHitBonus
        var effectiveSpeed = GetEffectiveSpeed(attacker);
        var baseCritChance = Math.Min(0.3f, effectiveSpeed / 100f);
        var critChance = Math.Min(0.5f, baseCritChance + weapon.CriticalHitBonus); // Cap at 50%
        var isCritical = _random.NextDouble() < critChance;

        // Apply weapon effects using EffectApplier
        var effects = EffectApplier.ApplyEffects(
            weapon.Effects ?? new CharacterEffects(),
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

        // Log affinity bonus if applicable
        var affinityMultiplier = EffectApplier.CalculateAffinityMultiplier(
            weapon.AffinityRef ?? attacker.AffinityRef,
            defender.AffinityRef,
            _world);

        if (affinityMultiplier > 1.0f)
        {
            CombatLog.Add($"⚡ Affinity advantage! ({affinityMultiplier:F1}x damage)");
        }
        else if (affinityMultiplier < 1.0f)
        {
            CombatLog.Add($"🛡️ Affinity resistance! ({affinityMultiplier:F1}x damage)");
        }

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
                    CombatLog.Add($"⚠️ {weapon.DisplayName} is badly damaged!");
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

            // Check both hand slots for the required equipment category
            foreach (var slot in new[] { "RightHand", "LeftHand" })
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

        // Calculate base damage: Magic (with stance multiplier) scaled by spell multiplier
        var effectiveMagic = GetEffectiveMagic(attacker);
        var baseDamage = effectiveMagic * SPELL_DAMAGE_MULTIPLIER;

        // Apply spell effects using EffectApplier
        // Use spell's UseType to determine if offensive (damage) or defensive (healing/buff)
        var effects = EffectApplier.ApplyEffects(
            spell.Effects ?? new CharacterEffects(),
            spell.AffinityRef,
            spellCondition,
            attacker.AffinityRef,
            defender.AffinityRef,
            isOffensive: spell.UseType == ItemUseType.Offensive,
            _world,
            spell.DisplayName);

        // Sum up Health damage from effects (should be negative)
        // Also apply caster costs (negative Mana/Stamina)
        var effectDamage = 0.0;
        var manaCost = 0.0;
        var staminaCost = 0.0;

        foreach (var effect in effects)
        {
            if (effect.StatName == "Health" && !effect.AppliedToAttacker)
            {
                effectDamage += effect.Change;  // Already negative for offensive
            }
            else if (effect.StatName == "Mana" && effect.AppliedToAttacker)
            {
                manaCost += Math.Abs(effect.Change);
            }
            else if (effect.StatName == "Stamina" && effect.AppliedToAttacker)
            {
                staminaCost += Math.Abs(effect.Change);
            }
        }

        // Check if attacker has enough Energy (using Energy for both Mana and Stamina)
        var totalCost = manaCost + staminaCost;
        if (attacker.Energy < totalCost)
        {
            CombatLog.Add($"{attacker.DisplayName} doesn't have enough energy to cast {spell.DisplayName}!");
            return new CombatEvent
            {
                Success = false,
                Message = "Not enough energy!"
            };
        }

        // Apply energy cost
        attacker.Energy = Math.Max(0, attacker.Energy - (float)totalCost);

        // Total damage = base + effect damage
        var totalDamage = Math.Max(BASE_DAMAGE_MINIMUM, (float)(baseDamage + Math.Abs(effectDamage)));

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

        // Log affinity bonus if applicable
        var affinityMultiplier = EffectApplier.CalculateAffinityMultiplier(
            spell.AffinityRef ?? attacker.AffinityRef,
            defender.AffinityRef,
            _world);

        if (affinityMultiplier > 1.0f)
        {
            CombatLog.Add($"⚡ Affinity advantage! ({affinityMultiplier:F1}x damage)");
        }
        else if (affinityMultiplier < 1.0f)
        {
            CombatLog.Add($"🛡️ Affinity resistance! ({affinityMultiplier:F1}x damage)");
        }

        CombatLog.Add($"{attacker.DisplayName} casts {spell.DisplayName} for {totalDamage * 100:F1}% damage!");
        if (totalCost > 0)
        {
            CombatLog.Add($"({totalCost * 100:F1}% energy used)");
        }
        CombatLog.Add($"{defender.DisplayName} HP: {defender.HealthPercent:F1}%");

        // Degrade spell condition slightly
        if (attacker.Capabilities?.Spells != null && spell.DurabilityLoss > 0)
        {
            var known = attacker.Capabilities.Spells.FirstOrDefault(s => s.SpellRef == spell.RefName);
            if (known != null)
            {
                known.Condition = Math.Max(0f, known.Condition - spell.DurabilityLoss);
                if (known.Condition < 0.3f)
                {
                    CombatLog.Add($"⚠️ {spell.DisplayName} knowledge is fading!");
                }
            }
        }

        // Apply status effect from spell (if defined)
        string? appliedStatusEffect = null;
        if (!string.IsNullOrEmpty(spell.StatusEffectRef))
        {
            var effectTarget = spell.UseType == ItemUseType.Offensive ? defender : attacker;
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
            TargetName = defender.DisplayName,
            Damage = totalDamage,
            IsCritical = false,
            Success = true,
            Message = $"{attacker.DisplayName} casts {spell.DisplayName}!",
            StatusEffectApplied = appliedStatusEffect
        };
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
            consumable.Effects ?? new CharacterEffects(),
            consumable.AffinityRef,
            1.0f,  // Consumables don't degrade
            user.AffinityRef,
            effectTarget.AffinityRef,
            isOffensive: isOffensive,
            _world,
            consumable.DisplayName);

        // Apply effects to appropriate target
        var totalHealthChange = 0.0f;
        foreach (var effect in effects)
        {
            if (effect.StatName == "Health" && !effect.AppliedToAttacker)
            {
                var change = (float)effect.Change;
                effectTarget.Health = Math.Clamp(effectTarget.Health + change, 0, Combatant.MAX_STAT);
                totalHealthChange += change;
            }
            else if (effect.StatName == "Stamina" || effect.StatName == "Mana")
            {
                // Energy costs/restores
                if (effect.AppliedToAttacker)
                {
                    // Cost to user
                    user.Energy = Math.Max(0, user.Energy + (float)effect.Change);
                }
                else
                {
                    // Restore to target
                    var energyRestore = (float)effect.Change;
                    effectTarget.Energy = Math.Min(Combatant.MAX_STAT, effectTarget.Energy + energyRestore);
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

        // Parse single change: "Slot:Value" (e.g., "RightHand:IronSword" or "Stance:Defensive" or "Affinity:Fire")
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

        // PHASE 6: Use two-handed weapon validation for hand slots
        if (slot == "RightHand" || slot == "LeftHand")
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
        // Example: "RightHand:IronSword,Affinity:Fire,Stance:Defensive"
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
            // PHASE 6: Use two-handed weapon validation for hand slots
            else if (slot == "RightHand" || slot == "LeftHand")
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
        combatant.Energy = Math.Min(Combatant.MAX_STAT, combatant.Energy + energyRestore);
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
                        CombatLog.Add($"🛡️ {equipment.DisplayName} triggers {result} while defending!");
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
        var fleeChance = 0.5 + fleer.Speed / 200f; // Base 50% + speed bonus

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
        if (_enemy.Health <= 0)
        {
            State = BattleState.Victory;
            CombatLog.Add("=== VICTORY ===");
            CombatLog.Add($"{_enemy.DisplayName} has been defeated!");
            return;
        }

        // Player down = defeat (companions flee without leader)
        if (_player.Health <= 0)
        {
            State = BattleState.Defeat;
            CombatLog.Add("=== DEFEAT ===");
            CombatLog.Add($"{_player.DisplayName} has been defeated...");
            if (_companions.Count > 0)
            {
                CombatLog.Add("Your companions flee without their leader!");
            }
            return;
        }

        // Switch turns: Player -> Companions -> Enemy -> Player
        if (State == BattleState.PlayerTurn)
        {
            // PHASE 6: Process EndOfTurn status effects for player before switching
            ProcessEndOfTurnStatusEffects(_player);

            // After player, companions go (if any alive)
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
            ProcessEndOfTurnStatusEffects(_enemy);

            State = BattleState.PlayerTurn;
            CombatLog.Add($"--- {_player.DisplayName}'s turn ---");
            _turnNumber++;
        }
        // CompanionTurn advancement is handled in AdvanceCompanionTurn()
    }

    /// <summary>
    /// Get the current player combatant (for UI binding).
    /// </summary>
    public Combatant GetPlayer() => _player;

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
    /// Set the player's available affinities for battle.
    /// </summary>
    public void SetPlayerAffinities(List<string> affinityRefs)
    {
        PlayerAffinityRefs = affinityRefs ?? new List<string>();
    }

    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================

    /// <summary>
    /// Check if combatant meets minimum stat requirements for an item.
    /// Returns a failed CombatEvent if requirements not met, null if OK.
    /// </summary>
    private CombatEvent? CheckMinimumStats(Combatant combatant, CharacterEffects minimumStats, string itemName)
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
        if (requiredEnergy > 0 && combatant.Energy < requiredEnergy)
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

            // Duration decrement only happens at StartOfTurn (to avoid double-counting)
            if (timing == ApplicationMethod.StartOfTurn)
            {
                active.RemainingTurns--;
                if (active.RemainingTurns <= 0)
                {
                    combatant.ActiveStatusEffects.RemoveAt(i);
                    CombatLog.Add($"✨ {statusEffect.DisplayName} wears off from {combatant.DisplayName}");
                }
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
    /// PHASE 6: Check if equipment is a two-handed weapon.
    /// Two-handed weapons occupy both RightHand and LeftHand slots.
    /// </summary>
    public bool IsTwoHandedWeapon(Equipment? equipment)
    {
        if (equipment == null) return false;
        return equipment.Category == EquipmentCategoryType.TwoHanded;
    }

    /// <summary>
    /// PHASE 6: Validates and applies equipment to a hand slot, handling two-handed weapon rules.
    /// Returns true if the equipment was successfully applied, false if validation failed.
    /// </summary>
    private bool TryApplyHandSlotEquipment(Combatant actor, string slot, string equipmentRef, out string? errorMessage)
    {
        errorMessage = null;

        // Only process hand slots for two-handed validation
        if (slot != "RightHand" && slot != "LeftHand")
        {
            actor.CombatProfile[slot] = equipmentRef;
            return true;
        }

        if (_world == null)
        {
            actor.CombatProfile[slot] = equipmentRef;
            return true;
        }

        var equipment = _world.TryGetEquipmentByRefName(equipmentRef);

        // If equipping a two-handed weapon
        if (IsTwoHandedWeapon(equipment))
        {
            // Check if the OTHER hand has something equipped
            var otherSlot = slot == "RightHand" ? "LeftHand" : "RightHand";
            if (actor.CombatProfile.TryGetValue(otherSlot, out var otherEquipRef) && !string.IsNullOrEmpty(otherEquipRef))
            {
                // Clear the other hand - two-handed weapons occupy both
                actor.CombatProfile[otherSlot] = string.Empty;
                CombatLog.Add($"  → {equipment?.DisplayName ?? equipmentRef} requires both hands - {otherSlot} cleared");
            }

            // Equip in both hands
            actor.CombatProfile["RightHand"] = equipmentRef;
            actor.CombatProfile["LeftHand"] = equipmentRef;
            CombatLog.Add($"  → Two-handed weapon {equipment?.DisplayName ?? equipmentRef} equipped in both hands");
            return true;
        }

        // PHASE 6 FIX: First check if EITHER hand has a two-handed weapon - must block one-handed equip
        var otherHandSlot = slot == "RightHand" ? "LeftHand" : "RightHand";

        // Check current slot for two-handed weapon
        var currentSlotEquip = actor.CombatProfile.TryGetValue(slot, out var currentRef) ? currentRef : null;
        if (!string.IsNullOrEmpty(currentSlotEquip))
        {
            var currentEquipment = _world.TryGetEquipmentByRefName(currentSlotEquip);
            if (IsTwoHandedWeapon(currentEquipment))
            {
                // Current slot has two-handed weapon - block one-handed equip
                errorMessage = $"Cannot equip {equipment?.DisplayName ?? equipmentRef} - {currentEquipment?.DisplayName ?? currentSlotEquip} is a two-handed weapon occupying both hands";
                CombatLog.Add($"  → {errorMessage}");
                return false;
            }
        }

        // Check other hand for two-handed weapon
        if (actor.CombatProfile.TryGetValue(otherHandSlot, out var otherHandRef) && !string.IsNullOrEmpty(otherHandRef))
        {
            var otherEquip = _world.TryGetEquipmentByRefName(otherHandRef);
            if (IsTwoHandedWeapon(otherEquip))
            {
                // Can't equip in this slot - other hand has a two-handed weapon
                errorMessage = $"Cannot equip {equipment?.DisplayName ?? equipmentRef} - {otherEquip?.DisplayName ?? otherHandRef} is a two-handed weapon occupying both hands";
                CombatLog.Add($"  → {errorMessage}");
                return false;
            }
        }

        // Normal one-handed equip
        actor.CombatProfile[slot] = equipmentRef;
        CombatLog.Add($"  → {slot} set to {equipment?.DisplayName ?? equipmentRef}");
        return true;
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
    /// Begin an attack with a telegraph, entering the reaction phase.
    /// Call this instead of directly executing an attack to enable player reactions.
    /// </summary>
    /// <param name="attacker">The attacking combatant</param>
    /// <param name="target">The target of the attack</param>
    /// <param name="tellRefName">Reference name of the attack tell to use</param>
    /// <param name="baseDamage">The base damage before reaction modifiers</param>
    /// <returns>True if reaction phase started, false if tell not found or invalid state</returns>
    public bool BeginAttackWithTell(Combatant attacker, Combatant target, string tellRefName, int baseDamage)
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
        CombatLog.Add($"⚔️ {tell.TellText}");
        CombatLog.Add($"   [DODGE] [BLOCK] [PARRY] [BRACE] - {tell.ReactionWindowMs / 1000.0:F1}s to react!");

        return true;
    }

    /// <summary>
    /// Resolve the pending attack with the player's chosen defense reaction.
    /// </summary>
    /// <param name="reaction">The defense reaction chosen by the player</param>
    /// <returns>The result of the reaction, or null if no pending attack</returns>
    public ReactionResult? ResolveReaction(PlayerDefenseType reaction)
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
            reaction = PlayerDefenseType.None;
            CombatLog.Add("⏱️ Time's up!");
        }

        var outcome = pending.Tell.GetOutcome(reaction);
        var finalDamage = (int)Math.Round(pending.BaseDamage * outcome.DamageMultiplier);

        // Apply damage
        pending.Target.Health -= finalDamage;

        // Build narrative
        var narrativeText = outcome.ResponseText;
        if (string.IsNullOrEmpty(narrativeText))
        {
            narrativeText = reaction switch
            {
                PlayerDefenseType.Dodge => finalDamage == 0 ? "You evade the attack!" : $"You dodge but take {finalDamage} damage.",
                PlayerDefenseType.Block => $"You block, taking {finalDamage} damage.",
                PlayerDefenseType.Parry => outcome.EnablesCounter ? "You parry and prepare to counter!" : $"You deflect, taking {finalDamage} damage.",
                PlayerDefenseType.Brace => $"You brace for impact, taking {finalDamage} damage.",
                _ => $"You take {finalDamage} damage!"
            };
        }

        CombatLog.Add($"🛡️ {narrativeText}");

        // Handle counter-attack
        int? counterDamage = null;
        if (outcome.EnablesCounter && pending.Target.IsAlive)
        {
            counterDamage = (int)Math.Round(pending.BaseDamage * outcome.CounterMultiplier);
            pending.Attacker.Health -= counterDamage.Value;
            CombatLog.Add($"⚡ Counter-attack hits {pending.Attacker.DisplayName} for {counterDamage} damage!");
        }

        // Apply defense effects (e.g., stamina recovery from skilled defense)
        float staminaGained = 0f;
        if (outcome.Effects != null && outcome.Effects.Stamina > 0)
        {
            var staminaGain = outcome.Effects.Stamina;
            var previousEnergy = pending.Target.Energy;
            pending.Target.Energy = Math.Min(Combatant.MAX_STAT, pending.Target.Energy + staminaGain);
            staminaGained = pending.Target.Energy - previousEnergy;

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
            // Move to player turn after enemy attack resolves
            State = BattleState.PlayerTurn;
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
            return ResolveReaction(PlayerDefenseType.None);
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
    /// Checks common weapon slots (RightHand, LeftHand, MainHand).
    /// </summary>
    private string? GetEquippedWeaponCategory(Combatant combatant)
    {
        if (_world == null || combatant.CombatProfile == null)
            return null;

        // Check common weapon slot names in priority order
        var weaponSlots = new[] { "RightHand", "MainHand", "LeftHand", "Weapon" };

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
