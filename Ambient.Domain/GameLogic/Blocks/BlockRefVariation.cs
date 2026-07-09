namespace Ambient.Domain;

/// <summary>
/// A block stack's identity is a single ref string with its variation folded in — the string
/// equivalent of how the voxel layer packs variation into the block word (VoxelOperators9).
/// Variation 0 is the bare ref (so pre-variation content, and every block that has no
/// variations, is unchanged); variations 1-7 append "#n".
///
/// Nothing above the voxel seam needs to know this format. To everything else — the RPG,
/// trade, quests, the hotbar, the inventory UI — a block ref is one opaque string, and
/// two refs that differ only in variation are simply two unrelated blocks. Combine at the
/// point a voxel is harvested; Split only when a voxel must be rebuilt for placement.
/// </summary>
public static class BlockRefVariation
{
    /// <summary>Separates the base ref from its variation index in a combined ref.</summary>
    public const char Delimiter = '#';

    /// <summary>
    /// Folds a base ref and variation into the combined identity ref. Variation 0 returns the
    /// bare ref so blocks without variations (and all legacy content) keep their original ref.
    /// </summary>
    public static string Combine(string baseRef, byte variation)
        => variation == 0 ? baseRef : string.Concat(baseRef, Delimiter, variation.ToString());

    /// <summary>
    /// Splits a combined identity ref back into (base ref, variation) for the voxel seam.
    /// A ref with no valid "#n" suffix is treated as the whole base ref at variation 0.
    /// </summary>
    public static (string BaseRef, byte Variation) Split(string combinedRef)
    {
        if (!string.IsNullOrEmpty(combinedRef))
        {
            var i = combinedRef.LastIndexOf(Delimiter);
            if (i > 0 && i < combinedRef.Length - 1
                && byte.TryParse(combinedRef.Substring(i + 1), out var v) && v is > 0 and <= 7)
            {
                return (combinedRef.Substring(0, i), v);
            }
        }

        return (combinedRef, 0);
    }

    /// <summary>The base ref with any variation suffix stripped (safe to hand to a block lookup).</summary>
    public static string BaseRef(string combinedRef) => Split(combinedRef).BaseRef;

    /// <summary>The variation encoded in a combined ref (0 when none).</summary>
    public static byte VariationOf(string combinedRef) => Split(combinedRef).Variation;
}
