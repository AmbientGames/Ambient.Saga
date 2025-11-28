namespace Ambient.Domain.Contracts;

/// <summary>
/// Marker interface for block types that can be traded.
/// Implemented by block classes in game-specific domain projects.
/// </summary>
public interface IBlock : ITradeable
{
}
