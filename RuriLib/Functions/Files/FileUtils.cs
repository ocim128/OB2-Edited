using RuriLib.Extensions;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RuriLib.Functions.Files
{
    /// <summary>
    /// Provides methods to work with files.
    /// </summary>
    public static class FileUtils
    {
        /// <summary>
        /// Gets the first available name in the given folder by incrementing a number at the end of the filename.
        /// </summary>
        public static string GetFirstAvailableFileName(string fileName)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            // If the file does not exist, the path is already valid.
            if (!File.Exists(fileName))
            {
                return fileName;
            }

            string path;
            var i = 0;
            do
            {
                i++;
                var newName = Path.GetFileNameWithoutExtension(fileName) + i + Path.GetExtension(fileName);
                path = Path.Combine(Path.GetDirectoryName(fileName), newName);
            }
            while (File.Exists(path));

            return path;
        }

        /// <summary>
        /// Fixes the filename to be compatible with the filesystem indicization by replacing all invalid
        /// file name characters with the specified <paramref name="replacement"/>.
        /// </summary>
        public static string ReplaceInvalidFileNameChars(string fileName, string replacement = "_")
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return Regex.Replace(fileName, invalidRegStr, replacement ?? "");
        }

        /// <summary>
        /// Historically this threw if the path was not part of the current working directory.
        /// The restriction has been lifted to allow working with files located outside
        /// of the software directory, so the method now acts as a no-op.
        /// </summary>
        /// <param name="path">The absolute or relative path.</param>
        public static void ThrowIfNotInCWD(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            // Intentionally left blank.
        }

        /// <summary>
        /// Creates the folder structure that contains a certain file if it doesn't already exist.
        /// </summary>
        /// <param name="file">The absolute or relative path to the file.</param>
        public static void CreatePath(string file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            var dirName = Path.GetDirectoryName(file);

            if (string.IsNullOrWhiteSpace(dirName))
                return;

            // Check if a file already exists at the path where we want to create a directory.
            // This prevents an IOException if Directory.CreateDirectory tries to create a directory over an existing file.
            if (File.Exists(dirName))
                throw new IOException($"A file with the name '{dirName}' already exists, preventing directory creation.");

            Directory.CreateDirectory(dirName);
        }
    }
}
