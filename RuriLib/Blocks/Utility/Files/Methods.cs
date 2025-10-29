using RuriLib.Attributes;
using RuriLib.Extensions;
using RuriLib.Functions.Files;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Utility.Files
{
    [BlockCategory("Files", "Blocks for working with files and folders", "#fad6a5")]
    public static class Methods
    {
        [Block("Checks if a file exists")]
        public static async Task<bool> FileExists(BotData data, string path)
        {
            path = SanitizePath(path);
            var exists = await ExecuteFileOperation(data, path, true, (p, c) =>
            {
                return Task.FromResult(File.Exists(p));
            }).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log(path + (exists ? " exists" : " does not exist"), LogColors.Flavescent);
            return exists;
        }

        #region Read File
        [Block("Reads the entire content of a file to a single string")]
        public static async Task<string> FileRead(BotData data, string path, FileEncoding encoding = FileEncoding.UTF8)
        {
            path = SanitizePath(path);
            var text = await ExecuteFileOperation(data, path, true, async (p, c) =>
            {
                return await File.ReadAllTextAsync(p, MapEncoding(encoding), data.CancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Read {path}: {text.TruncatePretty(200)}", LogColors.Flavescent);
            return text;
        }

        [Block("Reads all lines of a file")]
        public static async Task<List<string>> FileReadLines(BotData data, string path, FileEncoding encoding = FileEncoding.UTF8)
        {
            path = SanitizePath(path);
            var lines = await ExecuteFileOperation(data, path, true, async (p, c) =>
            {
                return await File.ReadAllLinesAsync(p, MapEncoding(encoding), data.CancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Read {lines.Length} lines from {path}", LogColors.Flavescent);
            return lines.ToList();
        }

        [Block("Reads all bytes of a file")]
        public static async Task<byte[]> FileReadBytes(BotData data, string path)
        {
            path = SanitizePath(path);
            var bytes = await ExecuteFileOperation(data, path, true, async (p, c) =>
            {
                return await File.ReadAllBytesAsync(p, data.CancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Read {bytes.Length} bytes from {path}", LogColors.Flavescent);
            return bytes;
        }
        #endregion

        #region Write File
        [Block("Writes a string to a file",
            extraInfo = "The file will be created if it doesn't exist and all its previous content will be overwritten")]
        public static async Task FileWrite(BotData data, string path, [Interpolated] string content,
            FileEncoding encoding = FileEncoding.UTF8)
        {
            path = SanitizePath(path);
            await ExecuteFileOperation(data, path, content, async (p, c) =>
            {
                await File.WriteAllTextAsync(p, c.Unescape(), MapEncoding(encoding), data.CancellationToken).ConfigureAwait(false);
                return true;
            }, isWriteOperation: true).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Wrote content to {path}", LogColors.Flavescent);
        }

        [Block("Writes lines to a file",
            extraInfo = "The file will be created if it doesn't exist and all its previous content will be overwritten")]
        public static async Task FileWriteLines(BotData data, string path, [Variable] List<string> lines,
            FileEncoding encoding = FileEncoding.UTF8)
        {
            path = SanitizePath(path);
            await ExecuteFileOperation(data, path, lines, async (p, c) =>
            {
                await File.WriteAllLinesAsync(p, c, MapEncoding(encoding), data.CancellationToken).ConfigureAwait(false);
                return true;
            }, isWriteOperation: true).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Wrote lines to {path}", LogColors.Flavescent);
        }

        [Block("Writes bytes to a file",
            extraInfo = "The file will be created if it doesn't exist and all its previous content will be overwritten")]
        public static async Task FileWriteBytes(BotData data, string path, [Variable] byte[] content)
        {
            path = SanitizePath(path);
            await ExecuteFileOperation(data, path, content, async (p, c) =>
            {
                await File.WriteAllBytesAsync(p, c, data.CancellationToken).ConfigureAwait(false);
                return true;
            }, isWriteOperation: true).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Wrote bytes to {path}", LogColors.Flavescent);
        }
        #endregion

        #region Append File
        [Block("Appends a string at the end of a file")]
        public static async Task FileAppend(BotData data, string path, [Interpolated] string content,
            FileEncoding encoding = FileEncoding.UTF8)
        {
            path = SanitizePath(path);
            await ExecuteFileOperation(data, path, content, async (p, c) =>
            {
                await File.AppendAllTextAsync(p, c.Unescape(), MapEncoding(encoding), data.CancellationToken).ConfigureAwait(false);
                return true;
            }, isWriteOperation: true).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Appended content to {path}", LogColors.Flavescent);
        }

        [Block("Appends lines at the end of a file")]
        public static async Task FileAppendLines(BotData data, string path, [Variable] List<string> lines,
            FileEncoding encoding = FileEncoding.UTF8)
        {
            path = SanitizePath(path);
            await ExecuteFileOperation(data, path, lines, async (p, c) =>
            {
                await File.AppendAllLinesAsync(p, c, MapEncoding(encoding), data.CancellationToken).ConfigureAwait(false);
                return true;
            }, isWriteOperation: true).ConfigureAwait(false);

            data.Logger.LogHeader();
            data.Logger.Log($"Appended lines to {path}", LogColors.Flavescent);
        }
        #endregion

        #region File Operations
        [Block("Copies a file to a new location")]
        public static void FileCopy(BotData data, string originPath, string destinationPath)
        {
            originPath = SanitizePath(originPath);
            destinationPath = SanitizePath(destinationPath);

            if (data.Providers.Security.RestrictBlocksToCWD)
            {
                FileUtils.ThrowIfNotInCWD(originPath);
                FileUtils.ThrowIfNotInCWD(destinationPath);
            }

            FileUtils.CreatePath(destinationPath);

            lock (FileLocker.GetHandle(originPath).GetSyncLock())
                lock (FileLocker.GetHandle(destinationPath).GetSyncLock())
                    File.Copy(originPath, destinationPath);

            data.Logger.LogHeader();
            data.Logger.Log($"Copied {originPath} to {destinationPath}", LogColors.Flavescent);
        }

        [Block("Moves a file to a new location")]
        public static void FileMove(BotData data, string originPath, string destinationPath)
        {
            originPath = SanitizePath(originPath);
            destinationPath = SanitizePath(destinationPath);

            if (data.Providers.Security.RestrictBlocksToCWD)
            {
                FileUtils.ThrowIfNotInCWD(originPath);
                FileUtils.ThrowIfNotInCWD(destinationPath);
            }

            FileUtils.CreatePath(destinationPath);

            lock (FileLocker.GetHandle(originPath).GetSyncLock())
                lock (FileLocker.GetHandle(destinationPath).GetSyncLock())
                    File.Move(originPath, destinationPath);

            data.Logger.LogHeader();
            data.Logger.Log($"Moved {originPath} to {destinationPath}", LogColors.Flavescent);
        }

        [Block("Deletes a file")]
        public static void FileDelete(BotData data, string path)
        {
            path = SanitizePath(path);

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(path);

            lock (FileLocker.GetHandle(path).GetSyncLock())
                File.Delete(path);

            data.Logger.LogHeader();
            data.Logger.Log($"Deleted {path}", LogColors.Flavescent);
        }
        #endregion

        #region Folders
        [Block("Checks if a folder exists")]
        public static bool FolderExists(BotData data, string path)
        {
            path = SanitizePath(path);

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(path);

            var exists = Directory.Exists(path);
            data.Logger.LogHeader();
            data.Logger.Log(path + (exists ? " exists" : " does not exist"), LogColors.Flavescent);
            return exists;
        }

        [Block("Creates a directory in the given path")]
        public static void CreatePath(BotData data, string path)
        {
            path = SanitizePath(path);

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(path);

            FileUtils.CreatePath(path);
            data.Logger.LogHeader();
            data.Logger.Log($"The path {path} was created", LogColors.Flavescent);
        }

        [Block("Gets the paths to all files in a specific folder")]
        public static List<string> GetFilesInFolder(BotData data, string path)
        {
            path = SanitizePath(path);

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(path);

            data.Logger.LogHeader();
            var files = Directory.GetFiles(path).ToList();
            data.Logger.Log($"Found {files.Count} files in {path}", LogColors.Flavescent);
            return files;
        }

        [Block("Deletes a given directory")]
        public static void FolderDelete(BotData data, string path)
        {
            path = SanitizePath(path);

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(path);

            Directory.Delete(path, true);

            data.Logger.LogHeader();
            data.Logger.Log($"Deleted {path}", LogColors.Flavescent);
        }

        [Block("Deletes all files and folders inside the system temporary folder",
            extraInfo = "Skips entries that are currently locked or in use by other processes.")]
        public static int ClearTempFolder(BotData data)
        {
            var tempPath = Path.GetTempPath();

            if (string.IsNullOrWhiteSpace(tempPath))
            {
                throw new InvalidOperationException("Unable to resolve the system temporary folder path.");
            }

            tempPath = SanitizePath(tempPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!Directory.Exists(tempPath))
            {
                data.Logger.LogHeader();
                data.Logger.Log($"The temporary directory {tempPath} does not exist.", LogColors.Flavescent);
                return 0;
            }

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(tempPath);

            var deletedFiles = 0;
            var deletedDirectories = 0;
            var skippedEntries = 0;
            var pendingDirectories = new Stack<(string Path, bool Visited)>();
            pendingDirectories.Push((tempPath, false));

            while (pendingDirectories.Count > 0)
            {
                var (currentPath, visited) = pendingDirectories.Pop();

                if (!visited)
                {
                    string[] filesInCurrent;
                    try
                    {
                        filesInCurrent = Directory.GetFiles(currentPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skippedEntries++;
                        continue;
                    }

                    foreach (var file in filesInCurrent)
                    {
                        var handle = FileLocker.GetHandle(file);
                        try
                        {
                            lock (handle.GetSyncLock())
                            {
                                if (File.Exists(file))
                                {
                                    File.Delete(file);
                                    deletedFiles++;
                                }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            skippedEntries++;
                        }
                    }

                    // Ensure we attempt to delete the directory after processing its children.
                    pendingDirectories.Push((currentPath, true));

                    string[] subDirectories;
                    try
                    {
                        subDirectories = Directory.GetDirectories(currentPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skippedEntries++;
                        continue;
                    }

                    foreach (var subDirectory in subDirectories)
                    {
                        try
                        {
                            var attributes = File.GetAttributes(subDirectory);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            skippedEntries++;
                            continue;
                        }

                        pendingDirectories.Push((subDirectory, false));
                    }
                }
                else
                {
                    if (string.Equals(currentPath, tempPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Don't delete the root temp directory itself.
                    }

                    try
                    {
                        Directory.Delete(currentPath, false);
                        deletedDirectories++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skippedEntries++;
                    }
                }
            }

            data.Logger.LogHeader();
            data.Logger.Log($"Deleted {deletedFiles} files and {deletedDirectories} folders from {tempPath}. Skipped {skippedEntries} locked or inaccessible entries.", LogColors.Flavescent);

            return deletedFiles + deletedDirectories;
        }
        #endregion

        private static async Task<TOut> ExecuteFileOperation<TIn, TOut>(BotData data, string path, TIn parameter,
            Func<string, TIn, Task<TOut>> func, bool isWriteOperation = false)
        {
            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(path);

            FileUtils.CreatePath(path);

            TOut result;
            var fileLock = FileLocker.GetHandle(path);

            try
            {
                if (isWriteOperation)
                {
                    await fileLock.EnterWriteLock(data.CancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await fileLock.EnterReadLock(data.CancellationToken).ConfigureAwait(false);
                }

                result = await func.Invoke(path, parameter).ConfigureAwait(false);
            }
            finally
            {
                if (isWriteOperation)
                {
                    fileLock.ExitWriteLock();
                }
                else
                {
                    fileLock.ExitReadLock();
                }
            }

            return result;
        }

        private static Encoding MapEncoding(FileEncoding encoding)
            => encoding switch
            {
                FileEncoding.UTF8 => Encoding.UTF8,
                FileEncoding.ASCII => Encoding.ASCII,
                FileEncoding.Unicode => Encoding.Unicode,
                FileEncoding.BigEndianUnicode => Encoding.BigEndianUnicode,
                FileEncoding.UTF32 => Encoding.UTF32,
                FileEncoding.Latin1 => Encoding.Latin1,
                _ => throw new NotImplementedException()
            };

        private static string SanitizePath(string path)
        {
            foreach (var invalid in Path.GetInvalidPathChars())
            {
                path = path.Replace(invalid.ToString(), "");
            }

            return path;
        }
    }

    public enum FileEncoding
    {
        UTF8,
        ASCII,
        Unicode,
        BigEndianUnicode,
        UTF32,
        Latin1
    }
}
