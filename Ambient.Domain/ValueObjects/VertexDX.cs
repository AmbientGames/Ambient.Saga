using SharpDX;

namespace Ambient.Domain.ValueObjects;

/// <summary>
/// Represents a 3D vertex with position, texture coordinates, light mapping, and ownership information.
/// </summary>
[Serializable]
public struct VertexDX
{
    /// <summary>
    /// The 3D position of the vertex.
    /// </summary>
    public Vector3 Position;
    
    /// <summary>
    /// The primary texture coordinates (UV mapping).
    /// </summary>
    public Vector2UV UVTexture;
    
    /// <summary>
    /// The light map texture coordinates.
    /// </summary>
    public Vector2UV UVTextureLightMap;
    
    /// <summary>
    /// Index into the ownership texture array.
    /// </summary>
    public uint OwnershipTextureIndex;    
    /// <summary>
    /// Determines whether the specified object is equal to this vertex.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if the specified object is equal; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is VertexDX && this == (VertexDX)obj;
    }

    /// <summary>
    /// Returns the hash code for this vertex.
    /// </summary>
    /// <returns>A hash code for the current vertex.</returns>
    public override int GetHashCode()
    {
        return (int)(Position.X * 56779) ^ (int)(Position.Z * 77573) ^ (int)(Position.Y * 87011) ^ (int)(UVTexture.U * 94063) ^ (int)(UVTextureLightMap.V * 104513) ^ (int)(OwnershipTextureIndex * 114967);
    }

    /// <summary>
    /// Determines whether two vertices are equal.
    /// </summary>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second vertex.</param>
    /// <returns>True if the vertices are equal; otherwise, false.</returns>
    public static bool operator ==(VertexDX a, VertexDX b)
    {
        const float Epsilon = .001f;

        return Math.Abs(a.UVTexture.U - b.UVTexture.U) < Epsilon && Math.Abs(a.UVTexture.V - b.UVTexture.V) < Epsilon && Math.Abs(a.Position.X - b.Position.X) < Epsilon && Math.Abs(a.Position.Y - b.Position.Y) < Epsilon && Math.Abs(a.Position.Z - b.Position.Z) < Epsilon && Math.Abs(a.UVTextureLightMap.U - b.UVTextureLightMap.U) < Epsilon && Math.Abs(a.UVTextureLightMap.V - b.UVTextureLightMap.V) < Epsilon && a.OwnershipTextureIndex == b.OwnershipTextureIndex;
    }

    /// <summary>
    /// Determines whether two vertices are not equal.
    /// </summary>
    /// <param name="x">The first vertex.</param>
    /// <param name="y">The second vertex.</param>
    /// <returns>True if the vertices are not equal; otherwise, false.</returns>
    public static bool operator !=(VertexDX x, VertexDX y)
    {
        return !(x == y);
    }
}