using Ambient.Domain.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Mock block provider for sandbox demonstration.
/// Provides sample blocks to show how the block system works without
/// exposing actual game block implementations.
/// </summary>
public class MockBlockProvider : IBlockProvider
{
    private readonly Dictionary<string, MockBlock> _blocks;

    public MockBlockProvider()
    {
        _blocks = CreateSampleBlocks().ToDictionary(b => b.RefName);
    }

    /// <inheritdoc />
    public IEnumerable<IBlock> GetAllBlocks() => _blocks.Values;

    /// <inheritdoc />
    public IBlock? GetBlockByRefName(string blockRef) =>
        _blocks.TryGetValue(blockRef, out var block) ? block : null;

    /// <inheritdoc />
    public IEnumerable<IBlock> GetBlocksBySubstance(string substanceRef) =>
        _blocks.Values.Where(b => b.SubstanceRef == substanceRef);

    private static IEnumerable<MockBlock> CreateSampleBlocks()
    {
        // Stone blocks
        yield return new MockBlock("Stone", "Stone", "Common stone block. The foundation of most construction.", "Stone", 5, 1.2f);
        yield return new MockBlock("Cobblestone", "Cobblestone", "Rough stone blocks, good for paths and walls.", "Stone", 3, 1.1f);
        yield return new MockBlock("StoneBrick", "Stone Brick", "Refined stone blocks for quality construction.", "Stone", 15, 1.4f);
        yield return new MockBlock("Granite", "Granite", "Dense igneous rock. Very durable.", "Stone", 25, 1.5f);
        yield return new MockBlock("Marble", "Marble", "Elegant metamorphic stone for decorative builds.", "Stone", 50, 1.8f);

        // Wood blocks
        yield return new MockBlock("OakLog", "Oak Log", "Sturdy oak wood. A reliable building material.", "Wood", 8, 1.2f);
        yield return new MockBlock("OakPlanks", "Oak Planks", "Processed oak lumber for construction.", "Wood", 12, 1.3f);
        yield return new MockBlock("BirchLog", "Birch Log", "Light-colored birch wood.", "Wood", 8, 1.2f);
        yield return new MockBlock("BirchPlanks", "Birch Planks", "Pale birch lumber with fine grain.", "Wood", 12, 1.3f);
        yield return new MockBlock("DarkOakLog", "Dark Oak Log", "Dense, dark hardwood from ancient forests.", "Wood", 20, 1.5f);

        // Metal blocks
        yield return new MockBlock("IronBlock", "Iron Block", "Solid iron. Heavy and strong.", "Metal", 100, 1.6f);
        yield return new MockBlock("GoldBlock", "Gold Block", "Pure gold. Valuable but soft.", "Metal", 500, 2.0f);
        yield return new MockBlock("CopperBlock", "Copper Block", "Copper block. Develops patina over time.", "Metal", 75, 1.5f);

        // Aggregate blocks
        yield return new MockBlock("Dirt", "Dirt", "Common soil. Easy to dig.", "Aggregate", 1, 1.0f);
        yield return new MockBlock("Grass", "Grass Block", "Dirt with grass on top.", "Aggregate", 2, 1.0f);
        yield return new MockBlock("Sand", "Sand", "Fine sand. Falls when unsupported.", "Aggregate", 2, 1.0f);
        yield return new MockBlock("Gravel", "Gravel", "Loose stones. Falls when unsupported.", "Aggregate", 3, 1.1f);
        yield return new MockBlock("Clay", "Clay", "Moldable clay. Can be fired into bricks.", "Aggregate", 10, 1.2f);

        // Decorative blocks
        yield return new MockBlock("Glass", "Glass", "Transparent glass block.", "Decorative", 20, 1.5f);
        yield return new MockBlock("Obsidian", "Obsidian", "Volcanic glass. Extremely hard.", "Stone", 200, 2.5f);
        yield return new MockBlock("Glowstone", "Glowstone", "Luminescent block. Provides light.", "Decorative", 150, 2.0f);

        // Miscellaneous blocks (machines, storage, lighting)
        yield return new MockBlock("Cache", "Storage Cache", "Secure storage container for items.", "Miscellaneous", 50, 1.5f);
        yield return new MockBlock("Lamp", "Lamp", "Portable light source.", "Miscellaneous", 15, 1.3f);
        yield return new MockBlock("WoodStove", "Wood Stove", "Basic heating and cooking appliance.", "Alloy", 75, 1.4f);
        yield return new MockBlock("BlastFurnace", "Blast Furnace", "Industrial furnace for smelting ores.", "Alloy", 200, 1.6f);
        yield return new MockBlock("BlastFurnaceMK2", "Blast Furnace MK2", "Improved blast furnace with higher efficiency.", "Alloy", 400, 1.8f);
        yield return new MockBlock("BlastFurnaceMK3", "Blast Furnace MK3", "Advanced blast furnace for rapid smelting.", "Alloy", 800, 2.0f);
        yield return new MockBlock("CharcoalKiln", "Charcoal Kiln", "Converts wood into charcoal fuel.", "Miscellaneous", 100, 1.5f);
        yield return new MockBlock("SawMill", "Sawmill", "Processes logs into lumber efficiently.", "Miscellaneous", 150, 1.5f);

        // Natural resources
        yield return new MockBlock("Oak", "Oak Wood", "Raw oak timber for construction and fuel.", "Wood", 5, 1.2f);
        yield return new MockBlock("Coal", "Coal", "Combustible mineral for fuel and smelting.", "Ore", 10, 1.3f);
        yield return new MockBlock("IronOre", "Iron Ore", "Raw iron ore for smelting into iron.", "Ore", 20, 1.4f);
        yield return new MockBlock("ManganeseOre", "Manganese Ore", "Rare ore used in steel alloys.", "Ore", 50, 1.6f);
        yield return new MockBlock("ChromiumOre", "Chromium Ore", "Precious ore for advanced metallurgy.", "Ore", 75, 1.8f);
    }
}

/// <summary>
/// Simple block implementation for sandbox demonstration.
/// Real games would implement IBlock on their actual Block class.
/// </summary>
public class MockBlock : IBlock
{
    public string RefName { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public string? TextureRef { get; }
    public string? SubstanceRef { get; }
    public int WholesalePrice { get; }
    public float MerchantMarkupMultiplier { get; }

    public MockBlock(
        string refName,
        string displayName,
        string? description,
        string? substanceRef,
        int wholesalePrice,
        float merchantMarkupMultiplier,
        string? textureRef = null)
    {
        RefName = refName;
        DisplayName = displayName;
        Description = description;
        SubstanceRef = substanceRef;
        WholesalePrice = wholesalePrice;
        MerchantMarkupMultiplier = merchantMarkupMultiplier;
        TextureRef = textureRef ?? refName; // Default texture ref to block name
    }
}
