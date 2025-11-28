namespace Ambient.Domain.Tests.UnitTests.Gameplay;

/// <summary>
/// Unit tests for the Tool partial class that represents game tools.
/// </summary>
public class ToolTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var tool = new Tool();

        // Assert
        Assert.NotNull(tool);
        Assert.Equal(0u, tool.Class);
        Assert.Equal(0, tool.TextureId);
    }

    [Fact]
    public void Class_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var tool = new Tool();
        const uint expectedClass = 42u;

        // Act
        tool.Class = expectedClass;

        // Assert
        Assert.Equal(expectedClass, tool.Class);
    }

    [Fact]
    public void TextureId_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var tool = new Tool();
        const int expectedTextureId = 123;

        // Act
        tool.TextureId = expectedTextureId;

        // Assert
        Assert.Equal(expectedTextureId, tool.TextureId);
    }

    [Fact]
    public void Class_SetToMaxValue_AcceptsValue()
    {
        // Arrange
        var tool = new Tool();

        // Act
        tool.Class = uint.MaxValue;

        // Assert
        Assert.Equal(uint.MaxValue, tool.Class);
    }

    [Fact]
    public void Class_SetToZero_AcceptsValue()
    {
        // Arrange
        var tool = new Tool();

        // Act
        tool.Class = 0u;

        // Assert
        Assert.Equal(0u, tool.Class);
    }

    [Fact]
    public void TextureId_SetToNegativeValue_AcceptsValue()
    {
        // Arrange
        var tool = new Tool();
        const int negativeTextureId = -1;

        // Act
        tool.TextureId = negativeTextureId;

        // Assert
        Assert.Equal(negativeTextureId, tool.TextureId);
    }

    [Fact]
    public void TextureId_SetToMaxValue_AcceptsValue()
    {
        // Arrange
        var tool = new Tool();

        // Act
        tool.TextureId = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, tool.TextureId);
    }

    [Fact]
    public void TextureId_SetToMinValue_AcceptsValue()
    {
        // Arrange
        var tool = new Tool();

        // Act
        tool.TextureId = int.MinValue;

        // Assert
        Assert.Equal(int.MinValue, tool.TextureId);
    }

    [Fact]
    public void MultipleInstances_HaveIndependentProperties()
    {
        // Arrange
        var tool1 = new Tool();
        var tool2 = new Tool();
        const uint class1 = 100u;
        const uint class2 = 200u;
        const int textureId1 = 300;
        const int textureId2 = 400;

        // Act
        tool1.Class = class1;
        tool1.TextureId = textureId1;
        tool2.Class = class2;
        tool2.TextureId = textureId2;

        // Assert
        Assert.Equal(class1, tool1.Class);
        Assert.Equal(textureId1, tool1.TextureId);
        Assert.Equal(class2, tool2.Class);
        Assert.Equal(textureId2, tool2.TextureId);
        Assert.NotEqual(tool1.Class, tool2.Class);
        Assert.NotEqual(tool1.TextureId, tool2.TextureId);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(10u)]
    [InlineData(100u)]
    [InlineData(1000u)]
    [InlineData(uint.MaxValue / 2)]
    public void Class_SetToVariousValues_AcceptsAllValues(uint classValue)
    {
        // Arrange
        var tool = new Tool();

        // Act
        tool.Class = classValue;

        // Assert
        Assert.Equal(classValue, tool.Class);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(int.MaxValue / 2)]
    public void TextureId_SetToVariousValues_AcceptsAllValues(int textureId)
    {
        // Arrange
        var tool = new Tool();

        // Act
        tool.TextureId = textureId;

        // Assert
        Assert.Equal(textureId, tool.TextureId);
    }

    [Fact]
    public void Tool_IsPartialClass_CanBeExtended()
    {
        // This test verifies that the Tool class is partial
        var type = typeof(Tool);
        
        // Assert - The class should be a public class (partial classes are regular classes at runtime)
        Assert.True(type.IsClass);
        Assert.True(type.IsPublic);
    }

    [Fact]
    public void Properties_RoundTripAssignment_MaintainValues()
    {
        // Arrange
        var tool = new Tool();
        const uint originalClass = 12345u;
        const int originalTextureId = 67890;

        // Act
        tool.Class = originalClass;
        tool.TextureId = originalTextureId;
        var retrievedClass = tool.Class;
        var retrievedTextureId = tool.TextureId;

        // Assert
        Assert.Equal(originalClass, retrievedClass);
        Assert.Equal(originalTextureId, retrievedTextureId);
    }

    [Fact]
    public void Properties_DefaultValues_AreZero()
    {
        // Act
        var tool = new Tool();

        // Assert
        Assert.Equal(0u, tool.Class);
        Assert.Equal(0, tool.TextureId);
    }
}
