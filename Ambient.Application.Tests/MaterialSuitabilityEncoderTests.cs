using Ambient.Domain.GameLogic.Gameplay.Avatar;

namespace Ambient.Application.Tests
{
    public class MaterialSuitabilityEncoderTests
    {
        [Theory]
        [InlineData("Stone", 1u)]
        [InlineData("Concrete", 2u)]
        [InlineData("Wood", 4u)]
        [InlineData("Decorative", 8u)]
        [InlineData("Metal", 16u)]
        [InlineData("Alloy", 32u)]
        [InlineData("Aggregate", 64u)]
        [InlineData("Plant", 128u)]
        [InlineData("Liquid", 256u)]
        [InlineData("Ore", 512u)]
        [InlineData("Carbon", 1024u)]
        [InlineData("Miscellaneous", 32768u)]
        public void Encode_KnownMaterial_ReturnsCorrectBitValue(string material, uint expectedBits)
        {
            // Act
            uint actualBits = SubstanceSuitabilityEncoder.Encode(material);

            // Assert
            Assert.Equal(expectedBits, actualBits);
        }

        [Theory]
        [InlineData("UnknownMaterial")]
        [InlineData("aggregate")] // Case sensitive
        [InlineData("STONE")]     // Case sensitive
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("Wood ")] // Trailing space
        [InlineData(" Wood")] // Leading space
        public void Encode_UnknownOrInvalidMaterial_ReturnsZero(string material)
        {
            // Act
            uint actualBits = SubstanceSuitabilityEncoder.Encode(material);

            // Assert
            Assert.Equal(0u, actualBits);
        }

        [Fact]
        public void Encode_AllMaterialsHaveUniqueBitValues()
        {
            // Arrange
            string[] allMaterials = {
                "Stone", "Concrete", "Wood", "Decorative", "Metal", "Alloy",
                "Aggregate", "Plant", "Liquid", "Ore", "Carbon", "Miscellaneous"
            };

            var encodedValues = new HashSet<uint>();

            // Act & Assert
            foreach (string material in allMaterials)
            {
                uint encoded = SubstanceSuitabilityEncoder.Encode(material);
                
                // Each material should have a unique bit value
                Assert.True(encodedValues.Add(encoded), $"Material '{material}' has duplicate bit value {encoded}");
                
                // Each value should be a power of 2 (single bit set)
                Assert.True(IsPowerOfTwo(encoded), $"Material '{material}' bit value {encoded} is not a power of 2");
            }

            // Verify we have the expected number of unique values
            Assert.Equal(allMaterials.Length, encodedValues.Count);
        }

        [Fact]
        public void Encode_BitValues_CanBeCombinedWithBitwiseOR()
        {
            // Arrange
            uint woodBits = SubstanceSuitabilityEncoder.Encode("Wood");
            uint metalBits = SubstanceSuitabilityEncoder.Encode("Metal");
            uint stoneBits = SubstanceSuitabilityEncoder.Encode("Stone");

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
            uint carbon = SubstanceSuitabilityEncoder.Encode("Carbon");
            uint plant = SubstanceSuitabilityEncoder.Encode("Plant");

            // Act
            uint combined = carbon | plant;

            // Assert - Test that we can check for individual materials in the combined value
            Assert.True((combined & carbon) != 0, "Carbon should be present in combined value");
            Assert.True((combined & plant) != 0, "Plant should be present in combined value");
            
            // Test that other materials are not present
            uint wood = SubstanceSuitabilityEncoder.Encode("Wood");
            Assert.True((combined & wood) == 0, "Wood should not be present in combined value");
        }        private static bool IsPowerOfTwo(uint value)
        {
            return value > 0 && (value & value - 1) == 0;
        }
    }
}
