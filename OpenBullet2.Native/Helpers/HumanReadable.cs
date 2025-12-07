using System;
using System.Globalization;

namespace OpenBullet2.Native.Helpers;

public static class HumanReadable
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long bytes) => Bytes((double)bytes);

    public static string Bytes(double bytes)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes <= 0)
        {
            return "0 B";
        }

        var value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = value >= 100 || unitIndex == 0 ? "0" : "0.0";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {SizeUnits[unitIndex]}";
    }
}
