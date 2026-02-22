namespace Ambient.Domain.Extensions;

/// <summary>
/// Extension methods for AvatarBase.
/// </summary>
public static class AvatarBaseExtensions
{
    /// <summary>
    /// Gets the block ownership count for the specified block.
    /// Returns 0 if the block is not in the dictionary.
    /// </summary>
    /// <param name="avatar">The avatar.</param>
    /// <param name="blockRef">The block reference name.</param>
    /// <returns>The number of blocks owned (can be fractional).</returns>
    public static float GetBlockOwnership(this AvatarBase avatar, string blockRef)
    {
        return avatar.BlockOwnership.GetValueOrDefault(blockRef);
    }

    /// <summary>
    /// Clears all block ownership for this avatar.
    /// Used during respawn to reset inventory.
    /// </summary>
    /// <param name="avatar">The avatar.</param>
    public static void ClearBlockOwnership(this AvatarBase avatar)
    {
        avatar.BlockOwnership.Clear();
    }

    /// <summary>
    /// Adjusts the block ownership for the specified block by the given delta.
    /// Handles the case where the block doesn't exist in the dictionary yet.
    /// </summary>
    /// <param name="avatar">The avatar.</param>
    /// <param name="blockRef">The block reference name.</param>
    /// <param name="delta">The amount to adjust (positive or negative).</param>
    public static void AdjustBlockOwnership(this AvatarBase avatar, string blockRef, float delta)
    {
        avatar.BlockOwnership[blockRef] = avatar.BlockOwnership.GetValueOrDefault(blockRef) + delta;
    }

    /// <summary>
    /// Adjusts the block ownership for the specified block by whole block amounts.
    /// Used for extension block transfers (cache/process blocks).
    /// </summary>
    /// <param name="avatar">The avatar.</param>
    /// <param name="blockRef">The block reference name.</param>
    /// <param name="wholeBlockDelta">The number of whole blocks to adjust (positive or negative).</param>
    public static void AdjustBlockOwnership(this AvatarBase avatar, string blockRef, int wholeBlockDelta)
    {
        avatar.BlockOwnership[blockRef] = avatar.BlockOwnership.GetValueOrDefault(blockRef) + wholeBlockDelta;
    }

    /// <summary>
    /// Sets the block ownership for the specified block to an absolute value.
    /// Used for game mode gifts (Paint/Explore unlimited blocks).
    /// </summary>
    /// <param name="avatar">The avatar.</param>
    /// <param name="blockRef">The block reference name.</param>
    /// <param name="amount">The absolute amount to set.</param>
    public static void SetBlockOwnership(this AvatarBase avatar, string blockRef, float amount)
    {
        avatar.BlockOwnership[blockRef] = amount;
    }
}
