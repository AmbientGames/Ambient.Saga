using Ambient.Domain.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Schema-driven tool provider using the IGameplayItemProvider pattern.
/// Provides Minecraft-style tools organized by material tier.
/// </summary>
public class SchemaToolProvider : IGameplayItemProvider
{
    private readonly Dictionary<string, SchemaTool> _items;
    private readonly HashSet<string> _categories;

    public SchemaToolProvider()
    {
        var items = CreateTools().ToList();
        _items = items.ToDictionary(t => t.RefName);
        _categories = items.Select(t => t.Category).Distinct().ToHashSet();
    }

    /// <inheritdoc />
    public string Name => "Tools";

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetAll() => _items.Values;

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetByCategory(string category) =>
        _items.Values.Where(t => t.Category == category);

    /// <inheritdoc />
    public IGameplayItem? GetByRefName(string refName) =>
        _items.TryGetValue(refName, out var item) ? item : null;

    /// <inheritdoc />
    public IEnumerable<string> GetCategories() => _categories.OrderBy(c => GetCategoryOrder(c));

    private static int GetCategoryOrder(string category) => category switch
    {
        "Wood" => 0,
        "Stone" => 1,
        "Iron" => 2,
        "Gold" => 3,
        "Diamond" => 4,
        _ => 99
    };

    private static IEnumerable<SchemaTool> CreateTools()
    {
        // Wood tools
        yield return new SchemaTool("WoodPickaxe", "Wooden Pickaxe", "Wood", "Basic pickaxe for mining stone.", 10, 1.2f);
        yield return new SchemaTool("WoodAxe", "Wooden Axe", "Wood", "Basic axe for chopping wood.", 10, 1.2f);
        yield return new SchemaTool("WoodShovel", "Wooden Shovel", "Wood", "Basic shovel for digging dirt and sand.", 5, 1.2f);
        yield return new SchemaTool("WoodSword", "Wooden Sword", "Wood", "Basic sword for combat.", 8, 1.2f);
        yield return new SchemaTool("WoodHoe", "Wooden Hoe", "Wood", "Basic hoe for tilling soil.", 5, 1.2f);

        // Stone tools
        yield return new SchemaTool("StonePickaxe", "Stone Pickaxe", "Stone", "Sturdy pickaxe for mining ores.", 25, 1.3f);
        yield return new SchemaTool("StoneAxe", "Stone Axe", "Stone", "Sturdy axe for faster wood chopping.", 25, 1.3f);
        yield return new SchemaTool("StoneShovel", "Stone Shovel", "Stone", "Sturdy shovel for efficient digging.", 15, 1.3f);
        yield return new SchemaTool("StoneSword", "Stone Sword", "Stone", "Sturdy sword with improved damage.", 20, 1.3f);
        yield return new SchemaTool("StoneHoe", "Stone Hoe", "Stone", "Sturdy hoe for efficient farming.", 15, 1.3f);

        // Iron tools
        yield return new SchemaTool("IronPickaxe", "Iron Pickaxe", "Iron", "Strong pickaxe for mining diamonds.", 100, 1.5f);
        yield return new SchemaTool("IronAxe", "Iron Axe", "Iron", "Strong axe for rapid wood harvesting.", 100, 1.5f);
        yield return new SchemaTool("IronShovel", "Iron Shovel", "Iron", "Strong shovel for quick excavation.", 75, 1.5f);
        yield return new SchemaTool("IronSword", "Iron Sword", "Iron", "Strong sword with solid damage.", 90, 1.5f);
        yield return new SchemaTool("IronHoe", "Iron Hoe", "Iron", "Strong hoe for large-scale farming.", 75, 1.5f);

        // Gold tools (fast but fragile)
        yield return new SchemaTool("GoldPickaxe", "Golden Pickaxe", "Gold", "Fast but fragile pickaxe.", 200, 1.8f);
        yield return new SchemaTool("GoldAxe", "Golden Axe", "Gold", "Fast but fragile axe.", 200, 1.8f);
        yield return new SchemaTool("GoldShovel", "Golden Shovel", "Gold", "Fast but fragile shovel.", 150, 1.8f);
        yield return new SchemaTool("GoldSword", "Golden Sword", "Gold", "Fast but fragile sword.", 180, 1.8f);
        yield return new SchemaTool("GoldHoe", "Golden Hoe", "Gold", "Fast but fragile hoe.", 150, 1.8f);

        // Diamond tools
        yield return new SchemaTool("DiamondPickaxe", "Diamond Pickaxe", "Diamond", "The finest pickaxe. Mines anything.", 500, 2.0f);
        yield return new SchemaTool("DiamondAxe", "Diamond Axe", "Diamond", "The finest axe. Fells trees instantly.", 500, 2.0f);
        yield return new SchemaTool("DiamondShovel", "Diamond Shovel", "Diamond", "The finest shovel. Excavates rapidly.", 400, 2.0f);
        yield return new SchemaTool("DiamondSword", "Diamond Sword", "Diamond", "The finest sword. Devastating damage.", 450, 2.0f);
        yield return new SchemaTool("DiamondHoe", "Diamond Hoe", "Diamond", "The finest hoe. Why though?", 400, 2.0f);
    }
}
