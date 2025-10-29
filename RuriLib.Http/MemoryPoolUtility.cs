using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace RuriLib.Http;

public static class MemoryPoolUtility
{
    private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] RentByteArray(int minimumLength)
        => BytePool.Rent(minimumLength);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnByteArray(byte[] buffer)
    {
        if (buffer == null)
        {
            return;
        }

        BytePool.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetTrimmedStringFromBytes(ReadOnlySpan<byte> bytes, Encoding encoding = null)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        encoding ??= Encoding.UTF8;
        return encoding.GetString(bytes).Trim();
    }
}