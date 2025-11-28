namespace Ambient.Domain.Enums;

/// <summary>
/// Provides fundamental constants used throughout the application.
/// </summary>
public static class Constants
{
    /// <summary>
    /// The port offset for server logging.
    /// </summary>
    public const int ServerLogPortOffset = 10000;

    /// <summary>
    /// The number of milliseconds in one second.
    /// </summary>
    public const int OneSecondMs = 1000;

    /// <summary>
    /// UDP port for broadcasting server availability.
    /// </summary>
    public const int UdpMultiplayerServersAvailableBroadcastPort = 21996;

    /// <summary>
    /// UDP port for server requests.
    /// </summary>
    public const int UdpServerRequestPort = 21998;

    /// <summary>
    /// Conversion factor from degrees to radians (double precision).
    /// </summary>
    public const double DegreesToRadians = Math.PI / 180;

    /// <summary>
    /// Conversion factor from degrees to radians (single precision).
    /// </summary>
    public const float DegreesToRadiansFloat = (float)Math.PI / 180;

    /// <summary>
    /// The maximum capacity of a byte.
    /// </summary>
    public const int ByteCapacity = 256;
    
    /// <summary>
    /// The upper bound of a byte.
    /// </summary>
    public const int ByteUpperBound = ByteCapacity - 1;

    /// <summary>
    /// Stride value for byte-indexed operations.
    /// </summary>
    public const int IndexStrideByte = ByteCapacity;
    
    /// <summary>
    /// Upper bound for byte stride indexing.
    /// </summary>
    public const int IndexStrideByteUpperBound = IndexStrideByte - 1;
}