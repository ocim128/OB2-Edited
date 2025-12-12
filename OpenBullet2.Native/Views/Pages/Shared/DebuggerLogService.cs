using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using OpenBullet2.Native.ViewModels;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OpenBullet2.Native.Views.Pages.Shared
{
    public class DebuggerLogService : IDisposable
    {
        private readonly TextEditor _logRTB;
        private readonly TextEditor _variablesRTB;
        private readonly DebuggerViewModel _viewModel;
        
        // State for syntax highlighting
        private readonly List<LogSegment> _segments = new();
        private readonly DebuggerLogColorizer _colorizer;
        
        private readonly List<LogSegment> _variableSegments = new();
        private readonly DebuggerLogColorizer _variableColorizer;
        
        private readonly Dictionary<string, Brush> _brushCache = new();

        // State for search
        private readonly List<int> _searchMatches = new();
        private int _currentMatchIndex = -1;
        private string _lastSearchText = string.Empty;

        // State for block navigation
        private readonly List<int> _blockStartIndices = new();
        private int _currentBlockIndex = -1;

        // UI State flags
        public bool UpdatesPaused { get; set; }
        public bool IsResizing { get; set; }
        public bool IsWindowMinimized { get; set; }
        public bool ScrollingDisabled { get; set; }

        private readonly object _lock = new();

        public DebuggerLogService(TextEditor logRTB, TextEditor variablesRTB, DebuggerViewModel viewModel)
        {
            _logRTB = logRTB;
            _variablesRTB = variablesRTB;
            _viewModel = viewModel;

            // Initialize Syntax Highlighting for Log
            _colorizer = new DebuggerLogColorizer(_segments);
            _logRTB.TextArea.TextView.LineTransformers.Add(_colorizer);
            
            // Initialize Syntax Highlighting for Variables
            _variableColorizer = new DebuggerLogColorizer(_variableSegments);
            _variablesRTB.TextArea.TextView.LineTransformers.Add(_variableColorizer);
        }

        public void HandleNewLogEntry(BotLoggerEntry entry)
        {
            if (UpdatesPaused || IsWindowMinimized || IsResizing)
            {
                // Buffering logic placeholder
            }

            // Append Text
            string textToAppend = entry.Message + Environment.NewLine;
            int startOffset = _logRTB.Document.TextLength;
            
            _logRTB.AppendText(textToAppend);

            // Add Highlighting Segment
            var brush = GetBrush(entry.Color);
            _segments.Add(new LogSegment
            {
                StartOffset = startOffset,
                Length = textToAppend.Length, 
                Color = brush
            });
            
            // Auto-scroll
            if (!ScrollingDisabled)
            {
                _logRTB.ScrollToEnd();
            }
        }

        public void ProcessPendingEntries()
        {
            // Buffering placeholder
        }

        public void UpdateVariablesList()
        {
            _variablesRTB.Clear();
            _variableSegments.Clear();
            
            foreach (var variable in _viewModel.Variables)
            {
                // Simple formatting
                var text = $"{variable.Name} ({variable.Type}) = {variable.AsString()}{Environment.NewLine}";
                
                // Colorize based on MarkedForCapture
                var color = variable.MarkedForCapture ? Brushes.Tomato : Brushes.Gainsboro;
                
                int startOffset = _variablesRTB.Document.TextLength;
                _variablesRTB.AppendText(text);
                
                _variableSegments.Add(new LogSegment
                {
                    StartOffset = startOffset,
                    Length = text.Length,
                    Color = color
                });
            }
            
            _variablesRTB.TextArea.TextView.Redraw();
        }

        public void ClearLog(Action<string>? htmlClearAction = null)
        {
            _logRTB.Clear();
            _segments.Clear();
            _searchMatches.Clear();
            _blockStartIndices.Clear();
            _currentMatchIndex = -1;
            _currentBlockIndex = -1;
            
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
            _logRTB.Select(0, 0); 
        }
        #endregion

        #region Block Navigation
        public void NavigateToBlock(int direction)
        {
            if (_blockStartIndices.Count == 0)
            {
                UpdateBlockPositions();
            }
            
            if (_blockStartIndices.Count == 0) return;

            _currentBlockIndex += direction;
            
            if (_currentBlockIndex < 0) _currentBlockIndex = 0;
            if (_currentBlockIndex >= _blockStartIndices.Count) _currentBlockIndex = _blockStartIndices.Count - 1;

            int offset = _blockStartIndices[_currentBlockIndex];
            _logRTB.Select(offset, 0);
            var line = _logRTB.Document.GetLineByOffset(offset);
            _logRTB.ScrollToLine(line.LineNumber);
        }

        private void UpdateBlockPositions()
        {
            _blockStartIndices.Clear();
            var text = _logRTB.Document.Text;
            
            // Look for "Executing block " pattern
            string blockMarker = "Executing block ";
            int index = 0;
            while ((index = text.IndexOf(blockMarker, index, StringComparison.Ordinal)) != -1)
            {
                _blockStartIndices.Add(index);
                index += blockMarker.Length;
            }
        }
        #endregion

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
            _variableSegments.Clear();
            _logRTB.TextArea.TextView.LineTransformers.Remove(_colorizer);
            _variablesRTB.TextArea.TextView.LineTransformers.Remove(_variableColorizer);
        }
    }
}
