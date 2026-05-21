using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RuriLib.Functions.Parsing
{
    /// <summary>
    /// Provides thread-safe caching for compiled regex patterns to improve performance.
    /// </summary>
    public static class RegexCache
    {
        public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(2);
        private const int MaxCacheEntries = 512;
        private static readonly ConcurrentDictionary<RegexCacheKey, Regex> _cache = new();
        private static readonly ConcurrentDictionary<RegexCacheKey, Regex> _compiledCache = new();

        /// <summary>
        /// Gets a cached regex pattern or creates a new one if not found.
        /// </summary>
        /// <param name="pattern">The regex pattern</param>
        /// <param name="options">Regex options</param>
        /// <param name="compile">Whether to compile the regex for better performance</param>
        /// <returns>A cached or new Regex instance</returns>
        public static Regex GetOrCreate(string pattern, RegexOptions options = RegexOptions.None,
            bool compile = true, TimeSpan? matchTimeout = null)
        {
            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentNullException(nameof(pattern));

            var timeout = matchTimeout ?? DefaultMatchTimeout;
            var key = new RegexCacheKey(pattern, options, timeout.Ticks);
            
            if (compile)
            {
                EnsureCapacity(_compiledCache);
                return _compiledCache.GetOrAdd(key, _ => new Regex(pattern, options | RegexOptions.Compiled, timeout));
            }
            
            EnsureCapacity(_cache);
            return _cache.GetOrAdd(key, _ => new Regex(pattern, options, timeout));
        }

        private static void EnsureCapacity(ConcurrentDictionary<RegexCacheKey, Regex> cache)
        {
            if (cache.Count >= MaxCacheEntries)
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// Clears all cached regex patterns.
        /// </summary>
        public static void Clear()
        {
            _cache.Clear();
            _compiledCache.Clear();
        }

        /// <summary>
        /// Gets the number of cached regex patterns.
        /// </summary>
        public static int CacheCount => _cache.Count + _compiledCache.Count;

        /// <summary>
        /// Gets the number of compiled regex patterns.
        /// </summary>
        public static int CompiledCacheCount => _compiledCache.Count;

        private readonly record struct RegexCacheKey(string Pattern, RegexOptions Options, long TimeoutTicks);
    }
}
