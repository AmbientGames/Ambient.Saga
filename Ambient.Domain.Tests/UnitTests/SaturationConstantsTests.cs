using Ambient.Domain.Enums;
using Xunit;

namespace Ambient.Domain.Tests.UnitTests;

/// <summary>
/// Unit tests for the SaturationConstants class and its associated functionality.
/// </summary>
public class SaturationConstantsTests
{
    [Fact]
    public void SaturationConstants_MaxSaturation_ShouldBe7()
    {
        // Assert
        Assert.Equal(7, SaturationConstants.MaxSaturation);
    }

    [Fact]
    public void SaturationConstants_MaxVariationCountDown_ShouldBe15()
    {
        // Assert
        Assert.Equal(15, SaturationConstants.MaxVariationCountDown);
    }

    [Fact]
    public void SaturationConstants_SaturationMask_ShouldBeCorrectBitPattern()
    {
        // Assert
        Assert.Equal(0b01110000, SaturationConstants.SaturationMask);
        Assert.Equal(112, SaturationConstants.SaturationMask); // 0b01110000 = 112
    }

    [Fact]
    public void SaturationConstants_SaturationShift_ShouldBe4()
    {
        // Assert
        Assert.Equal(4, SaturationConstants.SaturationShift);
    }

    [Fact]
    public void SaturationConstants_MaxSaturation_ShouldFitIn3Bits()
    {
        // Assert - Max value for 3 bits is 2^3 - 1 = 7
        Assert.True(SaturationConstants.MaxSaturation <= 7);
        Assert.Equal(7, SaturationConstants.MaxSaturation);
    }

    [Fact]
    public void SaturationConstants_MaxVariationCountDown_ShouldFitIn4Bits()
    {
        // Assert - Max value for 4 bits is 2^4 - 1 = 15
        Assert.True(SaturationConstants.MaxVariationCountDown <= 15);
        Assert.Equal(15, SaturationConstants.MaxVariationCountDown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void SaturationConstants_SaturationMaskAndShift_ShouldExtractCorrectValues(byte saturationValue)
    {
        // Arrange - Pack saturation value into the correct bit positions
        byte packedValue = (byte)(saturationValue << SaturationConstants.SaturationShift);

        // Act - Extract saturation using mask and shift
        byte extractedValue = (byte)((packedValue & SaturationConstants.SaturationMask) >> SaturationConstants.SaturationShift);

        // Assert
        Assert.Equal(saturationValue, extractedValue);
    }

    [Fact]
    public void SaturationConstants_BitManipulation_ShouldWorkWithCombinedValues()
    {
        // Arrange - Create a byte with both saturation (3 bits) and other data (lower 4 bits)
        byte saturation = 5; // 3 bits
        byte otherData = 10; // 4 bits (lower nibble)
        
        // Pack both values into a single byte
        byte packedByte = (byte)((saturation << SaturationConstants.SaturationShift) | otherData);

        // Act - Extract saturation
        byte extractedSaturation = (byte)((packedByte & SaturationConstants.SaturationMask) >> SaturationConstants.SaturationShift);
        byte extractedOtherData = (byte)(packedByte & 0x0F); // Lower 4 bits

        // Assert
        Assert.Equal(saturation, extractedSaturation);
        Assert.Equal(otherData, extractedOtherData);
    }

    [Fact]
    public void SaturationConstants_SaturationMask_ShouldIsolateCorrectBits()
    {
        // Arrange - Byte with all bits set
        byte allBitsSet = 0xFF; // 11111111

        // Act - Apply saturation mask
        byte maskedValue = (byte)(allBitsSet & SaturationConstants.SaturationMask);

        // Assert - Should isolate only the saturation bits (01110000)
        Assert.Equal(0b01110000, maskedValue);
    }
}
