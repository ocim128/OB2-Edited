using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OpenBullet2.Native.Views.Pages.Shared
{
    public class LogSegment
    {
        public int StartOffset { get; set; }
        public int Length { get; set; }
        public Brush Foreground { get; set; }
        public Brush? Background { get; set; }
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    }

    public class DebuggerLogColorizer : DocumentColorizingTransformer
    {
        private readonly List<LogSegment> _segments;

        public DebuggerLogColorizer(List<LogSegment> segments)
        {
            _segments = segments;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (_segments.Count == 0) return;

            int lineStart = line.Offset;
            int lineEnd = line.Offset + line.Length;

            // Find the index of the first segment that starts >= lineStart
            // We use BinarySearch with a dummy segment
            int index = _segments.BinarySearch(new LogSegment { StartOffset = lineStart }, new SegmentStartComparer());
            if (index < 0) index = ~index;

            // Check previous segment in case it overlaps into this line
            // Since log entries are disjoint and sequential, at most one previous segment can overlap
            // (unless segments are nested? No, they are sequential log entries)
            // Actually, we process all segments that overlap the line.
            
            // Start checking from index - 1 (if exists)
            int startIndex = Math.Max(0, index - 1);

            // Iterate until we find a segment that starts beyond the line
            for (int i = startIndex; i < _segments.Count; i++)
            {
                var segment = _segments[i];

                // If segment starts after line ends, we are done
                if (segment.StartOffset >= lineEnd) break;

                // If segment ends before line starts, continue
                if (segment.StartOffset + segment.Length <= lineStart) continue;

                // Intersection
                int start = Math.Max(segment.StartOffset, lineStart);
                int end = Math.Min(segment.StartOffset + segment.Length, lineEnd);

                if (end > start)
                {
                    ChangeLinePart(start, end, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(segment.Foreground);
                        
                        if (segment.Background != null)
                        {
                            element.BackgroundBrush = segment.Background;
                        }

                        if (segment.FontWeight != FontWeights.Normal)
                        {
                            element.TextRunProperties.SetTypeface(new Typeface(
                                element.TextRunProperties.Typeface.FontFamily,
                                element.TextRunProperties.Typeface.Style,
                                segment.FontWeight,
                                element.TextRunProperties.Typeface.Stretch));
                        }
                    });
                }
            }
        }

        private class SegmentStartComparer : IComparer<LogSegment>
        {
            public int Compare(LogSegment? x, LogSegment? y)
            {
                if (x == null || y == null) return 0;
                return x.StartOffset.CompareTo(y.StartOffset);
            }
        }
    }
}
