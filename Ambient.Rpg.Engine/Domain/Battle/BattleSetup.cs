using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Domain.Battle;

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

    // Party companions (characters who fight alongside avatar)
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
    /// <param name="randomSeed">Optional seed applied to both the engine and the opponent AI for deterministic battles (tests)</param>
    public BattleEngine CreateBattleEngine(int? randomSeed = null)
    {
        if (LoadedWorld == null)
            throw new InvalidOperationException("World not loaded");

        if (SelectedAvatarArchetype == null)
            throw new InvalidOperationException("Avatar archetype not selected");

        if (SelectedOpponentCharacter == null)
            throw new InvalidOperationException("Opponent character not selected");

        // Create avatar combatant from archetype
        var avatarStats = AvatarStatsOverride ?? SelectedAvatarArchetype.SpawnStats;
        if (avatarStats == null)
            throw new InvalidOperationException("Avatar archetype must have SpawnStats defined");

        var avatarCombatant = new Combatant
        {
            RefName = SelectedAvatarArchetype.RefName,
            DisplayName = SelectedAvatarArchetype.DisplayName,
            Health = avatarStats.Health,
            Stamina = avatarStats.Stamina,
            Strength = avatarStats.Strength,
            Defense = avatarStats.Defense,
            Speed = avatarStats.Speed,
            Magic = avatarStats.Magic,
            AffinityRef = AvatarStartingAffinityRef ?? SelectedAvatarArchetype.AffinityRef,
            Capabilities = AvatarCapabilities
        };

        // Initialize EquippedItems by assigning equipment to slots
        InitializeEquippedSlots(avatarCombatant, LoadedWorld);

        // Set starting stance
        if (!string.IsNullOrEmpty(AvatarStartingStanceRef))
        {
            avatarCombatant.CombatProfile[TransactionDataKeys.Stance] = AvatarStartingStanceRef;
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
            Stamina = opponentStats.Stamina,
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
        enemyCombatant.CombatProfile[TransactionDataKeys.Stance] = "Balanced";
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
                Stamina = companionChar.Stats.Stamina,
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
            companionCombatant.CombatProfile[TransactionDataKeys.Stance] = "Balanced";

            companions.Add(companionCombatant);
            System.Diagnostics.Debug.WriteLine($"  Added companion: {companionCombatant.DisplayName} (HP: {companionCombatant.Health:F2})");
        }

        // Create BattleEngine with CombatAI for opponent and companions
        var enemyMind = new CombatAI(LoadedWorld, randomSeed);
        var battleEngine = new BattleEngine(avatarCombatant, enemyCombatant, enemyMind, LoadedWorld,
            randomSeed: randomSeed,
            companions: companions.Count > 0 ? companions : null);

        // Set avatar's available affinities
        battleEngine.SetAvatarAffinities(AvatarAffinityRefs);

        System.Diagnostics.Debug.WriteLine($"\n=== BATTLE ENGINE CREATED ===");
        System.Diagnostics.Debug.WriteLine($"Avatar: {avatarCombatant.DisplayName} (HP: {avatarCombatant.Health:F2})");
        if (companions.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Companions: {string.Join(", ", companions.Select(c => c.DisplayName))}");
        }
        System.Diagnostics.Debug.WriteLine($"Enemy: {enemyCombatant.DisplayName} (HP: {enemyCombatant.Health:F2})");

        return battleEngine;
    }

    /// <summary>
    /// Initialize EquippedItems dictionary from Capabilities.Equipment.
    /// Hand slots are exclusive: a BothHands weapon occupies MainHand and OffHand,
    /// so equipping everything owned by SlotRef used to stack a two-hander's
    /// passives on top of a one-hander's from turn 0.
    /// </summary>
    private void InitializeEquippedSlots(Combatant combatant, IWorld world)
    {
        if (combatant.Capabilities?.Equipment == null || combatant.Capabilities.Equipment.Length == 0)
            return;

        foreach (var entry in combatant.Capabilities.Equipment)
        {
            var equipment = world.GetEquipmentByRefName(entry.EquipmentRef);
            if (equipment == null || string.IsNullOrEmpty(equipment.SlotRef))
                continue;

            var slotName = equipment.SlotRef.ToString();

            // Enforce hand exclusivity (first equipped wins, matching slot overwrite
            // semantics elsewhere: later same-slot items replace, but a occupied
            // conflicting hand blocks)
            if (slotName == "BothHands" &&
                (combatant.CombatProfile.ContainsKey("MainHand") || combatant.CombatProfile.ContainsKey("OffHand")))
            {
                continue;
            }
            if ((slotName == "MainHand" || slotName == "OffHand") &&
                combatant.CombatProfile.ContainsKey("BothHands"))
            {
                continue;
            }

            combatant.CombatProfile[slotName] = entry.EquipmentRef;
            System.Diagnostics.Debug.WriteLine($"  Equipped {equipment.DisplayName} in {slotName} slot");
        }
    }
}
