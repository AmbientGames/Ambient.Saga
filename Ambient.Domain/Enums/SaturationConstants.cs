namespace Ambient.Domain.Enums;

/// <summary>
/// Provides constants for color saturation calculations and bit manipulation in the game's visual system.
/// This class defines the limits, masks, and shift values used for encoding and decoding
/// saturation information in packed data structures.
/// </summary>
/// <remarks>
/// SaturationConstants supports a saturation system that uses bit-packed data for efficient storage.
/// The saturation system appears to use 3 bits for saturation values (0-7) and 4 bits for
/// variation countdown values (0-15), allowing for compact representation of color variation
/// and saturation states in the game world.
/// 
/// The bit manipulation constants enable efficient packing and unpacking of multiple
/// values into single byte storage, which is important for memory efficiency in
/// large-scale world data.
/// </remarks>
public class SaturationConstants
{
    /// <summary>
    /// The maximum saturation value that can be represented with 3 bits (0-7).
    /// This defines the upper limit for saturation intensity in the color system.
    /// </summary>
    public const byte MaxSaturation = 7; // 3 bits

    /// <summary>
    /// The maximum variation countdown value that can be represented with 4 bits (0-15).
    /// This defines the upper limit for variation progression or countdown timers.
    /// </summary>
    public const byte MaxVariationCountDown = 15; // 4 bits

    /// <summary>
    /// Bit mask for extracting saturation values from packed byte data.
    /// Corresponds to bits 4-6 (0b01110000) for 3-bit saturation values.
    /// </summary>
    /// <remarks>
    /// This mask isolates the saturation bits when applied with bitwise AND operations,
    /// allowing extraction of saturation values from packed data structures.
    /// </remarks>
    public const byte SaturationMask = 0b01110000; // Mask for 3 bits (bits 5-7)

    /// <summary>
    /// The number of bit positions to right-shift when extracting saturation values.
    /// Used in conjunction with SaturationMask to extract saturation from packed data.
    /// </summary>
    /// <remarks>
    /// After applying the SaturationMask, right-shifting by 4 positions moves the
    /// saturation bits to the least significant positions for normal value interpretation.
    /// </remarks>
    public const byte SaturationShift = 4;
}