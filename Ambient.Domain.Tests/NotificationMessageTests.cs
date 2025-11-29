using Ambient.Domain.ValueObjects;
using Xunit;

namespace Ambient.Domain.Tests;

/// <summary>
/// Unit tests for the NotificationMessage class and its associated functionality.
/// </summary>
public class NotificationMessageTests
{
    [Fact]
    public void NotificationMessage_DefaultConstructor_ShouldInitializeWithDefaultValues()
    {
        // Act
        var message = new NotificationMessage();

        // Assert
        Assert.Equal(Guid.Empty, message.MessageId);
        Assert.Equal(Guid.Empty, message.SourceId);
        Assert.Null(message.SourceDisplayName);
        Assert.Null(message.Message);
    }

    [Fact]
    public void NotificationMessage_Properties_ShouldBeSettableAndGettable()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var sourceDisplayName = "TestUser";
        var messageContent = "This is a test notification";

        // Act
        var notification = new NotificationMessage
        {
            MessageId = messageId,
            SourceId = sourceId,
            SourceDisplayName = sourceDisplayName,
            Message = messageContent
        };

        // Assert
        Assert.Equal(messageId, notification.MessageId);
        Assert.Equal(sourceId, notification.SourceId);
        Assert.Equal(sourceDisplayName, notification.SourceDisplayName);
        Assert.Equal(messageContent, notification.Message);
    }

    [Fact]
    public void NotificationMessage_SourceDisplayName_ShouldAcceptNullValue()
    {
        // Arrange
        var notification = new NotificationMessage();

        // Act
        notification.SourceDisplayName = null;

        // Assert
        Assert.Null(notification.SourceDisplayName);
    }

    [Fact]
    public void NotificationMessage_Message_ShouldAcceptNullValue()
    {
        // Arrange
        var notification = new NotificationMessage();

        // Act
        notification.Message = null;

        // Assert
        Assert.Null(notification.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SingleWord")]
    [InlineData("Multiple words with spaces")]
    [InlineData("Special characters: !@#$%^&*()")]
    [InlineData("Numbers: 12345")]
    public void NotificationMessage_SourceDisplayName_ShouldAcceptVariousStringValues(string displayName)
    {
        // Arrange
        var notification = new NotificationMessage();

        // Act
        notification.SourceDisplayName = displayName;

        // Assert
        Assert.Equal(displayName, notification.SourceDisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short message")]
    [InlineData("A very long message that contains multiple sentences and provides detailed information about the notification content.")]
    [InlineData("Message with\nnewlines\nand\ttabs")]
    public void NotificationMessage_Message_ShouldAcceptVariousStringValues(string messageContent)
    {
        // Arrange
        var notification = new NotificationMessage();

        // Act
        notification.Message = messageContent;

        // Assert
        Assert.Equal(messageContent, notification.Message);
    }

    [Fact]
    public void NotificationMessage_GuidsCanBeIndependent()
    {
        // Arrange
        var notification = new NotificationMessage
        {
            MessageId = Guid.NewGuid(),
            SourceId = Guid.NewGuid()
        };

        // Assert
        Assert.NotEqual(notification.MessageId, notification.SourceId);
        Assert.NotEqual(Guid.Empty, notification.MessageId);
        Assert.NotEqual(Guid.Empty, notification.SourceId);
    }

    [Fact]
    public void NotificationMessage_ShouldSupportSystemMessages()
    {
        // Arrange & Act - System message with no source display name
        var systemMessage = new NotificationMessage
        {
            MessageId = Guid.NewGuid(),
            SourceId = Guid.Empty, // System source
            SourceDisplayName = null, // No display name for system
            Message = "System maintenance scheduled"
        };

        // Assert
        Assert.NotEqual(Guid.Empty, systemMessage.MessageId);
        Assert.Equal(Guid.Empty, systemMessage.SourceId);
        Assert.Null(systemMessage.SourceDisplayName);
        Assert.NotNull(systemMessage.Message);
    }
}
