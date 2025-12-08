using Ambient.Domain.ValueObjects;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Serialization;

namespace Ambient.Domain;

/// <summary>
/// Represents the base class for avatar entities.
/// </summary>
public partial class AvatarBase
{
    /// <summary>
    /// The standard eye height for avatars in game units.
    /// </summary>
    public static float StandardEyeHeight = 1.6f;

    /// <summary>
    /// The standard walking speed for avatars in game units per second.
    /// </summary>
    public static float StandardWalkSpeed = 4.3f;

    /// <summary>
    /// Thread-safe collection of pending notification messages for this avatar.
    /// </summary>
    [NonSerialized]
    [XmlIgnore]
    public BlockingCollection<NotificationMessage> PendingMessages = new();

    /// <summary>
    /// The avatar's ID on the world server.
    /// </summary>
    public Guid AvatarId { get; set; }

    /// <summary>
    /// Block ownership quantities keyed by block reference name.
    /// </summary>
    [XmlIgnore]
    public Dictionary<string, int> BlockOwnership { get; set; } = new Dictionary<string, int>();

    /// <summary>
    /// Adds a notification message to the avatar's pending message queue.
    /// </summary>
    /// <param name="message">The notification message to queue.</param>
    public void EnqueMessage(NotificationMessage message)
    {
        if (!PendingMessages.IsAddingCompleted && !PendingMessages.TryAdd(message))
        {
            Debug.WriteLine("server shutting down - maybe send a message?");
        }
    }
}