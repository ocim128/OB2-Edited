using ICSharpCode.AvalonEdit.Document;
using OpenBullet2.Native.Views.Pages.Shared;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenBullet2.Native.ViewModels.Base;

namespace OpenBullet2.Native.Views.Dialogs
{
    /// <summary>
    /// Modern Bot Log Dialog with AvalonEdit, search navigation, and view options.
    /// </summary>
    public partial class BotLogDialog : Page
    {
        private readonly BotLogDialogViewModel vm;
        private readonly List<LogSegment> _segments = new();
        private readonly DebuggerLogColorizer _colorizer;
        private readonly Dictionary<string, Brush> _brushCache = new();
        
        private readonly List<int> _searchMatches = new();
        private double _currentFontSize = 13;
        private const double MinFontSize = 9;
        private const double MaxFontSize = 24;

        public BotLogDialog(IBotLogger logger)
        {
            vm = new BotLogDialogViewModel();
            DataContext = vm;

            InitializeComponent();
            
            // Initialize Syntax Highlighting
            _colorizer = new DebuggerLogColorizer(_segments);
            logRTB.TextArea.TextView.LineTransformers.Add(_colorizer);

            if (logger is null)
            {
                AppendLog("Bot log was not enabled when this hit was obtained" + Environment.NewLine, "#FF6347"); // Tomato
                vm.EntryCount = 0;
                return;
            }

            var sb = new System.Text.StringBuilder();
            int currentOffset = 0;

            foreach (var entry in logger.Entries)
            {
                var line = entry.Message + Environment.NewLine;
                sb.Append(line);
                
                var brush = GetBrush(entry.Color);
                _segments.Add(new LogSegment
                {
                    StartOffset = currentOffset,
                    Length = line.Length,
                    Foreground = brush,
                    Background = null,
                    FontWeight = FontWeights.Normal
                });

                currentOffset += line.Length;
            }
            
            logRTB.Text = sb.ToString();
            vm.EntryCount = logger.Entries.Count();
            
            try
            {
                logRTB.ScrollToEnd();
            }
            catch { }
        }
        
        private void AppendLog(string text, string hexColor)
        {
            int startOffset = logRTB.Document.TextLength;
            logRTB.AppendText(text);
            
            var brush = GetBrush(hexColor);
            _segments.Add(new LogSegment
            {
                StartOffset = startOffset,
                Length = text.Length,
                Foreground = brush,
                Background = null,
                FontWeight = FontWeights.Normal
            });
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

        #region View Options
        private void ToggleWordWrap(object sender, RoutedEventArgs e)
        {
            logRTB.WordWrap = wordWrapToggle.IsChecked == true;
        }
        
        private void ToggleLineNumbers(object sender, RoutedEventArgs e)
        {
            logRTB.ShowLineNumbers = lineNumbersToggle.IsChecked == true;
        }
        
        private void IncreaseFontSize(object sender, RoutedEventArgs e)
        {
            if (_currentFontSize < MaxFontSize)
            {
                _currentFontSize++;
                logRTB.FontSize = _currentFontSize;
                fontSizeDisplay.Text = _currentFontSize.ToString();
            }
        }
        
        private void DecreaseFontSize(object sender, RoutedEventArgs e)
        {
            if (_currentFontSize > MinFontSize)
            {
                _currentFontSize--;
                logRTB.FontSize = _currentFontSize;
                fontSizeDisplay.Text = _currentFontSize.ToString();
            }
        }
        
        private void CopyAll(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(logRTB.Text);
            }
            catch { }
        }
        
        private void ScrollToTop(object sender, RoutedEventArgs e)
        {
            logRTB.ScrollToHome();
        }
        
        private void ScrollToBottom(object sender, RoutedEventArgs e)
        {
            logRTB.ScrollToEnd();
        }
        #endregion

        #region Search
        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Search(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.F3)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    PreviousMatch(sender, e);
                }
                else
                {
                    NextMatch(sender, e);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _searchMatches.Clear();
                vm.Indices = Array.Empty<int>();
                vm.SearchString = string.Empty;
            }
        }
        
        private void Search(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(vm.SearchString))
            {
                _searchMatches.Clear();
                vm.Indices = Array.Empty<int>();
                return;
            }

            _searchMatches.Clear();
            var docText = logRTB.Document.Text;
            int index = 0;
            
            while ((index = docText.IndexOf(vm.SearchString, index, StringComparison.InvariantCultureIgnoreCase)) != -1)
            {
                _searchMatches.Add(index);
                index += vm.SearchString.Length;
            }

            vm.Indices = _searchMatches.ToArray();
            
            if (_searchMatches.Count > 0)
            {
                // Select first match
                SelectMatch(0);
            }
        }

        private void PreviousMatch(object sender, RoutedEventArgs e)
        {
            if (vm.Indices.Length == 0) return;

            int newIndex = vm.CurrentMatchIndex - 1;
            if (newIndex < 0) newIndex = vm.Indices.Length - 1;
            
            SelectMatch(newIndex);
        }

        private void NextMatch(object sender, RoutedEventArgs e)
        {
            if (vm.Indices.Length == 0) return;

            int newIndex = vm.CurrentMatchIndex + 1;
            if (newIndex >= vm.Indices.Length) newIndex = 0;
            
            SelectMatch(newIndex);
        }
        
        private void SelectMatch(int index)
        {
            vm.CurrentMatchIndex = index;
            int textIndex = vm.Indices[index];
            
            logRTB.Select(textIndex, vm.SearchString.Length);
            var line = logRTB.Document.GetLineByOffset(textIndex);
            logRTB.ScrollToLine(line.LineNumber);
            
            // Focus the editor to show selection highlight
            logRTB.Focus();
        }
        #endregion
    }
    
    public class BotLogDialogViewModel : ViewModelBase
    {
        private string searchString = string.Empty;
        public string SearchString
        {
            get => searchString;
            set
            {
                searchString = value;
                OnPropertyChanged();
            }
        }
        
        private int entryCount;
        public int EntryCount
        {
            get => entryCount;
            set
            {
                entryCount = value;
                OnPropertyChanged();
            }
        }

        private int[] indices = Array.Empty<int>();
        public int[] Indices
        {
            get => indices;
            set
            {
                indices = value;
                CurrentMatchIndex = 0;
                OnPropertyChanged(nameof(MatchInfo));
            }
        }

        private int currentMatchIndex;
        public int CurrentMatchIndex
        {
            get => currentMatchIndex;
            set
            {
                currentMatchIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MatchInfo));
            }
        }

        public string MatchInfo => Indices.Length == 0 
            ? "No matches" 
            : $"{CurrentMatchIndex + 1} of {Indices.Length}";
    }
}
