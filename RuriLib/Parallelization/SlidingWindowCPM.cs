using System;
using System.Threading;

namespace RuriLib.Parallelization
{
    /// <summary>
    /// A high-performance, thread-safe counter that tracks events in a sliding window of time.
    /// This implementation uses a circular buffer of seconds.
    /// </summary>
    public class SlidingWindowCPM
    {
        private readonly int[] _buckets;
        private readonly int _windowSeconds;
        
        // Use long for atomic 64-bit exchange to prevent tearing when reading/writing timestamp
        private long _lastSecondTimestamp;

        public SlidingWindowCPM(int windowSeconds = 60)
        {
            if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _windowSeconds = windowSeconds;
            _buckets = new int[windowSeconds];
            _lastSecondTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public void Increment()
        {
            UpdateBuckets();
            
            var currentSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var index = currentSecond % _windowSeconds;
            Interlocked.Increment(ref _buckets[index]);
        }

        public int Count
        {
            get
            {
                UpdateBuckets();
                
                // Sum all buckets
                int sum = 0;
                for (int i = 0; i < _windowSeconds; i++)
                {
                    sum += Volatile.Read(ref _buckets[i]);
                }
                return sum;
            }
        }

        private void UpdateBuckets()
        {
            var currentSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var lastSecond = Interlocked.Read(ref _lastSecondTimestamp);

            if (currentSecond <= lastSecond) return;

            // Try to advance time
            if (Interlocked.CompareExchange(ref _lastSecondTimestamp, currentSecond, lastSecond) == lastSecond)
            {
                var diff = currentSecond - lastSecond;
                
                // If more time passed than the window size, clear everything
                if (diff >= _windowSeconds)
                {
                    Array.Clear(_buckets, 0, _buckets.Length);
                }
                else
                {
                    // Clear the buckets that we "skipped" over
                    // e.g. last=10, current=13 (diff=3). Clear 11, 12, 13
                    // The strictly correct logic for a circular buffer:
                    // We need to clear buckets corresponding to (lastSecond + 1) to currentSecond (inclusive)
                    
                    for (long i = 1; i <= diff; i++)
                    {
                        var index = (lastSecond + i) % _windowSeconds;
                        // Reset the bucket count for this new second
                        Interlocked.Exchange(ref _buckets[index], 0);
                    }
                }
            }
        }
    }
}
