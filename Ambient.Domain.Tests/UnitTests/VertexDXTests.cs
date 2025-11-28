using Ambient.Domain.ValueObjects;
using SharpDX;

namespace Ambient.Domain.Tests.UnitTests;

/// <summary>
/// Unit tests for the VertexDX struct that represents 3D vertices with texture and ownership information.
/// </summary>
public class VertexDXTests
{
    [Fact]
    public void Constructor_DefaultValues_CreatesValidVertex()
    {
        // Act
        var vertex = new VertexDX();

        // Assert
        Assert.Equal(Vector3.Zero, vertex.Position);
        Assert.Equal(default(Vector2UV), vertex.UVTexture);
        Assert.Equal(default(Vector2UV), vertex.UVTextureLightMap);
        Assert.Equal(0u, vertex.OwnershipTextureIndex);
    }

    [Fact]
    public void Constructor_WithValues_SetsPropertiesCorrectly()
    {
        // Arrange
        var position = new Vector3(1.0f, 2.0f, 3.0f);
        var uvTexture = new Vector2UV { U = 0.5f, V = 0.7f };
        var uvLightMap = new Vector2UV { U = 0.2f, V = 0.8f };
        const uint ownershipIndex = 42;

        // Act
        var vertex = new VertexDX
        {
            Position = position,
            UVTexture = uvTexture,
            UVTextureLightMap = uvLightMap,
            OwnershipTextureIndex = ownershipIndex
        };

        // Assert
        Assert.Equal(position, vertex.Position);
        Assert.Equal(uvTexture, vertex.UVTexture);
        Assert.Equal(uvLightMap, vertex.UVTextureLightMap);
        Assert.Equal(ownershipIndex, vertex.OwnershipTextureIndex);
    }

    [Fact]
    public void Equals_SameVertices_ReturnsTrue()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();

        // Act & Assert
        Assert.True(vertex1.Equals(vertex2));
        Assert.True(vertex1 == vertex2);
        Assert.False(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_DifferentPositions_ReturnsFalse()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        vertex2.Position = new Vector3(999.0f, 999.0f, 999.0f);

        // Act & Assert
        Assert.False(vertex1.Equals(vertex2));
        Assert.False(vertex1 == vertex2);
        Assert.True(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_DifferentUVTexture_ReturnsFalse()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        vertex2.UVTexture = new Vector2UV { U = 0.999f, V = 0.999f };

        // Act & Assert
        Assert.False(vertex1.Equals(vertex2));
        Assert.False(vertex1 == vertex2);
        Assert.True(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_DifferentUVLightMap_ReturnsFalse()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        vertex2.UVTextureLightMap = new Vector2UV { U = 0.999f, V = 0.999f };

        // Act & Assert
        Assert.False(vertex1.Equals(vertex2));
        Assert.False(vertex1 == vertex2);
        Assert.True(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_DifferentOwnershipIndex_ReturnsFalse()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        vertex2.OwnershipTextureIndex = 999;

        // Act & Assert
        Assert.False(vertex1.Equals(vertex2));
        Assert.False(vertex1 == vertex2);
        Assert.True(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_WithinEpsilon_ReturnsTrue()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        
        // Modify values within epsilon tolerance (0.001)
        vertex2.Position = new Vector3(
            vertex1.Position.X + 0.0005f,
            vertex1.Position.Y + 0.0005f,
            vertex1.Position.Z + 0.0005f);
        vertex2.UVTexture = new Vector2UV 
        { 
            U = vertex1.UVTexture.U + 0.0005f, 
            V = vertex1.UVTexture.V + 0.0005f 
        };
        vertex2.UVTextureLightMap = new Vector2UV 
        { 
            U = vertex1.UVTextureLightMap.U + 0.0005f, 
            V = vertex1.UVTextureLightMap.V + 0.0005f 
        };

        // Act & Assert
        Assert.True(vertex1 == vertex2);
        Assert.True(vertex1.Equals(vertex2));
        Assert.False(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_OutsideEpsilon_ReturnsFalse()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        
        // Modify values outside epsilon tolerance (0.001)
        vertex2.Position = new Vector3(
            vertex1.Position.X + 0.002f,
            vertex1.Position.Y,
            vertex1.Position.Z);

        // Act & Assert
        Assert.False(vertex1 == vertex2);
        Assert.False(vertex1.Equals(vertex2));
        Assert.True(vertex1 != vertex2);
    }

    [Fact]
    public void Equals_WithNullObject_ReturnsFalse()
    {
        // Arrange
        var vertex = CreateTestVertex();

        // Act & Assert
        Assert.False(vertex.Equals(null));
    }

    [Fact]
    public void Equals_WithDifferentType_ReturnsFalse()
    {
        // Arrange
        var vertex = CreateTestVertex();
        var otherObject = "not a vertex";

        // Act & Assert
        Assert.False(vertex.Equals(otherObject));
    }

    [Fact]
    public void GetHashCode_SameVertices_ReturnsSameHashCode()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();

        // Act
        var hash1 = vertex1.GetHashCode();
        var hash2 = vertex2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_DifferentVertices_ReturnsDifferentHashCodes()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        vertex2.Position = new Vector3(999.0f, 999.0f, 999.0f);

        // Act
        var hash1 = vertex1.GetHashCode();
        var hash2 = vertex2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_ConsistentResults_SameVertexSameHash()
    {
        // Arrange
        var vertex = CreateTestVertex();

        // Act
        var hash1 = vertex.GetHashCode();
        var hash2 = vertex.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesData()
    {
        // Arrange
        var originalVertex = CreateTestVertex();

        // Act - Simulate serialization/deserialization by copying values
        var serializedVertex = new VertexDX
        {
            Position = originalVertex.Position,
            UVTexture = originalVertex.UVTexture,
            UVTextureLightMap = originalVertex.UVTextureLightMap,
            OwnershipTextureIndex = originalVertex.OwnershipTextureIndex
        };

        // Assert
        Assert.Equal(originalVertex, serializedVertex);
        Assert.True(originalVertex == serializedVertex);
    }

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(1.0f, 1.0f, 1.0f)]
    [InlineData(-1.0f, -1.0f, -1.0f)]
    [InlineData(float.MaxValue, float.MaxValue, float.MaxValue)]
    [InlineData(float.MinValue, float.MinValue, float.MinValue)]
    public void Position_EdgeCases_HandledCorrectly(float x, float y, float z)
    {
        // Arrange
        var position = new Vector3(x, y, z);

        // Act
        var vertex = new VertexDX { Position = position };

        // Assert
        Assert.Equal(position, vertex.Position);
    }

    [Theory]
    [InlineData(0.0f, 0.0f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(-1.0f, -1.0f)]
    [InlineData(10.0f, 10.0f)] // UV coordinates can exceed 0-1 range for tiling
    public void UVCoordinates_EdgeCases_HandledCorrectly(float u, float v)
    {
        // Arrange
        var uvTexture = new Vector2UV { U = u, V = v };
        var uvLightMap = new Vector2UV { U = u * 0.5f, V = v * 0.5f };

        // Act
        var vertex = new VertexDX 
        { 
            UVTexture = uvTexture,
            UVTextureLightMap = uvLightMap
        };

        // Assert
        Assert.Equal(uvTexture, vertex.UVTexture);
        Assert.Equal(uvLightMap, vertex.UVTextureLightMap);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void OwnershipTextureIndex_EdgeCases_HandledCorrectly(uint index)
    {
        // Arrange & Act
        var vertex = new VertexDX { OwnershipTextureIndex = index };

        // Assert
        Assert.Equal(index, vertex.OwnershipTextureIndex);
    }

    [Fact]
    public void ValueSemantics_Assignment_CreatesIndependentCopy()
    {
        // Arrange
        var vertex1 = CreateTestVertex();

        // Act
        var vertex2 = vertex1; // Value type assignment
        vertex2.Position = new Vector3(999.0f, 999.0f, 999.0f);

        // Assert
        Assert.NotEqual(vertex1.Position, vertex2.Position);
        Assert.NotEqual(vertex1, vertex2);
    }

    [Fact]
    public void EqualityOperators_Reflexive_VertexEqualsItself()
    {
        // Arrange
        var vertex = CreateTestVertex();

        // Act & Assert
        Assert.True(vertex == vertex);
        Assert.True(vertex.Equals(vertex));
        Assert.False(vertex != vertex);
    }

    [Fact]
    public void EqualityOperators_Symmetric_OrderDoesNotMatter()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();

        // Act & Assert
        Assert.Equal(vertex1 == vertex2, vertex2 == vertex1);
        Assert.Equal(vertex1.Equals(vertex2), vertex2.Equals(vertex1));
        Assert.Equal(vertex1 != vertex2, vertex2 != vertex1);
    }

    [Fact]
    public void EqualityOperators_Transitive_ConsistentChain()
    {
        // Arrange
        var vertex1 = CreateTestVertex();
        var vertex2 = CreateTestVertex();
        var vertex3 = CreateTestVertex();

        // Act & Assert
        if (vertex1 == vertex2 && vertex2 == vertex3)
        {
            Assert.True(vertex1 == vertex3);
        }    }

    private static VertexDX CreateTestVertex()
    {
        return new VertexDX
        {
            Position = new Vector3(1.5f, 2.5f, 3.5f),
            UVTexture = new Vector2UV { U = 0.3f, V = 0.7f },
            UVTextureLightMap = new Vector2UV { U = 0.1f, V = 0.9f },
            OwnershipTextureIndex = 123
        };
    }
}
