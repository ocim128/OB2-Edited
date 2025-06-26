using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Files
{
    /// <summary>
    /// Singleton class that manages application-wide file locking to avoid cross thread IO operations on the same file.
    /// </summary>
    public static class FileLocker
    {
        private static readonly ConcurrentDictionary<string, RWLock> lockTable = new();

        /// <summary>
        /// Gets a <see cref="RWLock"/> associated to a file name or creates one if it doesn't exist.
        /// </summary>
        /// <param name="fileName">The name of the file to access</param>
        public static RWLock GetHandle(string fileName)
        {
            // Thread-safe get-or-create pattern
            return lockTable.GetOrAdd(fileName, _ => new RWLock());
        }
    }

    public class RWLock : IDisposable
    {
        private readonly SemaphoreSlim fileLock = new SemaphoreSlim(1, 1);
        private readonly object syncLock = new object(); // For legacy synchronous code

        // Simplified approach: treat all file operations as exclusive
        // This eliminates deadlock possibilities while maintaining thread safety
        public Task EnterReadLock(CancellationToken cancellationToken = default) 
            => fileLock.WaitAsync(cancellationToken);

        public Task EnterWriteLock(CancellationToken cancellationToken = default) 
            => fileLock.WaitAsync(cancellationToken);

        public void ExitReadLock() => fileLock.Release();

        public void ExitWriteLock() => fileLock.Release();

        // Legacy synchronous lock support for backward compatibility
        public object GetSyncLock() => syncLock;

        public void Dispose()
        {
            fileLock?.Dispose();
        }
    }
}
