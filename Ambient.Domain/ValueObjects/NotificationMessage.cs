namespace Ambient.Domain.ValueObjects;

/// <summary>
/// Represents a notification message.
/// </summary>
public class NotificationMessage
{
    /// <summary>
    /// The unique identifier for this notification message.
    /// </summary>
    public Guid MessageId { get; set; }
    
    /// <summary>
    /// The unique identifier of the source that generated this notification.
    /// </summary>
    public Guid SourceId { get; set; }
    
    /// <summary>
    /// The display name of the notification source.
    /// </summary>
    public string? SourceDisplayName { get; set; }
    
    /// <summary>
    /// The content of the notification message.
    /// </summary>
    public string? Message { get; set; }
}