using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Helpers
{
    /// <summary>
    /// Async keyed lock to serialize work on a given key with low contention and no leaks.
    /// </summary>
    public sealed class AsyncLocker : IDisposable
    {
        private readonly ConcurrentDictionary<string, RefCountedSemaphore> _map = new(StringComparer.Ordinal);
        private volatile bool _disposed;

        /// <summary>
        /// Acquire the lock for a key. Returns a disposable that must be disposed to release.
        /// Prefer using with 'await using' for async scopes.
        /// </summary>
        public async Task<IDisposable> Acquire(string key, CancellationToken cancellationToken = default)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            ThrowIfDisposed();

            var sem = _map.GetOrAdd(key, static _ => new RefCountedSemaphore());
            sem.IncrementRef();

            try
            {
                await sem.Sem.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Releaser(this, key, sem);
            }
            catch
            {
                // If WaitAsync throws (e.g. cancellation), decrement the refcount we incremented.
                if (sem.DecrementRef() == 0)
                {
                    _map.TryRemove(key, out _);
                    sem.Dispose();
                }
                throw;
            }
        }

        // Backward-compatibility shims (non-async usage in existing code)
        /// <summary>
        /// Synchronous acquire for existing call sites that did not await. Prefer the async overload.
        /// </summary>
        public IDisposable AcquireSync(string key, CancellationToken cancellationToken = default)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            ThrowIfDisposed();

            var sem = _map.GetOrAdd(key, static _ => new RefCountedSemaphore());
            sem.IncrementRef();

            try
            {
                sem.Sem.Wait(cancellationToken);
                return new Releaser(this, key, sem);
            }
            catch
            {
                if (sem.DecrementRef() == 0)
                {
                    _map.TryRemove(key, out _);
                    sem.Dispose();
                }
                throw;
            }
        }

        /// <summary>
        /// Acquire using a type + method name composite key.
        /// </summary>
        public Task<IDisposable> Acquire(Type classType, string methodName, CancellationToken cancellationToken = default)
            => Acquire(CombineTypes(classType, methodName), cancellationToken);

        // Backward-compatibility: existing call sites expect explicit Release methods.
        public void Release(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_disposed) return;
            if (_map.TryGetValue(key, out var sem))
            {
                ReleaseInternal(key, sem);
            }
        }

        public void Release(Type classType, string methodName)
        {
            if (classType is null || string.IsNullOrEmpty(methodName)) return;
            var key = CombineTypes(classType, methodName);
            Release(key);
        }

        private static string CombineTypes(Type classType, string methodName)
        {
            if (classType is null) throw new ArgumentNullException(nameof(classType));
            if (string.IsNullOrEmpty(methodName)) throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));
            return $"{classType.FullName}.{methodName}";
        }

        private void ReleaseInternal(string key, RefCountedSemaphore sem)
        {
            // Release the semaphore first so the next waiter can proceed ASAP
            sem.Sem.Release();

            // Decrement refcount and cleanup if nobody else references this key
            if (sem.DecrementRef() == 0)
            {
                _map.TryRemove(key, out _);
                sem.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AsyncLocker));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _map)
            {
                kv.Value.DisposeSafely();
            }
            _map.Clear();
        }

        private sealed class RefCountedSemaphore : IDisposable
        {
            private int _refCount = 0;
            private int _disposed;
            public SemaphoreSlim Sem { get; } = new(1, 1);

            public void IncrementRef() => Interlocked.Increment(ref _refCount);

            public int DecrementRef() => Interlocked.Decrement(ref _refCount);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
                try { Sem.Dispose(); } catch { /* swallow */ }
            }

            public void DisposeSafely()
            {
                try { Dispose(); } catch { /* swallow */ }
            }
        }

        private sealed class Releaser : IDisposable
        {
            private AsyncLocker _owner;
            private readonly string _key;
            private RefCountedSemaphore _sem;

            public Releaser(AsyncLocker owner, string key, RefCountedSemaphore sem)
            {
                _owner = owner;
                _key = key;
                _sem = sem;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                var sem = Interlocked.Exchange(ref _sem, null);
                if (owner is null || sem is null) return;
                if (owner._disposed)
                {
                    // If owner disposed, just dispose the semaphore instance
                    sem.DisposeSafely();
                    return;
                }

                owner.ReleaseInternal(_key, sem);
            }
        }
    }
}
