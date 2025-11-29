//using Ambient.Infrastructure.Extensions;
//using System;
//using System.Linq;
//using Xunit;

//namespace Ambient.Infrastructure.Tests.UnitTests
//{
//    public class GuidConversionExtensionsTests
//    {
//        [Fact]
//        public void ConvertToBufferAndBack_SingleGuid_RoundTripSuccessful()
//        {
//            // Arrange
//            var originalGuid = Guid.NewGuid();
//            var guidArray = new[] { originalGuid };

//            // Act
//            byte[] buffer = guidArray.ConvertToBuffer();
//            Guid[] reconstructedGuids = buffer.ConvertToGuidArray(1);

//            // Assert
//            Assert.Single(reconstructedGuids);
//            Assert.Equal(originalGuid, reconstructedGuids[0]);
//        }

//        [Fact]
//        public void ConvertToBufferAndBack_MultipleGuids_RoundTripSuccessful()
//        {
//            // Arrange
//            var originalGuids = new[]
//            {
//                Guid.NewGuid(),
//                Guid.NewGuid(),
//                Guid.NewGuid(),
//                Guid.NewGuid()
//            };

//            // Act
//            byte[] buffer = originalGuids.ConvertToBuffer();
//            Guid[] reconstructedGuids = buffer.ConvertToGuidArray(originalGuids.Length);

//            // Assert
//            Assert.Equal(originalGuids.Length, reconstructedGuids.Length);
//            Assert.Equal(originalGuids, reconstructedGuids);
//        }

//        [Fact]
//        public void ConvertToBuffer_EmptyArray_ReturnsEmptyBuffer()
//        {
//            // Arrange
//            var emptyGuidArray = Array.Empty<Guid>();

//            // Act
//            byte[] buffer = emptyGuidArray.ConvertToBuffer();

//            // Assert
//            Assert.Empty(buffer);
//        }

//        [Fact]
//        public void ConvertToGuidArray_EmptyBuffer_ReturnsEmptyArray()
//        {
//            // Arrange
//            var emptyBuffer = Array.Empty<byte>();

//            // Act
//            Guid[] guidArray = emptyBuffer.ConvertToGuidArray(0);

//            // Assert
//            Assert.Empty(guidArray);
//        }

//        [Fact]
//        public void ConvertToBuffer_SingleGuid_ProducesCorrectSizeBuffer()
//        {
//            // Arrange
//            var guidArray = new[] { Guid.NewGuid() };

//            // Act
//            byte[] buffer = guidArray.ConvertToBuffer();

//            // Assert
//            Assert.Equal(16, buffer.Length); // Each GUID is 16 bytes
//        }

//        [Theory]
//        [InlineData(1, 16)]
//        [InlineData(2, 32)]
//        [InlineData(5, 80)]
//        [InlineData(10, 160)]
//        public void ConvertToBuffer_VariousGuidCounts_ProducesCorrectSizeBuffer(int guidCount, int expectedBufferSize)
//        {
//            // Arrange
//            var guidArray = new Guid[guidCount];
//            for (int i = 0; i < guidCount; i++)
//            {
//                guidArray[i] = Guid.NewGuid();
//            }

//            // Act
//            byte[] buffer = guidArray.ConvertToBuffer();

//            // Assert
//            Assert.Equal(expectedBufferSize, buffer.Length);
//        }

//        [Fact]
//        public void ConvertToGuidArray_WrongBufferSize_ThrowsArgumentException()
//        {
//            // Arrange
//            var buffer = new byte[15]; // 15 bytes is not a valid size for any number of GUIDs (should be multiple of 16)

//            // Act & Assert
//            Assert.Throws<ArgumentException>(() => buffer.ConvertToGuidArray(1));
//        }

//        [Theory]
//        [InlineData(17, 1)] // 17 bytes for 1 GUID (should be 16)
//        [InlineData(30, 2)] // 30 bytes for 2 GUIDs (should be 32)
//        [InlineData(50, 3)] // 50 bytes for 3 GUIDs (should be 48)
//        public void ConvertToGuidArray_IncorrectBufferSizeForGuidCount_ThrowsArgumentException(int bufferSize, int guidCount)
//        {
//            // Arrange
//            var buffer = new byte[bufferSize];

//            // Act & Assert
//            var exception = Assert.Throws<ArgumentException>(() => buffer.ConvertToGuidArray(guidCount));
//            Assert.Contains("Size of guidBuffer does not match", exception.Message);
//        }

//        [Fact]
//        public void ConvertToBufferAndBack_SpecificKnownGuids_MaintainsExactValues()
//        {
//            // Arrange
//            var knownGuids = new[]
//            {
//                Guid.Empty,
//                new Guid("12345678-1234-1234-1234-123456789012"),
//                new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
//                new Guid("00000000-0000-0000-0000-000000000001")
//            };

//            // Act
//            byte[] buffer = knownGuids.ConvertToBuffer();
//            Guid[] reconstructedGuids = buffer.ConvertToGuidArray(knownGuids.Length);

//            // Assert
//            Assert.Equal(knownGuids, reconstructedGuids);
//        }

//        [Fact]
//        public void ConvertToBuffer_OrderPreservation_MaintainsOriginalOrder()
//        {
//            // Arrange
//            var guidArray = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

//            // Act
//            byte[] buffer = guidArray.ConvertToBuffer();
//            Guid[] reconstructedGuids = buffer.ConvertToGuidArray(guidArray.Length);

//            // Assert
//            for (int i = 0; i < guidArray.Length; i++)
//            {
//                Assert.Equal(guidArray[i], reconstructedGuids[i]);
//            }
//        }

//        [Fact]
//        public void ConvertToBuffer_LargeGuidArray_HandlesSuccessfully()
//        {
//            // Arrange
//            const int largeCount = 1000;
//            var largeGuidArray = new Guid[largeCount];
//            for (int i = 0; i < largeCount; i++)
//            {
//                largeGuidArray[i] = Guid.NewGuid();
//            }

//            // Act
//            byte[] buffer = largeGuidArray.ConvertToBuffer();
//            Guid[] reconstructedGuids = buffer.ConvertToGuidArray(largeCount);

//            // Assert
//            Assert.Equal(largeCount * 16, buffer.Length);
//            Assert.Equal(largeGuidArray, reconstructedGuids);
//        }

//        [Fact]
//        public void ConvertToGuidArray_BufferWithValidSize_SucceedsEvenWithZeroGuids()
//        {
//            // Arrange
//            var buffer = Array.Empty<byte>();

//            // Act
//            Guid[] result = buffer.ConvertToGuidArray(0);

//            // Assert
//            Assert.Empty(result);
//        }

//        [Fact]
//        public void ConvertToBuffer_NullArray_ThrowsNullReferenceException()
//        {
//            // Arrange
//            Guid[] nullArray = null;

//            // Act & Assert
//            Assert.Throws<NullReferenceException>(() => nullArray.ConvertToBuffer());
//        }

//        [Fact]
//        public void ConvertToGuidArray_NullBuffer_ThrowsNullReferenceException()
//        {
//            // Arrange
//            byte[] nullBuffer = null;

//            // Act & Assert
//            Assert.Throws<NullReferenceException>(() => nullBuffer.ConvertToGuidArray(1));
//        }
//    }
//}
