using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace RuriLib.Http;

/// <summary>
/// High-performance memory pool utility for optimizing allocations across HTTP components.
/// </summary>
public static class MemoryPoolUtility
{
    // Shared pools for common objects
    private static readonly ConcurrentQueue<StringBuilder> _stringBuilderPool = new();
    private static readonly ConcurrentQueue<Dictionary<string, string>> _headerDictionaryPool = new();
    private static readonly ConcurrentQueue<List<string>> _stringListPool = new();
    private static readonly ConcurrentQueue<MemoryStream> _memoryStreamPool = new();
    
    // Buffer pools
    private static readonly ArrayPool<byte> _byteArrayPool = ArrayPool<byte>.Shared;
    private static readonly ArrayPool<char> _charArrayPool = ArrayPool<char>.Shared;
    
    // Pool size limits to prevent memory bloat
    private const int MaxStringBuilderCapacity = 8192;
    private const int MaxDictionarySize = 64;
    private const int MaxListSize = 128;
    private const int MaxMemoryStreamCapacity = 1024 * 1024; // 1MB
    
    #region StringBuilder Pool
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder GetStringBuilder(int initialCapacity = 256)
    {
        if (_stringBuilderPool.TryDequeue(out var sb))
        {
            sb.Clear();
            if (sb.Capacity < initialCapacity)
            {
                sb.Capacity = initialCapacity;
            }
            return sb;
        }
        return new StringBuilder(initialCapacity);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb != null && sb.Capacity <= MaxStringBuilderCapacity)
        {
            _stringBuilderPool.Enqueue(sb);
        }
    }
    
    #endregion
    
    #region Dictionary Pool
    
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
        {
            _headerDictionaryPool.Enqueue(dict);
        }
    }
    
    #endregion
    
    #region List Pool
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<string> GetStringList()
    {
        if (_stringListPool.TryDequeue(out var list))
        {
            list.Clear();
            return list;
        }
        return new List<string>();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnStringList(List<string> list)
    {
        if (list != null && list.Count <= MaxListSize)
        {
            _stringListPool.Enqueue(list);
        }
    }
    
    #endregion
    
    #region MemoryStream Pool
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MemoryStream GetMemoryStream()
    {
        if (_memoryStreamPool.TryDequeue(out var stream))
        {
            stream.SetLength(0);
            stream.Position = 0;
            return stream;
        }
        return new MemoryStream();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnMemoryStream(MemoryStream stream)
    {
        if (stream != null && stream.Capacity <= MaxMemoryStreamCapacity)
        {
            _memoryStreamPool.Enqueue(stream);
        }
        else
        {
            stream?.Dispose();
        }
    }
    
    #endregion
    
    #region Array Pool Wrappers
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] RentByteArray(int minimumLength)
    {
        return _byteArrayPool.Rent(minimumLength);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnByteArray(byte[] array)
    {
        if (array != null)
        {
            _byteArrayPool.Return(array);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char[] RentCharArray(int minimumLength)
    {
        return _charArrayPool.Rent(minimumLength);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnCharArray(char[] array)
    {
        if (array != null)
        {
            _charArrayPool.Return(array);
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    /// <summary>
    /// Efficiently converts a ReadOnlySpan&lt;byte&gt; to string using pooled char array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string GetStringFromBytes(ReadOnlySpan<byte> bytes, Encoding encoding = null)
    {
        if (bytes.IsEmpty)
            return string.Empty;
            
        encoding ??= Encoding.UTF8;
        
        var charCount = encoding.GetCharCount(bytes);
        if (charCount == 0)
            return string.Empty;
            
        var charArray = RentCharArray(charCount);
        try
        {
            var actualCharCount = encoding.GetChars(bytes, charArray);
            return new string(charArray, 0, actualCharCount);
        }
        finally
        {
            ReturnCharArray(charArray);
        }
    }
    
    /// <summary>
    /// Efficiently trims whitespace from a ReadOnlySpan&lt;byte&gt; and converts to string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string GetTrimmedStringFromBytes(ReadOnlySpan<byte> bytes, Encoding encoding = null)
    {
        if (bytes.IsEmpty)
            return string.Empty;
            
        // Trim leading and trailing spaces
        var start = 0;
        var end = bytes.Length - 1;
        
        while (start <= end && bytes[start] == ' ') start++;
        while (end >= start && bytes[end] == ' ') end--;
        
        if (start > end)
            return string.Empty;
            
        return GetStringFromBytes(bytes.Slice(start, end - start + 1), encoding);
    }
    
    #endregion
}