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
    public IEnumerable<IGameplayItem> GetAll() => _items.Values;

    /// <inheritdoc />
    public IEnumerable<IGameplayItem> GetByCategory(string category) =>
        _items.Values.Where(m => m.Category == category);

    /// <inheritdoc />
    public IGameplayItem? GetByRefName(string refName) =>
        _items.TryGetValue(refName, out var item) ? item : null;

    /// <inheritdoc />
    public IEnumerable<string> GetCategories() => _categories.OrderBy(c => c);

    private static IEnumerable<SchemaBuildingMaterial> CreateMaterials()
    {
        // Adhesives
        yield return new SchemaBuildingMaterial("Mortar", "Mortar", "Adhesive", "Traditional cement-based adhesive for stone and brick construction.", 15, 1.3f);
        yield return new SchemaBuildingMaterial("SuperGlue", "Super Glue", "Adhesive", "Industrial-strength adhesive. Bonds almost anything permanently.", 50, 1.6f);
    }
}
