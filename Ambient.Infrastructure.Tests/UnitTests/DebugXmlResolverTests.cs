using Ambient.Infrastructure.Utilities;
using System.Xml;

namespace Ambient.Infrastructure.Tests.UnitTests;

/// <summary>
/// Unit tests for the DebugXmlResolver class that extends XmlUrlResolver for debugging XML operations.
/// </summary>
public class DebugXmlResolverTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var resolver = new DebugXmlResolver();

        // Assert
        Assert.NotNull(resolver);
        Assert.IsType<DebugXmlResolver>(resolver);
    }

    [Fact]
    public void DebugXmlResolver_InheritsFromXmlUrlResolver()
    {
        // Arrange
        var resolver = new DebugXmlResolver();

        // Act & Assert
        Assert.IsAssignableFrom<XmlUrlResolver>(resolver);
    }

    [Fact]
    public void DebugXmlResolver_IsPublicClass()
    {
        // Arrange
        var type = typeof(DebugXmlResolver);

        // Act & Assert
        Assert.True(type.IsClass);
        Assert.True(type.IsPublic);
        Assert.False(type.IsAbstract);
        Assert.False(type.IsSealed);
    }

    [Fact]
    public void ResolveUri_WithValidBaseAndRelative_DoesNotThrow()
    {
        // Arrange
        var resolver = new DebugXmlResolver();
        var baseUri = new Uri("file:///C:/test/");
        var relativeUri = "test.xml";

        // Act & Assert
        var exception = Record.Exception(() => resolver.ResolveUri(baseUri, relativeUri));
        Assert.Null(exception);
    }

    [Fact]
    public void ResolveUri_WithNullBase_HandlesGracefully()
    {
        // Arrange
        var resolver = new DebugXmlResolver();
        Uri? baseUri = null;
        var relativeUri = "test.xml";

        // Act & Assert
        var exception = Record.Exception(() => resolver.ResolveUri(baseUri, relativeUri));
        // Should not throw for this simple test case
        Assert.Null(exception);
    }
}
