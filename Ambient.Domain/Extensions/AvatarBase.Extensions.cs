namespace Ambient.Domain.Extensions;

/// <summary>
/// Extension methods for AvatarBase.
/// </summary>
public static class AvatarBaseExtensions
{
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
}
