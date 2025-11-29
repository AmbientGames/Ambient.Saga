using Ambient.Infrastructure.Extensions;
using SkiaSharp;

namespace Ambient.Infrastructure.Tests
{
    public class SKBitmapExtensionsTests : IDisposable
    {
        private readonly string _testDirectory;

        public SKBitmapExtensionsTests()
        {
            // Create a temporary directory for test files
            _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            // Clean up test directory
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Fact]
        public void SaveSKBitmapAsPNG_ToFile_CreatesValidPNGFile()
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var filePath = Path.Combine(_testDirectory, "test.png");

            // Act
            bitmap.SaveSKBitmapAsPNG(filePath);

            // Assert
            Assert.True(File.Exists(filePath));
            var fileInfo = new FileInfo(filePath);
            Assert.True(fileInfo.Length > 0);

            // Verify it's a valid PNG by reading it back
            using var savedBitmap = SKBitmap.Decode(filePath);
            Assert.NotNull(savedBitmap);
            Assert.Equal(bitmap.Width, savedBitmap.Width);
            Assert.Equal(bitmap.Height, savedBitmap.Height);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(75)]
        [InlineData(50)]
        [InlineData(1)]
        public void SaveSKBitmapAsPNG_ToFile_WithDifferentQuality_CreatesFile(int quality)
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var filePath = Path.Combine(_testDirectory, $"test_q{quality}.png");

            // Act
            bitmap.SaveSKBitmapAsPNG(filePath, quality);

            // Assert
            Assert.True(File.Exists(filePath));
            var fileInfo = new FileInfo(filePath);
            Assert.True(fileInfo.Length > 0);
        }

        [Fact]
        public void SaveSKBitmapAsPNG_ToByteArray_ReturnsValidPNGBytes()
        {
            // Arrange
            using var bitmap = CreateTestBitmap();

            // Act
            byte[] pngBytes = bitmap.SaveSKBitmapAsPNG();

            // Assert
            Assert.NotNull(pngBytes);
            Assert.True(pngBytes.Length > 0);

            // Verify PNG magic bytes (first 8 bytes of PNG format)
            byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (var i = 0; i < pngSignature.Length; i++)
            {
                Assert.Equal(pngSignature[i], pngBytes[i]);
            }

            // Verify it can be decoded back to a bitmap
            using var decodedBitmap = SKBitmap.Decode(pngBytes);
            Assert.NotNull(decodedBitmap);
            Assert.Equal(bitmap.Width, decodedBitmap.Width);
            Assert.Equal(bitmap.Height, decodedBitmap.Height);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(75)]
        [InlineData(50)]
        [InlineData(1)]
        public void SaveSKBitmapAsPNG_ToByteArray_WithDifferentQuality_ReturnsValidBytes(int quality)
        {
            // Arrange
            using var bitmap = CreateTestBitmap();

            // Act
            byte[] pngBytes = bitmap.SaveSKBitmapAsPNG(quality);

            // Assert
            Assert.NotNull(pngBytes);
            Assert.True(pngBytes.Length > 0);

            // Verify it can be decoded
            using var decodedBitmap = SKBitmap.Decode(pngBytes);
            Assert.NotNull(decodedBitmap);
        }

        [Fact]
        public void SaveSKBitmapAsPNG_FileAndByteArrayMethods_ProduceSimilarResults()
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var filePath = Path.Combine(_testDirectory, "comparison.png");

            // Act
            bitmap.SaveSKBitmapAsPNG(filePath);
            byte[] bytesFromMethod = bitmap.SaveSKBitmapAsPNG();
            var bytesFromFile = File.ReadAllBytes(filePath);

            // Assert
            // The byte arrays should be identical since both use the same encoding
            Assert.Equal(bytesFromFile.Length, bytesFromMethod.Length);
            Assert.Equal(bytesFromFile, bytesFromMethod);
        }        private static SKBitmap CreateTestBitmap()
        {
            var bitmap = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            
            // Fill with a simple pattern
            canvas.Clear(SKColors.Red);
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(2, 2, 6, 6, paint);
            
            return bitmap;
        }
    }
}
