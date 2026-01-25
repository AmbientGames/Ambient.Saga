using Ambient.Domain;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Schema-driven building material provider using the IGameplayItemProvider pattern.
/// Provides crafting/construction materials.
/// </summary>
public class SchemaBuildingMaterialProvider : IGameplayItemProvider
{
    private readonly Dictionary<string, SchemaBuildingMaterial> _items;
    private readonly HashSet<string> _categories;

    public SchemaBuildingMaterialProvider()
    {
        var items = CreateMaterials().ToList();
        _items = items.ToDictionary(m => m.RefName);
        _categories = items.Select(m => m.Category).Distinct().ToHashSet();
    }

    /// <inheritdoc />
    public string Name => "Building Materials";

    /// <inheritdoc />
    public string? CurrentItemRef { get; set; }

    /// <inheritdoc />
    public IGameplayItem? CurrentItem => CurrentItemRef != null ? GetByRefName(CurrentItemRef) : null;

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetAll() => _items.Values;

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetByCategory(string category) =>
        _items.Values.Where(m => m.Category == category);

    /// <inheritdoc />
    public IGameplayItem? GetByRefName(string refName) =>
        _items.TryGetValue(refName, out var item) ? item : null;

    /// <inheritdoc />
    public IEnumerable<string> GetCategories() => _categories.OrderBy(c => c);

    /// <inheritdoc />
    public bool ClearOnRespawn => false; // Keep materials on death

    /// <inheritdoc />
    public IEnumerable<StartingItem> GetSpawnItems(string? archetypeRef = null)
    {
        // All archetypes get some starting building materials
        yield return new StartingItem("Mortar", 16);
    }

    /// <inheritdoc />
    public IEnumerable<StartingItem> GetRespawnItems(string? archetypeRef = null)
    {
        // Minimal materials on respawn
        yield return new StartingItem("Mortar", 4);
    }

    private static IEnumerable<SchemaBuildingMaterial> CreateMaterials()
    {
        // Adhesives
        yield return new SchemaBuildingMaterial("Mortar", "Mortar", "Adhesive", "Traditional cement-based adhesive for stone and brick construction.", 15, 1.3f);
        yield return new SchemaBuildingMaterial("SuperGlue", "Super Glue", "Adhesive", "Industrial-strength adhesive. Bonds almost anything permanently.", 50, 1.6f);
    }

    /// <inheritdoc />
    public int GetAvatarItemQuantity(AvatarBase avatar, string refName)
    {
        if (avatar.GameplayInventory.TryGetValue(Name, out var items))
            return items.GetValueOrDefault(refName, 0);
        return 0;
    }

    /// <inheritdoc />
    public void GiveAvatarItem(AvatarBase avatar, string refName, int quantity = 1)
    {
        if (!avatar.GameplayInventory.ContainsKey(Name))
            avatar.GameplayInventory[Name] = new Dictionary<string, int>();

        var items = avatar.GameplayInventory[Name];
        items[refName] = items.GetValueOrDefault(refName, 0) + quantity;
    }

    /// <inheritdoc />
    public void TakeAvatarItem(AvatarBase avatar, string refName, int quantity = 1)
    {
        if (avatar.GameplayInventory.TryGetValue(Name, out var items))
        {
            var current = items.GetValueOrDefault(refName, 0);
            items[refName] = Math.Max(0, current - quantity);
        }
    }
}
