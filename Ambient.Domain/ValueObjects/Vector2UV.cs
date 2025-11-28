namespace Ambient.Domain.ValueObjects;

/// <summary>
/// Represents a 2D texture coordinate (UV mapping).
/// </summary>
[Serializable]
public struct Vector2UV
{
    /// <summary>
    /// The U coordinate (horizontal texture coordinate).
    /// </summary>
    public float U;

    /// <summary>
    /// The V coordinate (vertical texture coordinate).
    /// </summary>
    public float V;

    /// <summary>
    /// Initializes a new instance of the <see cref="Vector2UV"/> struct.
    /// </summary>
    /// <param name="uIn">The U (horizontal) coordinate.</param>
    /// <param name="vIn">The V (vertical) coordinate.</param>
    public Vector2UV(float uIn, float vIn)
    {
        U = uIn;
        V = vIn;
    }
}