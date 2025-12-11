using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Battle configuration helper for creating properly configured battle scenarios.
/// Used by both UI (BattleModal) and tests (BattleCommandTests).
/// </summary>
public class BattleSetup
{
    public IWorld LoadedWorld { get; private set; }

    // Avatar configuration
    public AvatarArchetype? SelectedAvatarArchetype { get; set; }
    public ItemCollection AvatarCapabilities { get; set; } = new ItemCollection();
    public CharacterStats? AvatarStatsOverride { get; set; } = null;
    public string? AvatarStartingAffinityRef { get; set; } = null;
    public string? AvatarStartingStanceRef { get; set; } = null;
    public List<string> AvatarAffinityRefs { get; set; } = new List<string>();

    // Opponent configuration
    public Character? SelectedOpponentCharacter { get; set; }
    public ItemCollection OpponentCapabilities { get; set; } = new ItemCollection();
    public CharacterStats? OpponentStatsOverride { get; set; } = null;

    // Party companions (characters who fight alongside player)
    public List<Character> CompanionCharacters { get; set; } = new List<Character>();

    public void SetupFromWorld(IWorld world)
    {
        LoadedWorld = world;
        System.Diagnostics.Debug.WriteLine("Battle setup initialized with world data");
        System.Diagnostics.Debug.WriteLine($"Available Archetypes: {world.Gameplay?.AvatarArchetypes?.Length ?? 0}");
        System.Diagnostics.Debug.WriteLine($"Available Equipment: {world.Gameplay?.Equipment?.Length ?? 0}");
        System.Diagnostics.Debug.WriteLine($"Available Consumables: {world.Gameplay?.Consumables?.Length ?? 0}");
        System.Diagnostics.Debug.WriteLine($"Available Spells: {world.Gameplay?.Spells?.Length ?? 0}");
        System.Diagnostics.Debug.WriteLine($"Available Characters: {world.Gameplay?.Characters?.Length ?? 0}");
    }

    /// <summary>
    /// Create the BattleEngine with configured combatants.
    /// </summary>
    public BattleEngine CreateBattleEngine()
    {
        if (LoadedWorld == null)
            throw new InvalidOperationException("World not loaded");

        if (SelectedAvatarArchetype == null)
            throw new InvalidOperationException("Avatar archetype not selected");

        if (SelectedOpponentCharacter == null)
            throw new InvalidOperationException("Opponent character not selected");

        // Create player combatant from archetype
        var avatarStats = AvatarStatsOverride ?? SelectedAvatarArchetype.SpawnStats;
        if (avatarStats == null)
            throw new InvalidOperationException("Avatar archetype must have SpawnStats defined");

        var playerCombatant = new Combatant
        {
            RefName = SelectedAvatarArchetype.RefName,
            DisplayName = SelectedAvatarArchetype.DisplayName,
            Health = avatarStats.Health,
            Energy = avatarStats.Stamina,
            Strength = avatarStats.Strength,
            Defense = avatarStats.Defense,
            Speed = avatarStats.Speed,
            Magic = avatarStats.Magic,
            AffinityRef = AvatarStartingAffinityRef ?? SelectedAvatarArchetype.AffinityRef,
            Capabilities = AvatarCapabilities
        };

        // Initialize EquippedItems by assigning equipment to slots
        InitializeEquippedSlots(playerCombatant, LoadedWorld);

        // Set starting stance
        if (!string.IsNullOrEmpty(AvatarStartingStanceRef))
        {
            playerCombatant.CombatProfile["Stance"] = AvatarStartingStanceRef;
            System.Diagnostics.Debug.WriteLine($"  Set starting stance: {AvatarStartingStanceRef}");
        }

        // Create enemy combatant from character
        var opponentStats = OpponentStatsOverride ?? SelectedOpponentCharacter.Stats;
        if (opponentStats == null)
            throw new InvalidOperationException("Opponent character must have Stats defined");

        var enemyCombatant = new Combatant
        {
            RefName = SelectedOpponentCharacter.RefName,
            DisplayName = SelectedOpponentCharacter.DisplayName,
            Health = opponentStats.Health,
            Energy = opponentStats.Stamina,
            Strength = opponentStats.Strength,
            Defense = opponentStats.Defense,
            Speed = opponentStats.Speed,
            Magic = opponentStats.Magic,
            AffinityRef = SelectedOpponentCharacter.AffinityRef,
            Capabilities = OpponentCapabilities
        };

        // Initialize EquippedItems by assigning equipment to slots
        InitializeEquippedSlots(enemyCombatant, LoadedWorld);

        // Set default stance for opponent
        enemyCombatant.CombatProfile["Stance"] = "Balanced";
        System.Diagnostics.Debug.WriteLine($"  Set opponent stance: Balanced");

        // Create companion combatants
        var companions = new List<Combatant>();
        foreach (var companionChar in CompanionCharacters)
        {
            if (companionChar.Stats == null)
            {
                System.Diagnostics.Debug.WriteLine($"  Skipping companion {companionChar.DisplayName} - no stats defined");
                continue;
            }

            var companionCombatant = new Combatant
            {
                RefName = companionChar.RefName,
                DisplayName = companionChar.DisplayName,
                Health = companionChar.Stats.Health,
                Energy = companionChar.Stats.Stamina,
                Strength = companionChar.Stats.Strength,
                Defense = companionChar.Stats.Defense,
                Speed = companionChar.Stats.Speed,
                Magic = companionChar.Stats.Magic,
                AffinityRef = companionChar.AffinityRef,
                ArchetypeBias = companionChar.ArchetypeBias,
                Capabilities = companionChar.Capabilities
            };

            // Initialize equipped slots
            InitializeEquippedSlots(companionCombatant, LoadedWorld);
            companionCombatant.CombatProfile["Stance"] = "Balanced";

            companions.Add(companionCombatant);
            System.Diagnostics.Debug.WriteLine($"  Added companion: {companionCombatant.DisplayName} (HP: {companionCombatant.Health:F2})");
        }

        // Create BattleEngine with CombatAI for opponent and companions
        var enemyMind = new CombatAI(LoadedWorld);
        var battleEngine = new BattleEngine(playerCombatant, enemyCombatant, enemyMind, LoadedWorld,
            companions: companions.Count > 0 ? companions : null);

        // Set player's available affinities
        battleEngine.SetPlayerAffinities(AvatarAffinityRefs);

        System.Diagnostics.Debug.WriteLine($"\n=== BATTLE ENGINE CREATED ===");
        System.Diagnostics.Debug.WriteLine($"Player: {playerCombatant.DisplayName} (HP: {playerCombatant.Health:F2})");
        if (companions.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Companions: {string.Join(", ", companions.Select(c => c.DisplayName))}");
        }
        System.Diagnostics.Debug.WriteLine($"Enemy: {enemyCombatant.DisplayName} (HP: {enemyCombatant.Health:F2})");

        return battleEngine;
    }

    /// <summary>
    /// Initialize EquippedItems dictionary from Capabilities.Equipment.
    /// </summary>
    private void InitializeEquippedSlots(Combatant combatant, IWorld world)
    {
        if (combatant.Capabilities?.Equipment == null || combatant.Capabilities.Equipment.Length == 0)
            return;

        foreach (var entry in combatant.Capabilities.Equipment)
        {
            var equipment = world.GetEquipmentByRefName(entry.EquipmentRef);
            if (equipment != null && !string.IsNullOrEmpty(equipment.SlotRef))
            {
                var slotName = equipment.SlotRef.ToString();
                combatant.CombatProfile[slotName] = entry.EquipmentRef;
                System.Diagnostics.Debug.WriteLine($"  Equipped {equipment.DisplayName} in {slotName} slot");
            }
        }
    }
}
