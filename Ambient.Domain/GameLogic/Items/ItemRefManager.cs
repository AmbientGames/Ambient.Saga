namespace Ambient.Domain.GameLogic.Items;

/// <summary>
/// The one place the "variant folded into an item ref" format is written or read. An item's
/// inventory identity is a single ref with its variant folded in: variant 0 is the bare ref (so
/// items with no variants, and all existing content, are unchanged); variants 1+ append "#n". Use
/// <see cref="Combine"/> to fold a variant in, and <see cref="Split"/> / <see cref="BaseRef"/> /
/// <see cref="VariantOf"/> to pull it back out.
///
/// A general item concept — any <see cref="Contracts.ITradeable"/> may have variants
/// (a wool colour, a sword skin), not a block or voxel concept. How a variant is realised (a block's
/// texture, an item's skin) lives below this layer; here it is just an index.
/// </summary>
public static class ItemRefManager
{
    /// <summary>Separates the base ref from its variant index in a combined ref.</summary>
    public const char Delimiter = '#';

    /// <summary>
    /// Folds a base ref and variant index into the combined identity ref. Variant 0 returns the
    /// bare ref so items without variants (and all existing content) keep their original ref.
    /// </summary>
    public static string Combine(string baseRef, int variant)
        => variant == 0 ? baseRef : string.Concat(baseRef, Delimiter, variant.ToString());

    /// <summary>
    /// Splits a combined identity ref into (base ref, variant index). A ref with no valid "#n"
    /// suffix is treated as the whole base ref at variant 0.
    /// </summary>
    public static (string BaseRef, int Variant) Split(string combinedRef)
    {
        if (!string.IsNullOrEmpty(combinedRef))
        {
            var i = combinedRef.LastIndexOf(Delimiter);
            if (i > 0 && i < combinedRef.Length - 1
                && int.TryParse(combinedRef.Substring(i + 1), out var v) && v > 0)
            {
                return (combinedRef.Substring(0, i), v);
            }
        }

        return (combinedRef, 0);
    }

    /// <summary>The base ref with any variant suffix stripped (safe to hand to a catalog lookup).</summary>
    public static string BaseRef(string combinedRef) => Split(combinedRef).BaseRef;

    /// <summary>The variant index encoded in a combined ref (0 when none).</summary>
    public static int VariantOf(string combinedRef) => Split(combinedRef).Variant;
}
