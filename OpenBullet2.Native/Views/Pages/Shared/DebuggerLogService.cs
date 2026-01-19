using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using OpenBullet2.Native.ViewModels;
using RuriLib.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace OpenBullet2.Native.Views.Pages.Shared
{
    public class DebuggerLogService : IDisposable
    {
        private readonly TextEditor _logRTB;
        private readonly DebuggerViewModel _viewModel;
        
        // State for syntax highlighting
        private readonly List<LogSegment> _segments = new();
        private readonly DebuggerLogColorizer _colorizer;
        
        private readonly Dictionary<string, Brush> _brushCache = new();

        // State for search
        private readonly List<int> _searchMatches = new();
        private int _currentMatchIndex = -1;
        private string _lastSearchText = string.Empty;

        // State for block navigation
        private readonly List<int> _blockStartIndices = new();
        private int _currentBlockIndex = -1;

        // Buffering
        private readonly ConcurrentQueue<BotLoggerEntry> _pendingLogs = new();
        private bool _variablesNeedUpdate;

        // UI State flags
        public bool UpdatesPaused { get; set; }
        public bool IsResizing { get; set; }
        public bool IsWindowMinimized { get; set; }
        public bool ScrollingDisabled { get; set; }

        public DebuggerLogService(TextEditor logRTB, DebuggerViewModel viewModel)
        {
            _logRTB = logRTB;
            _viewModel = viewModel;

            // Configure hyperlink color for readability on dark background
            _logRTB.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.FromRgb(88, 209, 235)); // Bright cyan #58D1EB

            // Initialize Syntax Highlighting for Log
            _colorizer = new DebuggerLogColorizer(_segments);
            _logRTB.TextArea.TextView.LineTransformers.Add(_colorizer);
        }

        public void HandleNewLogEntry(BotLoggerEntry entry)
        {
            _pendingLogs.Enqueue(entry);
        }

        public void ProcessPendingEntries()
        {
            if (UpdatesPaused || IsWindowMinimized || IsResizing) return;

            bool logUpdated = false;
            
            // Process log buffer
            if (!_pendingLogs.IsEmpty)
            {
                var sb = new StringBuilder();
                var newSegments = new List<LogSegment>();
                int startOffset = _logRTB.Document.TextLength;
                int currentOffset = startOffset;
                
                // Limit batch size to prevent UI freeze on massive dumps
                int count = 0;
                while (_pendingLogs.TryDequeue(out var entry) && count < 500)
                {
                    // Track block starts efficiently
                    if (entry.IsBlockStart)
                    {
                         _blockStartIndices.Add(currentOffset);
                    }

                    string textToAppend = entry.Message + Environment.NewLine;
                    sb.Append(textToAppend);

                    var foreground = GetBrush(entry.Color);
                    var background = GetBackgroundBrush(entry, _segments.Count + newSegments.Count);
                    var fontWeight = entry.IsBlockStart ? FontWeights.Bold : FontWeights.Normal;

                    newSegments.Add(new LogSegment
                    {
                        StartOffset = currentOffset,
                        Length = textToAppend.Length,
                        Foreground = foreground,
                        Background = background,
                        FontWeight = fontWeight
                    });

                    currentOffset += textToAppend.Length;
                    _variablesNeedUpdate = true;
                    count++;
                }

                if (sb.Length > 0)
                {
                    _logRTB.AppendText(sb.ToString());
                    _segments.AddRange(newSegments);
                    logUpdated = true;
                }
            }

            // Auto-scroll request
            if (logUpdated && !ScrollingDisabled)
            {
                _logRTB.ScrollToEnd();
                
                // Use background priority to ensure layout is updated before scrolling again
                // This fixes issues where the scroll doesn't reach the absolute bottom when large amounts of text are added
                _logRTB.Dispatcher.BeginInvoke(new Action(() => _logRTB.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
            }

            // Update variables if needed
            if (_variablesNeedUpdate) 
            {
                _viewModel.RefreshVariables();
                _variablesNeedUpdate = false;
            }
        }

        public void UpdateVariablesList()
        {
            _viewModel.RefreshVariables();
        }

        public void ClearLog(Action<string>? htmlClearAction = null)
        {
            _logRTB.Clear();
            _segments.Clear();
            _searchMatches.Clear();
            _blockStartIndices.Clear();
            _currentMatchIndex = -1;
            _currentBlockIndex = -1;
            
            _pendingLogs.Clear(); // Clear pending buffer too
            
            // Force redraw of highlighting
            _logRTB.TextArea.TextView.Redraw();
            
            htmlClearAction?.Invoke(string.Empty);
        }

        #region Search
        public void Search()
        {
            var text = _viewModel.SearchString;
            if (string.IsNullOrEmpty(text))
            {
                ClearSearch();
                return;
            }

            if (text != _lastSearchText)
            {
                _lastSearchText = text;
                PerformSearch(text);
            }
            else
            {
                NextMatch();
            }
        }

        private void PerformSearch(string text)
        {
            _searchMatches.Clear();
            _currentMatchIndex = -1;

            var docText = _logRTB.Document.Text;
            int index = 0;
            while ((index = docText.IndexOf(text, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                _searchMatches.Add(index);
                index += text.Length;
            }

            _viewModel.Indices = _searchMatches.ToArray();
            
            if (_searchMatches.Count > 0)
            {
                _currentMatchIndex = 0;
                HighlightCurrentMatch();
            }
            else
            {
                _viewModel.CurrentMatchIndex = 0;
            }
        }

        public void NextMatch()
        {
            if (_searchMatches.Count == 0 || _currentMatchIndex == -1) return;

            _currentMatchIndex++;
            if (_currentMatchIndex >= _searchMatches.Count)
                _currentMatchIndex = 0;

            HighlightCurrentMatch();
        }

        public void PreviousMatch()
        {
            if (_searchMatches.Count == 0 || _currentMatchIndex == -1) return;

            _currentMatchIndex--;
            if (_currentMatchIndex < 0)
                _currentMatchIndex = _searchMatches.Count - 1;

            HighlightCurrentMatch();
        }

        private void HighlightCurrentMatch()
        {
            if (_currentMatchIndex >= 0 && _currentMatchIndex < _searchMatches.Count)
            {
                int start = _searchMatches[_currentMatchIndex];
                int length = _viewModel.SearchString.Length;
                
                _logRTB.Select(start, length);
                var line = _logRTB.Document.GetLineByOffset(start);
                _logRTB.ScrollToLine(line.LineNumber);
                
                _viewModel.CurrentMatchIndex = _currentMatchIndex + 1;
            }
        }

        public void ClearSearch()
        {
            _viewModel.SearchString = string.Empty;
            _searchMatches.Clear();
            _currentMatchIndex = -1;
            _lastSearchText = string.Empty;
            
            // Deselect but don't reset position necessarily, just clear selection
            _logRTB.Select(0, 0); 
        }
        #endregion

        #region Block Navigation
        public void NavigateToBlock(int direction)
        {
            if (_blockStartIndices.Count == 0) return;

            _currentBlockIndex += direction;
            
            if (_currentBlockIndex < 0) _currentBlockIndex = 0;
            if (_currentBlockIndex >= _blockStartIndices.Count) _currentBlockIndex = _blockStartIndices.Count - 1;

            int offset = _blockStartIndices[_currentBlockIndex];
            _logRTB.Select(offset, 0);
            var line = _logRTB.Document.GetLineByOffset(offset);
            _logRTB.ScrollToLine(line.LineNumber);
        }
        #endregion

        private Brush? GetBackgroundBrush(BotLoggerEntry entry, int index)
        {
            if (entry.IsBlockStart) 
            {
                return GetBrush("#16233A"); // Slightly lighter than pure background for blocks
            }

            if (entry.Level == LogLevel.Error)
            {
                return GetBrush("#2D1B1B"); // Deep red for errors
            }

            if (entry.Level == LogLevel.Warning)
            {
                return GetBrush("#2D241B"); // Deep amber for warnings
            }

            // Alternating row background - extremely subtle
            return (index % 2 == 0) ? null : GetBrush("#121212");
        }

        private Brush GetBrush(string hexColor)
        {
            if (_brushCache.TryGetValue(hexColor, out var brush)) return brush;
            
            try 
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                brush = new SolidColorBrush(color);
                brush.Freeze();
                _brushCache[hexColor] = brush;
                return brush;
            } 
            catch { return Brushes.Gainsboro; }
        }

        public void Dispose()
        {
            _brushCache.Clear();
            _segments.Clear();
            _logRTB.TextArea.TextView.LineTransformers.Remove(_colorizer);
        }
    }
}
