using System.Collections;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// IDictionary&lt;string, float&gt; wrapper backed by Capabilities.Blocks (BlockEntry[]).
/// All reads/writes go through the array — no separate copy.
/// A stack's identity is its <see cref="BlockEntry.BlockRef"/>, treated as an opaque string:
/// the dictionary never parses a ref, so distinct refs are simply distinct keys.
/// </summary>
public class BlockInventoryDictionary : IDictionary<string, float>, IReadOnlyDictionary<string, float>
{
    private readonly Func<BlockEntry[]?> _getBlocks;
    private readonly Action<BlockEntry[]> _setBlocks;

    public BlockInventoryDictionary(Func<BlockEntry[]?> getBlocks, Action<BlockEntry[]> setBlocks)
    {
        _getBlocks = getBlocks;
        _setBlocks = setBlocks;
    }

    private BlockEntry[] Blocks => _getBlocks() ?? [];

    private void EnsureArray()
    {
        if (_getBlocks() == null)
            _setBlocks([]);
    }

    public float this[string key]
    {
        get
        {
            if (!TryGetValue(key, out var quantity))
                throw new KeyNotFoundException($"Block '{key}' not found in inventory.");
            return quantity;
        }
        set
        {
            EnsureArray();
            var entry = Find(key);
            if (entry != null)
                entry.Quantity = value;
            else
                Append(new BlockEntry { BlockRef = key, Quantity = value });
        }
    }

    /// <summary>
    /// Adjusts the stack identified by <paramref name="blockRef"/> by delta, creating it when absent.
    /// </summary>
    public void Adjust(string blockRef, float delta)
    {
        EnsureArray();
        var entry = Find(blockRef);
        if (entry != null)
            entry.Quantity += delta;
        else
            Append(new BlockEntry { BlockRef = blockRef, Quantity = delta });
    }

    /// <summary>
    /// Every stack in the inventory.
    /// </summary>
    public IReadOnlyList<BlockEntry> Entries => Blocks;

    public ICollection<string> Keys => Blocks.Select(b => b.BlockRef).ToList();
    public ICollection<float> Values => Blocks.Select(b => b.Quantity).ToList();
    IEnumerable<string> IReadOnlyDictionary<string, float>.Keys => Keys;
    IEnumerable<float> IReadOnlyDictionary<string, float>.Values => Values;
    public int Count => Blocks.Length;
    public bool IsReadOnly => false;

    public void Add(string key, float value)
    {
        if (ContainsKey(key))
            throw new ArgumentException($"Block '{key}' already exists in inventory.");
        this[key] = value;
    }

    public void Add(KeyValuePair<string, float> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        _setBlocks([]);
    }

    public bool Contains(KeyValuePair<string, float> item)
    {
        return TryGetValue(item.Key, out var quantity) && quantity == item.Value;
    }

    public bool ContainsKey(string key) => Find(key) != null;

    public void CopyTo(KeyValuePair<string, float>[] array, int arrayIndex)
    {
        foreach (var block in Blocks)
        {
            array[arrayIndex++] = new KeyValuePair<string, float>(block.BlockRef, block.Quantity);
        }
    }

    public bool Remove(string key)
    {
        var blocks = Blocks;
        if (!blocks.Any(b => b.BlockRef == key)) return false;

        _setBlocks(blocks.Where(b => b.BlockRef != key).ToArray());
        return true;
    }

    public bool Remove(KeyValuePair<string, float> item)
    {
        if (!Contains(item)) return false;
        return Remove(item.Key);
    }

    public bool TryGetValue(string key, out float value)
    {
        var entry = Find(key);
        value = entry?.Quantity ?? 0;
        return entry != null;
    }

    public IEnumerator<KeyValuePair<string, float>> GetEnumerator()
    {
        foreach (var block in Blocks)
        {
            yield return new KeyValuePair<string, float>(block.BlockRef, block.Quantity);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private BlockEntry? Find(string blockRef) => Array.Find(Blocks, b => b.BlockRef == blockRef);

    private void Append(BlockEntry entry)
    {
        var blocks = Blocks;
        var newBlocks = new BlockEntry[blocks.Length + 1];
        Array.Copy(blocks, newBlocks, blocks.Length);
        newBlocks[blocks.Length] = entry;
        _setBlocks(newBlocks);
    }
}
