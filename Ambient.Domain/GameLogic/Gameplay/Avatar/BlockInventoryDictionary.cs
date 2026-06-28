using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// IDictionary&lt;string, float&gt; wrapper backed by Capabilities.Blocks (BlockEntry[]).
/// All reads/writes go through the array — no separate copy.
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
            var entry = Find(key);
            if (entry == null)
                throw new KeyNotFoundException($"Block '{key}' not found in inventory.");
            return entry.Quantity;
        }
        set
        {
            EnsureArray();
            var entry = Find(key);
            if (entry != null)
            {
                entry.Quantity = value;
            }
            else
            {
                var blocks = Blocks;
                var newBlocks = new BlockEntry[blocks.Length + 1];
                Array.Copy(blocks, newBlocks, blocks.Length);
                newBlocks[blocks.Length] = new BlockEntry { BlockRef = key, Quantity = value };
                _setBlocks(newBlocks);
            }
        }
    }

    public ICollection<string> Keys => Blocks.Select(b => b.BlockRef).ToList();
    public ICollection<float> Values => Blocks.Select(b => b.Quantity).ToList();
    IEnumerable<string> IReadOnlyDictionary<string, float>.Keys => Keys;
    IEnumerable<float> IReadOnlyDictionary<string, float>.Values => Values;
    public int Count => Blocks.Length;
    public bool IsReadOnly => false;

    public void Add(string key, float value)
    {
        if (Find(key) != null)
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
        var entry = Find(item.Key);
        return entry != null && entry.Quantity == item.Value;
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
        var index = Array.FindIndex(blocks, b => b.BlockRef == key);
        if (index < 0) return false;

        var newBlocks = new BlockEntry[blocks.Length - 1];
        Array.Copy(blocks, 0, newBlocks, 0, index);
        Array.Copy(blocks, index + 1, newBlocks, index, blocks.Length - index - 1);
        _setBlocks(newBlocks);
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
        if (entry != null)
        {
            value = entry.Quantity;
            return true;
        }
        value = 0;
        return false;
    }

    public IEnumerator<KeyValuePair<string, float>> GetEnumerator()
    {
        foreach (var block in Blocks)
        {
            yield return new KeyValuePair<string, float>(block.BlockRef, block.Quantity);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private BlockEntry? Find(string key) => Array.Find(Blocks, b => b.BlockRef == key);
}
