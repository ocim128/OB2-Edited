using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace RuriLib.Http;

/// <summary>
/// Lightweight pool for frequently-allocated HTTP response parsing objects.
/// Only pools objects with measurable allocation pressure in hot paths.
/// </summary>
public static class MemoryPoolUtility
{
    private static readonly ConcurrentQueue<StringBuilder> _stringBuilderPool = new();
    private static readonly ConcurrentQueue<Dictionary<string, string>> _headerDictionaryPool = new();

    private const int MaxStringBuilderCapacity = 8192;
    private const int MaxDictionarySize = 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder GetStringBuilder(int initialCapacity = 256)
    {
        if (_stringBuilderPool.TryDequeue(out var sb))
        {
            sb.Clear();
            if (sb.Capacity < initialCapacity)
                sb.Capacity = initialCapacity;
            return sb;
        }
        return new StringBuilder(initialCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb != null && sb.Capacity <= MaxStringBuilderCapacity)
            _stringBuilderPool.Enqueue(sb);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, string> GetHeaderDictionary()
    {
        if (_headerDictionaryPool.TryDequeue(out var dict))
        {
            dict.Clear();
            return dict;
        }
        return new Dictionary<string, string>(16, StringComparer.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnHeaderDictionary(Dictionary<string, string> dict)
    {
        if (dict != null && dict.Count <= MaxDictionarySize)
            _headerDictionaryPool.Enqueue(dict);
    }
}
