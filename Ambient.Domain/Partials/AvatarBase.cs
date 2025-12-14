using Ambient.Domain.Enums;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.ValueObjects;
using SharpDX;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Serialization;

namespace Ambient.Domain;

public interface IAvatarBase
{
    int BuildBlocksPlaced { get; set; }
    long GamePlayTime { get; set; }
    int BuildUpVotes { get; set; }
}


/// <summary>
/// Represents the base class for avatar entities.
/// </summary>
public partial class AvatarBase : IAvatarBase
{
    /// <summary>
    /// The standard eye height for avatars in game units.
    /// </summary>
    public static float StandardEyeHeight = 1.6f;

    #region Runtime/Session Fields (moved from XSD - not part of formal schema contract)

    /// <summary>
    /// The currently active affinity reference (session state).
    /// </summary>
    public string? ActiveAffinityRef { get; set; }

    /// <summary>
    /// The currently selected tool reference (session state).
    /// </summary>
    public string? CurrentToolRef { get; set; }

    /// <summary>
    /// The currently selected building material reference (session state).
    /// </summary>
    public string? CurrentBuildingMaterialRef { get; set; }

    /// <summary>
    /// Total play time in hours (runtime metric).
    /// </summary>
    public float PlayTimeHours { get; set; }

    /// <summary>
    /// Total blocks placed during gameplay (runtime metric).
    /// </summary>
    public long BlocksPlaced { get; set; }

    /// <summary>
    /// Total blocks destroyed during gameplay (runtime metric).
    /// </summary>
    public long BlocksDestroyed { get; set; }

    /// <summary>
    /// Total distance traveled during gameplay (runtime metric).
    /// </summary>
    public float DistanceTraveled { get; set; }

    #endregion

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
    /// The ID of the last processed UDP message for this avatar.
    /// </summary>
    [NonSerialized]
    [XmlIgnore]
    public int UdpMessageIdProcessed;

    /// <summary>
    /// The avatar's ID on the world server.
    /// </summary>
    public Guid AvatarId { get; set; }

    /// <summary>
    /// The avatar's position in the world.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// The number of reputation points downloaded.
    /// </summary>
    public int ReputationPointsDownloaded { get; set; }

    /// <summary>
    /// The number of blocks built by the avatar.
    /// </summary>
    public int BuildBlocksPlaced { get; set; }

    /// <summary>
    /// The number of up votes received for builds.
    /// </summary>
    public int BuildUpVotes { get; set; }

    /// <summary>
    /// Indicates if the avatar is invulnerable.
    /// </summary>
    public bool IsInvulnerable { get; set; }

    /// <summary>
    /// The number of chunks owned by the avatar.
    /// </summary>
    public int ChunksOwned { get; set; }

    ///// <summary>
    ///// Saturation levels for blocks owned by the avatar.
    ///// </summary>
    //public int[] BlockOwnershipSaturation { get; set; } = new int[WorldMaximums.MaxBlocks];
    [XmlIgnore]
    public Dictionary<string, float> BlockOwnership { get; set; } = new Dictionary<string, float>();

    /// <summary>
    /// Usage statistics for blocks by the avatar.
    /// </summary>
    public int[] BlockUsage { get; set; } = new int[WorldMaximums.MaxBlocks];

    /// <summary>
    /// The avatar's spawn position.
    /// </summary>
    public Vector3 HomeLocation { get; set; }

    /// <summary>
    /// The avatar's velocity.
    /// </summary>
    public Vector3 Velocity { get; set; }

    /// <summary>
    /// The total gameplay time for the avatar.
    /// </summary>
    public long GamePlayTime { get; set; }

    /// <summary>
    /// The avatar's view direction.
    /// </summary>
    public Vector3 ViewDir { get; set; }

    /// <summary>
    /// The avatar's view up vector.
    /// </summary>
    public Vector3 ViewUp { get; set; }

    /// <summary>
    /// The ID of the world the avatar is in.
    /// </summary>
    public Guid WorldId { get; set; }

    /// <summary>
    /// Decrements the block placement credit for a given block type.
    /// </summary>
    /// <param name="blockType">The type of block.</param>
    public void DecrementPlacementCreditRemaining(ushort blockType) { }

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