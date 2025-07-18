using OpenBullet2.Native.Extensions;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenBullet2.Native.Views.Pages.Shared
{
    /// <summary>
    /// Interaction logic for Debugger.xaml
    /// </summary>
    public partial class Debugger : Page
    {
        #region Constants
        private const int MAX_LOG_LINES = 50000;
        private const int TRIM_TO_LINES = 40000;
        private const int MAX_PENDING_ENTRIES = 500;
        private const int CLEAR_UNDO_FREQUENCY = 100;
        private const int RESIZE_DELAY_MS = 300;
        private const int UPDATE_INTERVAL_MS = 100;
        private const int WM_SETREDRAW = 0x0B;
        #endregion

        #region Private Fields
        private readonly DebuggerViewModel _viewModel;
        private Queue<BotLoggerEntry> _pendingEntries;
        private DispatcherTimer _resizeTimer;
        private DispatcherTimer _updateTimer;

        private int _logLineCount;
        private bool _isResizing;
        private bool _updatesPaused;
        private readonly Dictionary<int, System.Drawing.Color> _originalTextColors = new();
        private bool _isSearching;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the Debugger page.
        /// </summary>
        public Debugger()
        {
            _viewModel = SP.GetService<ViewModelsService>().Debugger;
            DataContext = _viewModel;

            _viewModel.NewLogEntry += OnNewLogEntry;
            _viewModel.LogCleared += OnLogCleared;

            InitializeComponent();
            InitializeComponents();
            SetupEventHandlers();
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes UI components and sets up initial configurations.
        /// </summary>
        private void InitializeComponents()
        {
            KeyDown += OnDebuggerKeyDown;
            tabControl.SelectedIndex = 0;

            // Configure RichTextBox controls
            ConfigureRichTextBox(logRTB);
            ConfigureRichTextBox(variablesRTB);

            // Initialize queues and timers
            _pendingEntries = new Queue<BotLoggerEntry>();
            _resizeTimer = CreateTimer(TimeSpan.FromMilliseconds(RESIZE_DELAY_MS), OnResizeTimerTick);
            _updateTimer = CreateTimer(TimeSpan.FromMilliseconds(UPDATE_INTERVAL_MS), OnUpdateTimerTick);
            _updateTimer.Start();
        }

        /// <summary>
        /// Configures a RichTextBox control with consistent styling.
        /// </summary>
        /// <param name="richTextBox">The RichTextBox to configure.</param>
        private static void ConfigureRichTextBox(System.Windows.Forms.RichTextBox richTextBox)
        {
            richTextBox.Font = new System.Drawing.Font("Consolas", 11f);
            richTextBox.BackColor = System.Drawing.Color.FromArgb(22, 22, 22);
            richTextBox.HandleCreated += (_, _) => FixAutoWordSelection(richTextBox);
        }

        /// <summary>
        /// Creates a dispatcher timer with specified interval and tick handler.
        /// </summary>
        /// <param name="interval">The timer interval.</param>
        /// <param name="tickHandler">The tick event handler.</param>
        /// <returns>A configured DispatcherTimer instance.</returns>
        private static DispatcherTimer CreateTimer(TimeSpan interval, EventHandler tickHandler)
        {
            var timer = new DispatcherTimer { Interval = interval };
            timer.Tick += tickHandler;
            return timer;
        }

        /// <summary>
        /// Sets up event handlers for the page.
        /// </summary>
        private void SetupEventHandlers()
        {
            SizeChanged += OnDebuggerSizeChanged;
        }
        #endregion

        #region UI Configuration
        /// <summary>
        /// Fixes auto word selection issues in RichTextBox controls.
        /// </summary>
        /// <param name="richTextBox">The RichTextBox to fix.</param>
        private static void FixAutoWordSelection(System.Windows.Forms.RichTextBox richTextBox)
        {
            richTextBox.AutoWordSelection = true;
            richTextBox.AutoWordSelection = false;

            richTextBox.WordWrap = true;
            richTextBox.Multiline = true;
            richTextBox.SelectionIndent = 8;
            richTextBox.ZoomFactor = 1.0f;
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the SizeChanged event to optimize performance during resize operations.
        /// </summary>
        private void OnDebuggerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isResizing)
            {
                _isResizing = true;
                _updatesPaused = true;

                try
                {
                    _updateTimer.Stop();
                    SuspendDrawing(logRTB);
                    SuspendDrawing(variablesRTB);
                    logRTB.SuspendLayout();
                    variablesRTB.SuspendLayout();
                }
                catch { }
            }

            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        /// <summary>
        /// Handles the resize timer tick event to resume normal operations.
        /// </summary>
        private void OnResizeTimerTick(object sender, EventArgs e)
        {
            _resizeTimer.Stop();
            _isResizing = false;
            _updatesPaused = false;

            try
            {
                ResumeDrawing(logRTB);
                ResumeDrawing(variablesRTB);
                logRTB.ResumeLayout(false);
                variablesRTB.ResumeLayout(false);

                _updateTimer.Start();
                ProcessPendingEntries();
            }
            catch { }
        }

        /// <summary>
        /// Handles the update timer tick event for processing pending log entries.
        /// </summary>
        private void OnUpdateTimerTick(object sender, EventArgs e)
        {
            if (!_updatesPaused && !_isResizing)
            {
                ProcessPendingEntries();
            }
        }

        /// <summary>
        /// Handles keyboard shortcuts for improved user experience.
        /// </summary>
        private void OnDebuggerKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.F3 when Keyboard.Modifiers == ModifierKeys.Shift:
                    PreviousMatch(null, null);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.F3:
                    NextMatch(null, null);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                    searchTextBox.Focus();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                    ClearLog(null, null);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Escape when !string.IsNullOrEmpty(_viewModel.SearchString):
                    ClearSearch(null, null);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Enter when searchTextBox.IsFocused:
                    Search(null, null);
                    e.Handled = true;
                    break;
            }
        }
        #endregion

        #region Log Management
        /// <summary>
        /// Handles new log entries from the view model.
        /// </summary>
        private void OnNewLogEntry(object sender, BotLoggerEntry entry)
        {
            if (_updatesPaused || _isResizing)
            {
                lock (_pendingEntries)
                {
                    _pendingEntries.Enqueue(entry);
                    if (_pendingEntries.Count > MAX_PENDING_ENTRIES)
                    {
                        _pendingEntries.Dequeue();
                    }
                }
                return;
            }

            ProcessLogEntry(entry);
        }

        /// <summary>
        /// Processes a single log entry for display.
        /// </summary>
        /// <param name="entry">The log entry to process.</param>
        private void ProcessLogEntry(BotLoggerEntry entry)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_updatesPaused || _isResizing) return;

                    logRTB.SuspendLayout();
                    logRTB.AppendText(entry.Message + Environment.NewLine, entry.Color);
                    _logLineCount++;
                    _viewModel.LogLineCount = _logLineCount;

                    TrimLogIfNeeded();

                    if (_viewModel.Indices.Length == 0)
                    {
                        logRTB.SelectionStart = logRTB.TextLength;
                        logRTB.ScrollToCaret();
                    }

                    if (_logLineCount % CLEAR_UNDO_FREQUENCY == 0)
                    {
                        logRTB.ClearUndoHistory();
                    }

                    logRTB.ResumeLayout(false);
                    UpdateVariablesList();
                }
                catch
                {
                    try { logRTB.ResumeLayout(false); } catch { }
                }
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Trims the log if it exceeds the maximum line count.
        /// </summary>
        private void TrimLogIfNeeded()
        {
            if (_logLineCount <= MAX_LOG_LINES) return;

            try
            {
                var text = logRTB.Text;
                var lines = logRTB.Lines;

                if (lines.Length > TRIM_TO_LINES)
                {
                    var linesToRemove = lines.Length - TRIM_TO_LINES;
                    var charsToRemove = 0;

                    for (int i = 0; i < linesToRemove && i < lines.Length; i++)
                    {
                        charsToRemove += lines[i].Length + Environment.NewLine.Length;
                    }

                    if (charsToRemove > 0 && charsToRemove < text.Length)
                    {
                        logRTB.Select(0, charsToRemove);
                        logRTB.SelectedText = "";
                        _logLineCount = TRIM_TO_LINES;
                        _viewModel.LogLineCount = _logLineCount;

                        logRTB.SelectionStart = logRTB.TextLength;
                        logRTB.ScrollToCaret();
                    }
                }
            }
            catch
            {
                logRTB.Clear();
                _logLineCount = 0;
                _viewModel.LogLineCount = 0;
            }
        }

        /// <summary>
        /// Clears the log content.
        /// </summary>
        private void OnLogCleared(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    logRTB.SuspendLayout();
                    variablesRTB.SuspendLayout();

                    logRTB.Clear();
                    variablesRTB.Clear();
                    htmlViewer.HTML = string.Empty;

                    _logLineCount = 0;
                    _viewModel.LogLineCount = 0;
                    _viewModel.Indices = Array.Empty<int>();
                    _viewModel.CurrentMatchIndex = 0;

                    lock (_pendingEntries)
                    {
                        _pendingEntries.Clear();
                    }

                    logRTB.ResumeLayout(true);
                    variablesRTB.ResumeLayout(true);
                }
                catch
                {
                    try { logRTB.ResumeLayout(true); } catch { }
                }
            }, DispatcherPriority.Background);
        }
        #endregion

        #region Tab Management
        /// <summary>
        /// Shows the log tab.
        /// </summary>
        private void ShowLog(object sender, RoutedEventArgs e) => tabControl.SelectedIndex = 0;

        /// <summary>
        /// Shows the variables tab and updates the variables list.
        /// </summary>
        private void ShowVariables(object sender, RoutedEventArgs e)
        {
            tabControl.SelectedIndex = 1;
            UpdateVariablesList();
        }

        /// <summary>
        /// Shows the HTML tab.
        /// </summary>
        private void ShowHTML(object sender, RoutedEventArgs e) => tabControl.SelectedIndex = 2;
        #endregion

        #region Search Functionality
        /// <summary>
        /// Searches for text in the log.
        /// </summary>
        private void Search(object sender, RoutedEventArgs e)
        {
            ClearPreviousSearch();

            if (string.IsNullOrWhiteSpace(_viewModel.SearchString))
            {
                _viewModel.Indices = Array.Empty<int>();
                _isSearching = false;
                return;
            }

            _isSearching = true;
            var indices = new List<int>();
            var text = logRTB.Text;
            var search = _viewModel.SearchString;
            var startIndex = 0;

            while ((startIndex = text.IndexOf(search, startIndex, StringComparison.InvariantCultureIgnoreCase)) != -1)
            {
                indices.Add(startIndex);
                startIndex += search.Length;
            }

            _viewModel.Indices = indices.ToArray();
            logRTB.SelectionStart = logRTB.TextLength;
            logRTB.ScrollToCaret();
        }

        /// <summary>
        /// Navigates to the previous search match.
        /// </summary>
        private void PreviousMatch(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Indices.Length == 0) return;

            _viewModel.CurrentMatchIndex = _viewModel.CurrentMatchIndex == 0
                ? _viewModel.Indices.Length - 1
                : _viewModel.CurrentMatchIndex - 1;

            NavigateToMatch();
        }

        /// <summary>
        /// Navigates to the next search match.
        /// </summary>
        private void NextMatch(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Indices.Length == 0) return;

            _viewModel.CurrentMatchIndex = _viewModel.CurrentMatchIndex == _viewModel.Indices.Length - 1
                ? 0
                : _viewModel.CurrentMatchIndex + 1;

            NavigateToMatch();
        }

        /// <summary>
        /// Navigates to the current search match.
        /// </summary>
        private void NavigateToMatch()
        {
            if (_viewModel.Indices.Length == 0) return;

            logRTB.DeselectAll();
            logRTB.Select(_viewModel.Indices[_viewModel.CurrentMatchIndex], _viewModel.SearchString.Length);
            logRTB.ScrollToCaret();
            logRTB.Focus();
        }

        /// <summary>
        /// Clears the previous search results.
        /// </summary>
        private void ClearPreviousSearch()
        {
            if (_viewModel.Indices.Length == 0) return;

            foreach (var index in _viewModel.Indices)
            {
                if (index >= 0 && index < logRTB.Text.Length)
                {
                    logRTB.Select(index, _viewModel.SearchString.Length);
                    logRTB.SelectionBackColor = System.Drawing.Color.FromArgb(22, 22, 22);
                }
            }

            _originalTextColors.Clear();
            _viewModel.Indices = Array.Empty<int>();
            _viewModel.CurrentMatchIndex = 0;
            logRTB.DeselectAll();
        }

        /// <summary>
        /// Clears the current search.
        /// </summary>
        private void ClearSearch(object sender, RoutedEventArgs e)
        {
            ClearPreviousSearch();
            _viewModel.SearchString = string.Empty;
            _isSearching = false;
        }

        /// <summary>
        /// Clears the log content.
        /// </summary>
        private void ClearLog(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearLog();
        }
        #endregion

        #region Control Actions
        /// <summary>
        /// Starts the debugging process.
        /// </summary>
        private async void Start(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.PersistLog)
            {
                logRTB.Clear();
            }

            try
            {
                await _viewModel.RunAsync();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        /// <summary>
        /// Takes a single debugging step.
        /// </summary>
        private void TakeStep(object sender, RoutedEventArgs e) => _viewModel.TakeStep();

        /// <summary>
        /// Stops the debugging process.
        /// </summary>
        private void Stop(object sender, RoutedEventArgs e) => _viewModel.Stop();
        #endregion

        #region Helper Methods
        /// <summary>
        /// Suspends drawing for a RichTextBox control.
        /// </summary>
        /// <param name="richTextBox">The RichTextBox to suspend.</param>
        private static void SuspendDrawing(System.Windows.Forms.RichTextBox richTextBox)
        {
            SendMessage(richTextBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Resumes drawing for a RichTextBox control.
        /// </summary>
        /// <param name="richTextBox">The RichTextBox to resume.</param>
        private static void ResumeDrawing(System.Windows.Forms.RichTextBox richTextBox)
        {
            SendMessage(richTextBox.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
        }

        /// <summary>
        /// Processes pending log entries in the queue.
        /// </summary>
        private void ProcessPendingEntries()
        {
            try
            {
                var entriesToProcess = new List<BotLoggerEntry>();

                lock (_pendingEntries)
                {
                    for (int i = 0; i < 10 && _pendingEntries.Count > 0; i++)
                    {
                        entriesToProcess.Add(_pendingEntries.Dequeue());
                    }
                }

                foreach (var entry in entriesToProcess)
                {
                    ProcessLogEntry(entry);
                }
            }
            catch { }
        }

        /// <summary>
        /// Updates the variables list display.
        /// </summary>
        private void UpdateVariablesList()
        {
            if (tabControl.SelectedIndex != 1) return;

            try
            {
                variablesRTB.SuspendLayout();
                variablesRTB.Clear();

                foreach (var variable in _viewModel.Variables)
                {
                    var color = variable.MarkedForCapture ? System.Drawing.Color.Tomato : System.Drawing.Color.Yellow;
                    variablesRTB.SelectionColor = color;
                    variablesRTB.AppendText($"{variable.Name} ({variable.Type}) = {variable.AsString()}{Environment.NewLine}");
                }

                variablesRTB.ResumeLayout(false);
                variablesRTB.ClearUndoHistory();
            }
            catch
            {
                try { variablesRTB.ResumeLayout(false); } catch { }
            }
        }
        #endregion

        #region Windows API
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        #endregion
    }
}
