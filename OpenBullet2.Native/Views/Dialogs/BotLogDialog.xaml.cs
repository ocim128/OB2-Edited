using ICSharpCode.AvalonEdit.Document;
using OpenBullet2.Native.Views.Pages.Shared;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenBullet2.Native.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for BotLogDialog.xaml
    /// </summary>
    public partial class BotLogDialog : Page
    {
        private readonly BotLogDialogViewModel vm;
        private readonly List<LogSegment> _segments = new();
        private readonly DebuggerLogColorizer _colorizer;
        private readonly Dictionary<string, Brush> _brushCache = new();
        
        private readonly List<int> _searchMatches = new();

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
                return;
            }

            foreach (var entry in logger.Entries)
            {
                AppendLog(entry.Message + Environment.NewLine, entry.Color);
            }
            
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
                Color = brush
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

        #region Search
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
        }
        #endregion
    }
    
    public class BotLogDialogViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
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

        private int[] indices = Array.Empty<int>();
        public int[] Indices
        {
            get => indices;
            set
            {
                indices = value;
                CurrentMatchIndex = 0;
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

        public string MatchInfo => $"{CurrentMatchIndex + 1} of {Indices.Length}";
    }
}
