using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Engine.Extensions
{
    /// <summary>
    /// Protects file system access against path traversal via index or metadata names.
    /// </summary>
    public static class PathGuard
    {
        private static readonly Regex NameRegex = new Regex("^[a-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidName(string name) => name != null && NameRegex.IsMatch(name);

        public static string ValidateIndexName(string indexName)
        {
            if (!IsValidName(indexName))
            {
                throw new ArgumentException($"Invalid index name '{indexName}'. Allowed: a-z, 0-9, '_', '-' (max. 64 characters).");
            }
            return indexName;
        }

        public static string ValidateMetadataName(string name)
        {
            if (!IsValidName(name))
            {
                throw new ArgumentException($"Invalid metadata name '{name}'. Allowed: a-z, 0-9, '_', '-' (max. 64 characters).");
            }
            return name;
        }

        /// <summary>
        /// Path.Combine, but guarantees that the result lies strictly below <paramref name="root"/>.
        /// </summary>
        public static string Combine(string root, params string[] parts)
        {
            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            var segments = new string[parts.Length + 1];
            segments[0] = fullRoot;
            Array.Copy(parts, 0, segments, 1, parts.Length);

            var fullPath = Path.GetFullPath(Path.Combine(segments));

            if (!fullPath.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Path is outside of the allowed root directory.");
            }

            return fullPath;
        }
    }
}
