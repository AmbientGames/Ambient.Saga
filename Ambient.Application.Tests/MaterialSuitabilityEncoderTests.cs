using Ambient.Domain;
using Ambient.Domain.GameLogic.Gameplay.Avatar;

namespace Ambient.Application.Tests
{
    public class MaterialSuitabilityEncoderTests
    {
        [Theory]
        [InlineData(SubstanceType.Stone, 1u)]
        [InlineData(SubstanceType.Concrete, 2u)]
        [InlineData(SubstanceType.Wood, 4u)]
        [InlineData(SubstanceType.Structural, 8u)]
        [InlineData(SubstanceType.Decorative, 16u)]
        [InlineData(SubstanceType.Metal, 32u)]
        [InlineData(SubstanceType.Alloy, 64u)]
        [InlineData(SubstanceType.Aggregate, 128u)]
        [InlineData(SubstanceType.Plant, 256u)]
        [InlineData(SubstanceType.Liquid, 512u)]
        [InlineData(SubstanceType.Ore, 1024u)]
        [InlineData(SubstanceType.Carbon, 2048u)]
        [InlineData(SubstanceType.Miscellaneous, 32768u)]
        public void Encode_KnownMaterial_ReturnsCorrectBitValue(SubstanceType substance, uint expectedBits)
        {
            // Act
            uint actualBits = SubstanceSuitabilityEncoder.Encode(substance);

            // Assert
            Assert.Equal(expectedBits, actualBits);
        }

        [Fact]
        public void Encode_AllSubstancesHaveUniqueBitValues()
        {
            var encodedValues = new HashSet<uint>();

            // Act & Assert
            foreach (SubstanceType substance in Enum.GetValues<SubstanceType>())
            {
                uint encoded = SubstanceSuitabilityEncoder.Encode(substance);

                // Each substance should have a unique bit value
                Assert.True(encodedValues.Add(encoded), $"Substance '{substance}' has duplicate bit value {encoded}");

                // Each value should be a power of 2 (single bit set)
                Assert.True(IsPowerOfTwo(encoded), $"Substance '{substance}' bit value {encoded} is not a power of 2");
            }

            // The full vocabulary fits the 16-bit suitability mask
            Assert.Equal(16, encodedValues.Count);
            Assert.True(encodedValues.All(v => v <= 1u << 15), "A substance bit exceeds the 16-bit suitability mask");
        }

        [Fact]
        public void Encode_BitValues_CanBeCombinedWithBitwiseOR()
        {
            // Arrange
            uint woodBits = SubstanceSuitabilityEncoder.Encode(SubstanceType.Wood);
            uint metalBits = SubstanceSuitabilityEncoder.Encode(SubstanceType.Metal);
            uint stoneBits = SubstanceSuitabilityEncoder.Encode(SubstanceType.Stone);

            // Act
            uint combined = woodBits | metalBits | stoneBits;

            // Assert
            // Combined value should have all three bits set
            Assert.True((combined & woodBits) == woodBits, "Wood bit not set in combined value");
            Assert.True((combined & metalBits) == metalBits, "Metal bit not set in combined value");
            Assert.True((combined & stoneBits) == stoneBits, "Stone bit not set in combined value");

            // Combined should equal the sum since they're unique powers of 2
            Assert.Equal(woodBits + metalBits + stoneBits, combined);
        }

        [Fact]
        public void Encode_BitwiseOperations_WorkCorrectly()
        {
            // Arrange
            uint carbon = SubstanceSuitabilityEncoder.Encode(SubstanceType.Carbon);
            uint plant = SubstanceSuitabilityEncoder.Encode(SubstanceType.Plant);

            // Act
            uint combined = carbon | plant;

            // Assert - Test that we can check for individual materials in the combined value
            Assert.True((combined & carbon) != 0, "Carbon should be present in combined value");
            Assert.True((combined & plant) != 0, "Plant should be present in combined value");

            // Test that other materials are not present
            uint wood = SubstanceSuitabilityEncoder.Encode(SubstanceType.Wood);
            Assert.True((combined & wood) == 0, "Wood should not be present in combined value");
        }

        private static bool IsPowerOfTwo(uint value)
        {
            return value > 0 && (value & value - 1) == 0;
        }
    }
}
