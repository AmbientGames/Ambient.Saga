using System.Linq;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.SagaEngine.Domain.Rpg.Battle;

/// <summary>
/// Current state of the battle.
/// </summary>
public enum BattleState
{
    NotStarted,
    PlayerTurn,
    EnemyTurn,
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
    private readonly Combatant _enemy;
    private readonly ICombatAI? _enemyMind;  // Tactical AI for enemy
    private readonly Random _random;
    private readonly World? _world;  // Needed for affinity lookups and item resolution

    private int _turnNumber;
    private readonly List<CombatEvent> _actionHistory = new();

    public BattleState State { get; private set; }
    public List<string> CombatLog { get; } = new();
    public List<string> PlayerAffinityRefs { get; private set; } = new();
    public IReadOnlyList<CombatEvent> ActionHistory => _actionHistory.AsReadOnly();

    /// <summary>
    /// Create a new battle engine.
    /// </summary>
    /// <param name="player">The player combatant</param>
    /// <param name="enemy">The enemy combatant</param>
    /// <param name="enemyMind">AI brain for enemy tactical decisions (optional for player-vs-player)</param>
    /// <param name="world">World data for affinity matchups and item lookups (required for advanced combat)</param>
    /// <param name="randomSeed">Optional seed for deterministic behavior in tests</param>
    public BattleEngine(Combatant player, Combatant enemy, ICombatAI? enemyMind = null, World? world = null, int? randomSeed = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
        _enemyMind = enemyMind;
        _world = world;
        _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        State = BattleState.NotStarted;
        _turnNumber = 0;
    }

    /// <summary>
    /// Start the battle and determine turn order.
    /// </summary>
    public void StartBattle()
    {
        if (State != BattleState.NotStarted)
            return;

        CombatLog.Add("=== BATTLE START ===");
        CombatLog.Add($"{_player.DisplayName} vs {_enemy.DisplayName}!");

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

        if (_enemyMind == null)
        {
            // Fallback: basic attack if no AI provided
            var fallbackAction = ExecuteAttack(_enemy, _player);
            RecordAction(fallbackAction);
            CheckBattleEnd();
            return fallbackAction;
        }

        // AI decides what to do based on observable battle state
        var snapshot = CreateBattleSnapshot(forEnemy: true);
        var decision = _enemyMind.DecideTurn(snapshot);

        // Execute the AI's decision
        var action = ExecuteDecision(_enemy, _player, decision);
        RecordAction(action);

        if (action.Success)
        {
            CheckBattleEnd();
        }

        return action;
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
    // STANCE MULTIPLIER HELPERS
    // ============================================================================

    /// <summary>
    /// Get effective Strength stat with stance multiplier applied.
    /// </summary>
    private float GetEffectiveStrength(Combatant combatant)
    {
        var baseStrength = combatant.Strength;
        if (_world == null || !combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) || string.IsNullOrEmpty(stanceRef))
            return baseStrength;

        var stance = _world.TryGetCombatStanceByRefName(stanceRef);
        return stance != null ? baseStrength * stance.StrengthMultiplier : baseStrength;
    }

    /// <summary>
    /// Get effective Defense stat with stance multiplier applied.
    /// </summary>
    private float GetEffectiveDefense(Combatant combatant)
    {
        var baseDefense = combatant.Defense;
        if (_world == null || !combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) || string.IsNullOrEmpty(stanceRef))
            return baseDefense;

        var stance = _world.TryGetCombatStanceByRefName(stanceRef);
        return stance != null ? baseDefense * stance.DefenseMultiplier : baseDefense;
    }

    /// <summary>
    /// Get effective Speed stat with stance multiplier applied.
    /// </summary>
    private float GetEffectiveSpeed(Combatant combatant)
    {
        var baseSpeed = combatant.Speed;
        if (_world == null || !combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) || string.IsNullOrEmpty(stanceRef))
            return baseSpeed;

        var stance = _world.TryGetCombatStanceByRefName(stanceRef);
        return stance != null ? baseSpeed * stance.SpeedMultiplier : baseSpeed;
    }

    /// <summary>
    /// Get effective Magic stat with stance multiplier applied.
    /// </summary>
    private float GetEffectiveMagic(Combatant combatant)
    {
        var baseMagic = combatant.Magic;
        if (_world == null || !combatant.CombatProfile.TryGetValue("Stance", out var stanceRef) || string.IsNullOrEmpty(stanceRef))
            return baseMagic;

        var stance = _world.TryGetCombatStanceByRefName(stanceRef);
        return stance != null ? baseMagic * stance.MagicMultiplier : baseMagic;
    }

    // ============================================================================
    // COMBAT ACTIONS
    // ============================================================================

    private CombatEvent ExecuteAttack(Combatant attacker, Combatant defender)
    {
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

        return new CombatEvent
        {
            ActionType = BattleActionType.Attack,
            ActorName = attacker.DisplayName,
            TargetName = defender.DisplayName,
            Damage = totalDamage,
            IsCritical = false,
            Success = true,
            Message = $"{attacker.DisplayName} attacks with {weapon.DisplayName}!"
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

        return new CombatEvent
        {
            ActionType = BattleActionType.SpecialAttack,  // Using SpecialAttack type for spells
            ActorName = attacker.DisplayName,
            TargetName = defender.DisplayName,
            Damage = totalDamage,
            IsCritical = false,
            Success = true,
            Message = $"{attacker.DisplayName} casts {spell.DisplayName}!"
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

        return new CombatEvent
        {
            ActionType = BattleActionType.UseItem,
            ActorName = user.DisplayName,
            TargetName = effectTarget.DisplayName,
            Damage = isOffensive ? Math.Abs(totalHealthChange) : 0,
            Healing = !isOffensive ? totalHealthChange : 0,
            Success = true,
            Message = $"{user.DisplayName} uses {consumable.DisplayName}!"
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

        actor.CombatProfile[slot] = value;
        CombatLog.Add($"  → {slot} set to {value}");

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

        return new CombatEvent
        {
            ActionType = BattleActionType.Defend,
            ActorName = combatant.DisplayName,
            TargetName = combatant.DisplayName,
            Healing = energyRestore,
            IsDefending = true,
            Success = true,
            Message = $"{combatant.DisplayName} defends!"
        };
    }

    private CombatEvent ExecuteFlee(Combatant fleer)
    {
        var fleeChance = 0.5 + fleer.Speed / 200f; // Base 50% + speed bonus

        if (_random.NextDouble() < fleeChance)
        {
            CombatLog.Add($"{fleer.DisplayName} successfully fled from battle!");
            State = BattleState.Fled;

            return new CombatEvent
            {
                ActionType = BattleActionType.Flee,
                ActorName = fleer.DisplayName,
                Success = true,
                Message = "Fled successfully!"
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

        if (_player.Health <= 0)
        {
            State = BattleState.Defeat;
            CombatLog.Add("=== DEFEAT ===");
            CombatLog.Add($"{_player.DisplayName} has been defeated...");
            return;
        }

        // Switch turns
        if (State == BattleState.PlayerTurn)
        {
            State = BattleState.EnemyTurn;
            CombatLog.Add($"--- {_enemy.DisplayName}'s turn ---");
        }
        else if (State == BattleState.EnemyTurn)
        {
            State = BattleState.PlayerTurn;
            CombatLog.Add($"--- {_player.DisplayName}'s turn ---");
            _turnNumber++;
        }
    }

    /// <summary>
    /// Get the current player combatant (for UI binding).
    /// </summary>
    public Combatant GetPlayer() => _player;

    /// <summary>
    /// Get the current enemy combatant (for UI binding).
    /// </summary>
    public Combatant GetEnemy() => _enemy;

    /// <summary>
    /// Get the world data (for equipment/spell/consumable lookups).
    /// </summary>
    public World? GetWorld() => _world;

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
}
