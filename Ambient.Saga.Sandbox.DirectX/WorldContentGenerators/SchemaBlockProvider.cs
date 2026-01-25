using Ambient.Domain.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Schema-driven block provider using the new IGameplayItemProvider pattern.
/// Demonstrates how games can provide their own item catalogs through the generic interface.
///
/// This replaces the older IBlockProvider pattern with a more flexible approach
/// where games define their own categories.
/// </summary>
public class SchemaBlockProvider : IGameplayItemProvider
{
    private readonly Dictionary<string, SchemaBlock> _items;
    private readonly HashSet<string> _categories;

    public SchemaBlockProvider()
    {
        var items = CreateSampleBlocks().ToList();
        _items = items.ToDictionary(b => b.RefName);
        _categories = items.Select(b => b.Category).Distinct().ToHashSet();
    }

    /// <inheritdoc />
    public string Name => "Blocks";

    /// <inheritdoc />
    public string? CurrentItemRef { get; set; }

    /// <inheritdoc />
    public IGameplayItem? CurrentItem => CurrentItemRef != null ? GetByRefName(CurrentItemRef) : null;

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetAll() => _items.Values;

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetByCategory(string category) =>
        _items.Values.Where(b => b.Category == category);

    /// <inheritdoc />
    public IGameplayItem? GetByRefName(string refName) =>
        _items.TryGetValue(refName, out var item) ? item : null;

    /// <inheritdoc />
    public IEnumerable<string> GetCategories() => _categories.OrderBy(c => c);

    /// <inheritdoc />
    public bool ClearOnRespawn => false; // Keep blocks on death - they're resources

    /// <inheritdoc />
    public IEnumerable<StartingItem> GetSpawnItems(string? archetypeRef = null)
    {
        // All archetypes get the same starting blocks
        yield return new StartingItem("Dirt", 64);
        yield return new StartingItem("Cobblestone", 32);
        yield return new StartingItem("Stone", 16);
        yield return new StartingItem("OakPlanks", 32);
        yield return new StartingItem("OakLog", 16);
    }

    /// <inheritdoc />
    public IEnumerable<StartingItem> GetRespawnItems(string? archetypeRef = null)
    {
        // On respawn, minimal blocks (inventory preserved due to ClearOnRespawn = false, but add some)
        yield return new StartingItem("Dirt", 16);
        yield return new StartingItem("Cobblestone", 8);
    }

    private static IEnumerable<SchemaBlock> CreateSampleBlocks()
    {
        // Stone blocks
        yield return new SchemaBlock("Stone", "Stone", "Stone", "Common stone block. The foundation of most construction.", 5, 1.2f);
        yield return new SchemaBlock("Cobblestone", "Cobblestone", "Stone", "Rough stone blocks, good for paths and walls.", 3, 1.1f);
        yield return new SchemaBlock("StoneBrick", "Stone Brick", "Stone", "Refined stone blocks for quality construction.", 15, 1.4f);
        yield return new SchemaBlock("Granite", "Granite", "Stone", "Dense igneous rock. Very durable.", 25, 1.5f);
        yield return new SchemaBlock("Marble", "Marble", "Stone", "Elegant metamorphic stone for decorative builds.", 50, 1.8f);

        // Wood blocks
        yield return new SchemaBlock("OakLog", "Oak Log", "Wood", "Sturdy oak wood. A reliable building material.", 8, 1.2f);
        yield return new SchemaBlock("OakPlanks", "Oak Planks", "Wood", "Processed oak lumber for construction.", 12, 1.3f);
        yield return new SchemaBlock("BirchLog", "Birch Log", "Wood", "Light-colored birch wood.", 8, 1.2f);
        yield return new SchemaBlock("BirchPlanks", "Birch Planks", "Wood", "Pale birch lumber with fine grain.", 12, 1.3f);
        yield return new SchemaBlock("DarkOakLog", "Dark Oak Log", "Wood", "Dense, dark hardwood from ancient forests.", 20, 1.5f);

        // Metal blocks
        yield return new SchemaBlock("IronBlock", "Iron Block", "Metal", "Solid iron. Heavy and strong.", 100, 1.6f);
        yield return new SchemaBlock("GoldBlock", "Gold Block", "Metal", "Pure gold. Valuable but soft.", 500, 2.0f);
        yield return new SchemaBlock("CopperBlock", "Copper Block", "Metal", "Copper block. Develops patina over time.", 75, 1.5f);

        // Earth blocks
        yield return new SchemaBlock("Dirt", "Dirt", "Earth", "Common soil. Easy to dig.", 1, 1.0f);
        yield return new SchemaBlock("Grass", "Grass Block", "Earth", "Dirt with grass on top.", 2, 1.0f);
        yield return new SchemaBlock("Sand", "Sand", "Earth", "Fine sand. Falls when unsupported.", 2, 1.0f);
        yield return new SchemaBlock("Gravel", "Gravel", "Earth", "Loose stones. Falls when unsupported.", 3, 1.1f);
        yield return new SchemaBlock("Clay", "Clay", "Earth", "Moldable clay. Can be fired into bricks.", 10, 1.2f);

        // Special blocks
        yield return new SchemaBlock("Glass", "Glass", "Glass", "Transparent glass block.", 20, 1.5f);
        yield return new SchemaBlock("Obsidian", "Obsidian", "Stone", "Volcanic glass. Extremely hard.", 200, 2.5f);
        yield return new SchemaBlock("Glowstone", "Glowstone", "Crystal", "Luminescent block. Provides light.", 150, 2.0f);

        // Functional blocks (machines, storage, lighting)
        yield return new SchemaBlock("Cache", "Storage Cache", "Functional", "Secure storage container for items.", 50, 1.5f);
        yield return new SchemaBlock("Lamp", "Lamp", "Functional", "Portable light source.", 15, 1.3f);
        yield return new SchemaBlock("WoodStove", "Wood Stove", "Functional", "Basic heating and cooking appliance.", 75, 1.4f);
        yield return new SchemaBlock("BlastFurnace", "Blast Furnace", "Functional", "Industrial furnace for smelting ores.", 200, 1.6f);
        yield return new SchemaBlock("BlastFurnaceMK2", "Blast Furnace MK2", "Functional", "Improved blast furnace with higher efficiency.", 400, 1.8f);
        yield return new SchemaBlock("BlastFurnaceMK3", "Blast Furnace MK3", "Functional", "Advanced blast furnace for rapid smelting.", 800, 2.0f);
        yield return new SchemaBlock("CharcoalKiln", "Charcoal Kiln", "Functional", "Converts wood into charcoal fuel.", 100, 1.5f);
        yield return new SchemaBlock("SawMill", "Sawmill", "Functional", "Processes logs into lumber efficiently.", 150, 1.5f);

        // Natural resources
        yield return new SchemaBlock("Oak", "Oak Wood", "Wood", "Raw oak timber for construction and fuel.", 5, 1.2f);
        yield return new SchemaBlock("Coal", "Coal", "Ore", "Combustible mineral for fuel and smelting.", 10, 1.3f);
        yield return new SchemaBlock("IronOre", "Iron Ore", "Ore", "Raw iron ore for smelting into iron.", 20, 1.4f);
        yield return new SchemaBlock("ManganeseOre", "Manganese Ore", "Ore", "Rare ore used in steel alloys.", 50, 1.6f);
        yield return new SchemaBlock("ChromiumOre", "Chromium Ore", "Ore", "Precious ore for advanced metallurgy.", 75, 1.8f);
    }
}
