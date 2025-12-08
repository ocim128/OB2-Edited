using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OpenBullet2.Native.Extensions
{
    public static class IEnumerableExtensions
    {
        public static void SaveToFile<T>(this IEnumerable<T> items, string fileName, Func<T, string> mapping)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentNullException(nameof(fileName), "The filename must not be empty");
            }
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (mapping is null) throw new ArgumentNullException(nameof(mapping));

            // Avoid materializing the whole sequence to minimize RAM; write streaming
            using var sw = new StreamWriter(fileName);
            foreach (var item in items)
            {
                var line = mapping(item);
                if (line is not null)
                    sw.WriteLine(line);
            }
        }

        public static async Task CopyToClipboardAsync<T>(this IEnumerable<T> items, Func<T, string> mapping)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (mapping is null) throw new ArgumentNullException(nameof(mapping));

            // Build the text with a pooled buffer to avoid multiple enumerations/allocations
            // and then apply a bounded retry with exponential backoff on clipboard contention.
            using var writer = new StringWriter();
            using var e = items.GetEnumerator();
            if (e.MoveNext())
            {
                var first = mapping(e.Current);
                if (first is not null)
                    writer.Write(first);
                while (e.MoveNext())
                {
                    writer.Write(Environment.NewLine);
                    var s = mapping(e.Current);
                    if (s is not null)
                        writer.Write(s);
                }
            }

            var text = writer.ToString();
            var dispatcher = Application.Current?.Dispatcher;

            const int maxAttempts = 6;
            var delayMs = 10;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (dispatcher is null || dispatcher.CheckAccess())
                    {
                        Clipboard.SetText(text);
                    }
                    else
                    {
                        await dispatcher.InvokeAsync(() => Clipboard.SetText(text)).Task.ConfigureAwait(false);
                    }
                    return;
                }
                catch (COMException ex)
                {
                    const uint CLIPBRD_E_CANT_OPEN = 0x800401D0;
                    if ((uint)ex.ErrorCode != CLIPBRD_E_CANT_OPEN)
                        throw;
                    // backoff and retry
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    delayMs = Math.Min(delayMs * 2, 200);
                }
            }
        }

        // Intentionally no synchronous wrapper: awaiting CopyToClipboardAsync avoids dispatcher deadlocks under contention.
    }
}
