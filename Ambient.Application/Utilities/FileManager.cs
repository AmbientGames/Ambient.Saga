using System.Reflection;

namespace Ambient.Application.Utilities
{
    /// <summary>
    /// Provides utility methods for managing file search paths.
    /// </summary>
    public static class FileManager
    {
        private static readonly List<string> _searchPaths = new List<string>();
        private static readonly HashSet<string> _seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the directory name of the currently executing assembly.
        /// </summary>
        /// <returns>The absolute path of the executing directory.</returns>
        public static string GetExecutingDirectoryName()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly()?.Location ?? AppContext.BaseDirectory) ?? string.Empty;
        }

        /// <summary>
        /// Adds one or more file search paths.
        /// </summary>
        /// <param name="newPaths">A string containing one or more paths, separated by semicolons.</param>
        public static void AddSearchPath(string newPaths)
        {
            if (string.IsNullOrWhiteSpace(newPaths))
            {
                return;
            }

            var individualPaths = newPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var path in individualPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                string absolutePath = Path.GetFullPath(path);

                if (_seenPaths.Add(absolutePath))
                {
                    _searchPaths.Add(absolutePath);
                }
            }
        }

        /// <summary>
        /// Attempts to find a file within the registered search paths.
        /// </summary>
        /// <param name="fileName">The name of the file to find.</param>
        /// <returns>The full absolute path to the file if found; otherwise, <c>string.Empty</c>.</returns>
        public static string FindFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            string absolutePathFromInput = Path.GetFullPath(fileName);
            if (File.Exists(absolutePathFromInput))
            {
                return absolutePathFromInput;
            }

            string justFileName = Path.GetFileName(fileName);

            string execDir = GetExecutingDirectoryName();
            if (!string.IsNullOrWhiteSpace(execDir))
            {
                string candidatePath = Path.Combine(execDir, justFileName);
                if (File.Exists(candidatePath))
                {
                    return Path.GetFullPath(candidatePath);
                }
            }

            foreach (var searchDir in _searchPaths)
            {
                string candidatePath = Path.Combine(searchDir, justFileName);
                if (File.Exists(candidatePath))
                {
                    return Path.GetFullPath(candidatePath);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Resets all registered search paths.
        /// </summary>
        public static void ResetSearchPath()
        {
            _searchPaths.Clear();
            _seenPaths.Clear();
        }
    }
}