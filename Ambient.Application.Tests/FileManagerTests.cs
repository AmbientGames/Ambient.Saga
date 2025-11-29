using System;
using System.IO;
using System.Reflection;
using Xunit;
using Ambient.Application.Utilities;

namespace Ambient.Application.Tests
{
    public class FileManagerTests : IDisposable
    {
        private readonly string tempDir;

        public FileManagerTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            FileManager.ResetSearchPath();
        }

        public void Dispose()
        {
            Directory.Delete(tempDir, true);
            FileManager.ResetSearchPath();
        }

        [Fact]
        public void GetExecutingDirectoryName_ReturnsCurrentDir()
        {
            var expected = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var actual = FileManager.GetExecutingDirectoryName();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void AddSearchPath_AddsSingleAndMultiplePathsCorrectly()
        {
            var path1 = Path.Combine(tempDir, "dir1");
            var path2 = Path.Combine(tempDir, "dir2");
            Directory.CreateDirectory(path1);
            Directory.CreateDirectory(path2);

            FileManager.AddSearchPath(path1 + ";" + path2);

            var testFile1 = Path.Combine(path1, "a.txt");
            var testFile2 = Path.Combine(path2, "b.txt");
            File.WriteAllText(testFile1, "alpha");
            File.WriteAllText(testFile2, "beta");

            var found1 = FileManager.FindFile(testFile1);
            var found2 = FileManager.FindFile(testFile2);

            Assert.True(File.Exists(found1));
            Assert.True(File.Exists(found2));
        }

        [Fact]
        public void FindFile_FindsExistingFileInSearchPath()
        {
            var subDir = Path.Combine(tempDir, "assets");
            Directory.CreateDirectory(subDir);
            var fileName = "example.txt";
            var filePath = Path.Combine(subDir, fileName);
            File.WriteAllText(filePath, "hello");

            FileManager.AddSearchPath(subDir);
            var found = FileManager.FindFile(filePath);
            Assert.True(File.Exists(found));
            Assert.Equal(Path.GetFullPath(filePath), Path.GetFullPath(found));
        }

        // Modified unit test for the new behavior
        [Fact]
        public void FindFile_ReturnsEmptyStringIfNotFound()
        {
            var missingPath = Path.Combine(tempDir, "definitely_not_here.txt");
            var result = FileManager.FindFile(missingPath);
            Assert.Equal(string.Empty, result);
            Assert.False(File.Exists(result));
        }

        [Fact]
        public void FindFile_DoesNotSearchSubdirectories()
        {
            var baseDir = Path.Combine(tempDir, "base");
            var nestedDir = Path.Combine(baseDir, "nested");
            Directory.CreateDirectory(nestedDir);
            var fileName = "nested.txt";
            var nestedFilePath = Path.Combine(nestedDir, fileName);
            File.WriteAllText(nestedFilePath, "data");

            FileManager.AddSearchPath(baseDir);
            var result = FileManager.FindFile(fileName);

            Assert.False(File.Exists(result));
        }

        [Fact]
        public void FindFile_FindsFileInLaterSearchPath()
        {
            var path1 = Path.Combine(tempDir, "first");
            var path2 = Path.Combine(tempDir, "second");
            Directory.CreateDirectory(path1);
            Directory.CreateDirectory(path2);
            var fileName = "target.txt";
            var filePath = Path.Combine(path2, fileName);
            File.WriteAllText(filePath, "content");

            FileManager.AddSearchPath(path1);
            FileManager.AddSearchPath(path2);
            var found = FileManager.FindFile(filePath);
            Assert.True(File.Exists(found));
            Assert.Equal(Path.GetFullPath(filePath), Path.GetFullPath(found));
        }
    }
}
