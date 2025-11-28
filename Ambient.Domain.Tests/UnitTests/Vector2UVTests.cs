using Ambient.Domain.ValueObjects;

namespace Ambient.Domain.Tests.UnitTests;

/// <summary>
/// Unit tests for the Vector2UV struct that represents 2D UV texture coordinates.
/// </summary>
public class Vector2UVTests
{
    [Fact]
    public void Constructor_WithFloatParameters_SetsProperties()
    {
        // Arrange
        var u = 0.5f;
        var v = 0.75f;

        // Act
        var vector = new Vector2UV(u, v);

        // Assert
        Assert.Equal(u, vector.U);
        Assert.Equal(v, vector.V);
    }

    [Fact]
    public void Constructor_Default_HasZeroValues()
    {
        // Act
        var vector = new Vector2UV();

        // Assert
        Assert.Equal(0.0f, vector.U);
        Assert.Equal(0.0f, vector.V);
    }

    [Fact]
    public void Properties_CanBeSetAndGet()
    {
        // Arrange
        var vector = new Vector2UV();
        var expectedU = 0.25f;
        var expectedV = 0.95f;

        // Act
        vector.U = expectedU;
        vector.V = expectedV;

        // Assert
        Assert.Equal(expectedU, vector.U);
        Assert.Equal(expectedV, vector.V);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var vector1 = new Vector2UV(0.1f, 0.2f);
        var vector2 = new Vector2UV(0.1f, 0.2f);

        // Act & Assert
        Assert.Equal(vector1, vector2);
        Assert.True(vector1.Equals(vector2));
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var vector1 = new Vector2UV(0.1f, 0.2f);
        var vector2 = new Vector2UV(0.1f, 0.3f); // Different V

        // Act & Assert
        Assert.NotEqual(vector1, vector2);
        Assert.False(vector1.Equals(vector2));
    }

    [Fact]
    public void GetHashCode_SameValues_SameHashCode()
    {
        // Arrange
        var vector1 = new Vector2UV(0.5f, 0.5f);
        var vector2 = new Vector2UV(0.5f, 0.5f);

        // Act & Assert
        Assert.Equal(vector1.GetHashCode(), vector2.GetHashCode());
    }

    [Fact]
    public void Vector2UV_IsValueType()
    {
        // Arrange
        var type = typeof(Vector2UV);

        // Act & Assert
        Assert.True(type.IsValueType);
        Assert.False(type.IsClass);
        Assert.False(type.IsInterface);
    }

    [Fact]
    public void Constructor_WithNegativeValues_AcceptsValues()
    {
        // Arrange
        var u = -0.5f;
        var v = -0.25f;

        // Act
        var vector = new Vector2UV(u, v);

        // Assert
        Assert.Equal(u, vector.U);
        Assert.Equal(v, vector.V);
    }

    [Fact]
    public void Constructor_WithLargeValues_AcceptsValues()
    {
        // Arrange
        var u = 10.5f;
        var v = 20.75f;

        // Act
        var vector = new Vector2UV(u, v);

        // Assert
        Assert.Equal(u, vector.U);
        Assert.Equal(v, vector.V);
    }

    [Theory]
    [InlineData(0.0f, 0.0f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(-1.0f, 2.0f)]
    public void Constructor_WithVariousValues_SetsCorrectly(float u, float v)
    {
        // Act
        var vector = new Vector2UV(u, v);

        // Assert
        Assert.Equal(u, vector.U);
        Assert.Equal(v, vector.V);
    }
}
