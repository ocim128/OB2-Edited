using RuriLib.Extensions;
using System;
using System.Linq;

namespace RuriLib.Functions.Conversion
{
    public static class BinaryConverter
    {
        /// <summary>
        /// Converts a <see cref="string"/> <paramref name="str"/> of zeroes and ones to a <see cref="byte[]"/>,
        /// optionally adding a padding to the left if one of the octets is incomplete.
        /// </summary>
        public static byte[] ToByteArray(string str, bool addPadding = true)
        {
            if (str.Contains(" "))
                str = str.Replace(" ", "");

            if (addPadding)
                str = str.PadLeftToNearestMultiple(8);

            return str.SplitInChunks(8, false)
                .Select(octet => Convert.ToByte(octet, 2))
                .ToArray();
        }

        /// <summary>
        /// Converts a <see cref="byte[]"/> to a string of ones and zeroes.
        /// </summary>
        public static string ToBinaryString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var chars = new char[bytes.Length * 8];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                int base_idx = i * 8;
                chars[base_idx]     = (char)('0' + ((b >> 7) & 1));
                chars[base_idx + 1] = (char)('0' + ((b >> 6) & 1));
                chars[base_idx + 2] = (char)('0' + ((b >> 5) & 1));
                chars[base_idx + 3] = (char)('0' + ((b >> 4) & 1));
                chars[base_idx + 4] = (char)('0' + ((b >> 3) & 1));
                chars[base_idx + 5] = (char)('0' + ((b >> 2) & 1));
                chars[base_idx + 6] = (char)('0' + ((b >> 1) & 1));
                chars[base_idx + 7] = (char)('0' + (b & 1));
            }
            return new string(chars);
        }
    }
}
