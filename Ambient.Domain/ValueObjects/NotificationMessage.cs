namespace Ambient.Domain.ValueObjects;

/// <summary>
/// Severity level for notification messages.
/// Maps to MessageType in the UI layer for color-coding.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>General information (white)</summary>
    Info,
    /// <summary>Warning message (yellow) - temperature warnings, low resources</summary>
    Warning,
    /// <summary>Error/critical message (red) - death, critical damage</summary>
    Error,
    /// <summary>Combat narration (orange)</summary>
    Combat,
    /// <summary>Quest update (cyan)</summary>
    Quest,
    /// <summary>Loot/reward (green)</summary>
    Loot
}

/// <summary>
/// Represents a notification message for the toast overlay system.
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

    /// <summary>
    /// The severity/type of the message for visual styling.
    /// </summary>
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    /// <summary>
    /// How long to display the message in seconds.
    /// </summary>
    public float Duration { get; set; } = 4f;
}