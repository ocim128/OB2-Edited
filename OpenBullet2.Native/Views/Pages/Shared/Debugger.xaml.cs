using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Services;
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
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

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
        private readonly AccessibilitySettings _accessibility;
        private Queue<BotLoggerEntry> _pendingEntries;
        private DispatcherTimer _resizeTimer;
        private DispatcherTimer _updateTimer;

        private int _logLineCount;
        private bool _isResizing;
        private bool _updatesPaused;
        private bool _isWindowMinimized;
        private readonly Dictionary<int, System.Drawing.Color> _originalTextColors = new();
        private bool _isSearching;
        private bool _scrollingDisabled;
        private bool _areTabButtonsVisible = false;
        private bool _areOptionsVisible = false;
        private bool _areStackerControlsVisible = false;
        private int _currentBlockIndex = -1;
        private List<int> _blockPositions = new List<int>();
        private bool _windowKeyHandlersAttached = false;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the Debugger page.
        /// </summary>
        public Debugger()
        {
            var settingsService = ServiceLocator.GetService<OpenBulletSettingsService>();
            _accessibility = settingsService.Settings.AccessibilitySettings ?? new AccessibilitySettings();

            _viewModel = ServiceLocator.GetService<ViewModelsService>().Debugger;
            DataContext = _viewModel;

            _viewModel.NewLogEntry += OnNewLogEntry;
            _viewModel.LogCleared += OnLogCleared;

            InitializeComponent();
            InitializeComponents();
            SetupEventHandlers();
            ApplyAccessibilityPreferences();
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes UI components and sets up initial configurations.
        /// </summary>
        private void InitializeComponents()
        {
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnDebuggerKeyDown), true);
            AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnDebuggerKeyDown), true);
            tabControl.SelectedIndex = 0;

            // Configure RichTextBox controls
            ConfigureRichTextBox(logRTB, _accessibility.UseLargeEditorFonts ? 12.5f : 11f);
            ConfigureRichTextBox(variablesRTB, _accessibility.UseLargeEditorFonts ? 12.5f : 11f);

            // Initialize queues and timers
            _pendingEntries = new Queue<BotLoggerEntry>();
            _resizeTimer = CreateTimer(TimeSpan.FromMilliseconds(_accessibility.ReduceAnimations ? RESIZE_DELAY_MS * 2 : RESIZE_DELAY_MS), OnResizeTimerTick);
            _updateTimer = CreateTimer(TimeSpan.FromMilliseconds(_accessibility.ReduceAnimations ? UPDATE_INTERVAL_MS * 2 : UPDATE_INTERVAL_MS), OnUpdateTimerTick);
            _updateTimer.Start();
        }

        /// <summary>
        /// Configures a RichTextBox control with consistent styling.
        /// </summary>
        /// <param name="richTextBox">The RichTextBox to configure.</param>
        private static void ConfigureRichTextBox(System.Windows.Forms.RichTextBox richTextBox, float fontSize)
        {
            richTextBox.Font = new System.Drawing.Font("Consolas", fontSize);
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
            Loaded += OnDebuggerLoaded;
            Unloaded += OnDebuggerUnloaded;
        }

        /// <summary>
        /// Sets up keyboard shortcuts for the debugger.
        /// </summary>
        private void SetupKeyboardShortcuts()
        {
            // Keyboard shortcuts are already handled in OnDebuggerKeyDown
            // No additional setup needed
        }

        private void ApplyAccessibilityPreferences()
        {
            if (_accessibility.UseLargeEditorFonts)
            {
                inputDataTextBox.FontSize = 14;
                searchTextBox.FontSize = 14;
            }

            if (_accessibility.UseComfortableSpacing)
            {
                TabToggleButton.Margin = new Thickness(0, 0, 12, 0);
                OptionsToggleButton.Margin = new Thickness(0, 0, 12, 0);
                StackerToggleButton.Margin = new Thickness(0, 0, 12, 0);
                ConfigurationPanel.Padding = new Thickness(18, 14, 18, 14);
            }

            if (_accessibility.ShowHelpfulTooltips)
            {
                ConfigureTooltip(TabToggleButton, "Show or hide debugger interface elements (Alt+U)");
                ConfigureTooltip(OptionsToggleButton, "Toggle secondary debugger options");
                ConfigureTooltip(StackerToggleButton, "Toggle stacker blocks visibility");
                ConfigureTooltip(StartButton, "Start execution (Alt+S)");
                if (StepButton != null)
                {
                    ConfigureTooltip(StepButton, "Run a single step (Ctrl+Alt+Right)");
                }
                if (StopButton != null)
                {
                    ConfigureTooltip(StopButton, "Stop execution (Alt+X)");
                }
                ConfigureTooltip(searchTextBox, "Search within log output (Ctrl+F)");
            }

            if (_accessibility.AlwaysShowFocusVisuals)
            {
                var focusStyle = Application.Current.TryFindResource("HighVisibilityFocusStyle") as Style;
                if (focusStyle != null)
                {
                    foreach (var control in EnumerateFocusableControls())
                    {
                        control.FocusVisualStyle = focusStyle;
                    }
                }
            }
        }

        private IEnumerable<Control> EnumerateFocusableControls()
        {
            yield return TabToggleButton;
            yield return OptionsToggleButton;
            yield return StackerToggleButton;
            yield return StartButton;

            if (StopButton != null)
            {
                yield return StopButton;
            }

            if (StepButton != null)
            {
                yield return StepButton;
            }

            yield return searchTextBox;
            yield return inputDataTextBox;
        }

        private void ConfigureTooltip(DependencyObject target, string text)
        {
            if (target == null)
            {
                return;
            }

            ToolTipService.SetToolTip(target, text);
            ToolTipService.SetInitialShowDelay(target, 150);
            ToolTipService.SetShowDuration(target, 12000);
            ToolTipService.SetBetweenShowDelay(target, 300);
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
            if (!_updatesPaused && !_isResizing && !_isWindowMinimized)
            {
                ProcessPendingEntries();
            }
        }

        /// <summary>
        /// Handles keyboard shortcuts for improved user experience.
        /// </summary>
        private void OnDebuggerKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Handle shortcuts only once during the preview phase
            if (e.RoutedEvent != Keyboard.PreviewKeyDownEvent)
            {
                return;
            }

            // Check if focus is on a code editor (LoliCode or CSharp) - if so, don't intercept editor shortcuts
            var focusedElement = Keyboard.FocusedElement as System.Windows.UIElement;
            var isEditorFocused = focusedElement != null && (
                focusedElement.GetType().Name.Contains("TextEditor") || // AvalonEdit editor
                focusedElement.ToString().Contains("ICSharpCode.AvalonEdit") // More specific check
            );

            // Handle Alt-based shortcuts using SystemKey (fallback to Key when SystemKey is None)
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                var key = e.SystemKey != Key.None ? e.SystemKey : e.Key;

                switch (key)
                {
                    case System.Windows.Input.Key.A:
                        if (inputDataTextBox != null && inputDataTextBox.IsEnabled)
                        {
                            inputDataTextBox.Focus();
                            inputDataTextBox.SelectAll();
                            e.Handled = true;
                            return;
                        }
                        break;
                    case System.Windows.Input.Key.S:
                        if (StartButton != null && StartButton.IsEnabled && StartButton.IsVisible)
                        {
                            Start(StartButton, new RoutedEventArgs(Button.ClickEvent, StartButton));
                            e.Handled = true;
                            return;
                        }
                        break;
                }
            }

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
                    // Only handle Ctrl+F if not focused on an editor
                    if (!isEditorFocused)
                    {
                        searchTextBox.Focus();
                        e.Handled = true;
                    }
                    // If editor is focused, let the editor handle Ctrl+F for its search
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
            if (_updatesPaused || _isResizing || _isWindowMinimized)
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
                if (ShouldSkipLogEntry()) return;

                Alert.SafeExecute(() =>
                {
                    logRTB.SuspendLayout();
                    AppendLogEntry(entry);
                    HandleAutoScroll();
                    HandleUndoClearing();
                    logRTB.ResumeLayout(false);
                    UpdateVariablesList();
                }, "processing log entry");
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Checks if log entry processing should be skipped.
        /// </summary>
        private bool ShouldSkipLogEntry() => _updatesPaused || _isResizing || _isWindowMinimized;

        /// <summary>
        /// Appends a log entry to the display and updates counters.
        /// </summary>
        private void AppendLogEntry(BotLoggerEntry entry)
        {
            logRTB.AppendText(entry.Message + Environment.NewLine, entry.Color);
            _logLineCount++;
            _viewModel.LogLineCount = _logLineCount;
            TrimLogIfNeeded();
        }

        /// <summary>
        /// Handles auto-scrolling when enabled and no search is active.
        /// </summary>
        private void HandleAutoScroll()
        {
            // Double-check auto-scroll is enabled and not explicitly disabled
            if (!_scrollingDisabled && _viewModel?.IsAutoScrollEnabled == true && _viewModel.Indices.Length == 0)
            {
                logRTB.SelectionStart = logRTB.TextLength;
                logRTB.ScrollToCaret();
            }
        }

        /// <summary>
        /// Clears undo history periodically to prevent memory buildup.
        /// </summary>
        private void HandleUndoClearing()
        {
            if (_logLineCount % CLEAR_UNDO_FREQUENCY == 0)
            {
                logRTB.ClearUndoHistory();
            }
        }

        /// <summary>
        /// Trims the log if it exceeds the maximum line count.
        /// </summary>
        private void TrimLogIfNeeded()
        {
            if (_logLineCount <= MAX_LOG_LINES) return;

            Alert.SafeExecute(() =>
            {
                var lines = logRTB.Lines;
                if (lines.Length <= TRIM_TO_LINES) return;

                var charsToRemove = CalculateCharsToRemove(lines);
                if (charsToRemove > 0 && charsToRemove < logRTB.Text.Length)
                {
                    RemoveLogLines(charsToRemove);
                    UpdateLogCountAfterTrim();
                    HandleAutoScrollAfterTrim();
                }
            }, "trimming log");
        }

        /// <summary>
        /// Calculates the number of characters to remove when trimming the log.
        /// </summary>
        private int CalculateCharsToRemove(string[] lines)
        {
            var linesToRemove = lines.Length - TRIM_TO_LINES;
            var charsToRemove = 0;

            for (int i = 0; i < linesToRemove && i < lines.Length; i++)
            {
                charsToRemove += lines[i].Length + Environment.NewLine.Length;
            }

            return charsToRemove;
        }

        /// <summary>
        /// Removes the specified number of characters from the beginning of the log.
        /// </summary>
        private void RemoveLogLines(int charsToRemove)
        {
            logRTB.Select(0, charsToRemove);
            logRTB.SelectedText = "";
        }

        /// <summary>
        /// Updates the log line count after trimming.
        /// </summary>
        private void UpdateLogCountAfterTrim()
        {
            _logLineCount = TRIM_TO_LINES;
            _viewModel.LogLineCount = _logLineCount;
        }

        /// <summary>
        /// Handles auto-scrolling after log trimming if enabled.
        /// </summary>
        private void HandleAutoScrollAfterTrim()
        {
            // Double-check auto-scroll is enabled and not explicitly disabled
            if (!_scrollingDisabled && _viewModel?.IsAutoScrollEnabled == true)
            {
                logRTB.SelectionStart = logRTB.TextLength;
                logRTB.ScrollToCaret();
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

        /// <summary>
        /// Navigates to the previous block in the log.
        /// </summary>
        private void PreviousBlock(object sender, RoutedEventArgs e)
        {
            NavigateToBlock(-1);
        }

        /// <summary>
        /// Navigates to the next block in the log.
        /// </summary>
        private void NextBlock(object sender, RoutedEventArgs e)
        {
            NavigateToBlock(1);
        }

        /// <summary>
        /// Navigates to a block in the specified direction.
        /// </summary>
        /// <param name="direction">-1 for previous, 1 for next</param>
        private void NavigateToBlock(int direction)
        {
            try
            {
                UpdateBlockPositions();

                if (_blockPositions.Count == 0)
                {
                    return; // No blocks found
                }

                // Find current position in log
                int currentPosition = logRTB.SelectionStart;
                int targetIndex = -1;

                if (direction == 1) // Next block
                {
                    // Find the first block after current position
                    for (int i = 0; i < _blockPositions.Count; i++)
                    {
                        if (_blockPositions[i] > currentPosition)
                        {
                            targetIndex = i;
                            break;
                        }
                    }

                    // If no block found after current position, wrap to first block
                    if (targetIndex == -1 && _blockPositions.Count > 0)
                    {
                        targetIndex = 0;
                    }
                }
                else // Previous block
                {
                    // Find the last block before current position
                    for (int i = _blockPositions.Count - 1; i >= 0; i--)
                    {
                        if (_blockPositions[i] < currentPosition)
                        {
                            targetIndex = i;
                            break;
                        }
                    }

                    // If no block found before current position, wrap to last block
                    if (targetIndex == -1 && _blockPositions.Count > 0)
                    {
                        targetIndex = _blockPositions.Count - 1;
                    }
                }

                // Navigate to the target block
                if (targetIndex >= 0 && targetIndex < _blockPositions.Count)
                {
                    _currentBlockIndex = targetIndex;
                    ScrollToPosition(_blockPositions[targetIndex]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to block: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the list of block positions in the log.
        /// </summary>
        private void UpdateBlockPositions()
        {
            _blockPositions.Clear();

            try
            {
                string logText = logRTB.Text;
                if (string.IsNullOrEmpty(logText))
                {
                    return;
                }

                // Look for block headers with pattern ">> BlockName (caller) <<"
                var lines = logText.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                int position = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    // Check if this line is a block header
                    if (line.StartsWith(">>") && line.EndsWith("<<") && line.Contains("("))
                    {
                        _blockPositions.Add(position);
                    }

                    // Add length of current line plus newline characters
                    position += lines[i].Length;
                    if (i < lines.Length - 1) // Don't add newline after last line
                    {
                        position += Environment.NewLine.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating block positions: {ex.Message}");
            }
        }

        /// <summary>
        /// Scrolls to a specific position in the log.
        /// </summary>
        /// <param name="position">The character position to scroll to</param>
        private void ScrollToPosition(int position)
        {
            try
            {
                if (position >= 0 && position < logRTB.Text.Length)
                {
                    // Select the block header line for visual feedback
                    logRTB.Select(position, 0);
                    logRTB.ScrollToCaret();

                    // Find the end of the line to select the entire header
                    int lineEnd = logRTB.Text.IndexOf('\n', position);
                    if (lineEnd == -1)
                    {
                        lineEnd = logRTB.Text.Length;
                    }

                    // Select the entire block header line
                    logRTB.Select(position, lineEnd - position);
                    logRTB.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scrolling to position: {ex.Message}");
            }
        }
        #endregion

        #region Control Actions
        /// <summary>
        /// Starts the debugging process.
        /// </summary>
        private async void Start(object sender, RoutedEventArgs e)
        {
            try
            {
                logRTB.Clear();
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

        #region Button Event Handlers
        /// <summary>
        /// Toggles the auto-scroll functionality for the log display.
        /// </summary>
        private void ToggleAutoScroll(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleAutoScroll();

            // Set scrolling disabled flag when auto-scroll is turned off
            _scrollingDisabled = !_viewModel.IsAutoScrollEnabled;
        }

        /// <summary>
        /// Toggles the visibility of the options area.
        /// </summary>
        private void ToggleOptions(object sender, RoutedEventArgs e)
        {
            _areOptionsVisible = !_areOptionsVisible;

            // Toggle visibility of the secondary options grid
            SecondaryOptionsGrid.Visibility = _areOptionsVisible ? Visibility.Visible : Visibility.Collapsed;

            // Update the toggle button appearance for better UX
            if (_areOptionsVisible)
            {
                OptionsToggleIcon.Kind = MahApps.Metro.IconPacks.PackIconUniconsKind.EyeSlash;
                ((TextBlock)((StackPanel)OptionsToggleButton.Content).Children[1]).Text = "Hide Options";
                OptionsToggleButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); // Red when visible
            }
            else
            {
                OptionsToggleIcon.Kind = MahApps.Metro.IconPacks.PackIconUniconsKind.Eye;
                ((TextBlock)((StackPanel)OptionsToggleButton.Content).Children[1]).Text = "Show Options";
                OptionsToggleButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)); // Purple when hidden
            }
        }

        /// <summary>
        /// Toggles the visibility of the stacker block list only, keeping block information visible.
        /// </summary>
        private void ToggleStacker(object sender, RoutedEventArgs e)
        {
            // Determine UI state from actual layout instead of relying solely on the flag
            bool isHidden = false;

            // Get reference to the main window and config editor
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var configEditor = mainWindow?.ConfigEditorPage;
            if (configEditor != null)
            {
                // Only toggle if we're on the stacker page
                if (configEditor.IsStackerPageActive())
                {
                    // Get the ConfigStacker page loaded in the editorFrame
                    var editorFrame = configEditor.editorFrame;
                    if (editorFrame?.Content is ConfigStacker configStacker)
                    {
                        // Find the main grid in ConfigStacker
                        var mainGrid = configStacker.Content as Grid;
                        if (mainGrid != null && mainGrid.ColumnDefinitions.Count >= 3)
                        {
                            // Target the block list column (column 0) and splitter column (column 1)
                            var blockListColumn = mainGrid.ColumnDefinitions[0];
                            var splitterColumn = mainGrid.ColumnDefinitions[1];

                            // Find the toolbar (Border in row 0, column 0), block list container (Border row 1, col 0), grid splitter, and inspector
                            Border toolbar = null;
                            Border blockListContainer = null;
                            GridSplitter gridSplitter = null;
                            Border blockInspector = configStacker.BlockInspectorBorder; // Use the named border

                            foreach (var child in mainGrid.Children)
                            {
                                if (child is Border border)
                                {
                                    int row = Grid.GetRow(border);
                                    int col = Grid.GetColumn(border);

                                    if (row == 0 && col == 0)
                                        toolbar = border;
                                    else if (row == 1 && col == 0)
                                        blockListContainer = border;
                                }
                                else if (child is GridSplitter splitter && Grid.GetRow(splitter) == 1 && Grid.GetColumn(splitter) == 1)
                                {
                                    gridSplitter = splitter;
                                }
                            }

                            // Determine current hidden state from actual grid widths/visibility
                            isHidden = blockListColumn.Width.Value <= 0
                                       || (blockListContainer != null && blockListContainer.Visibility != Visibility.Visible);

                            if (isHidden)
                            {
                                // Show stacker - restore original layout
                                blockListColumn.Width = new GridLength(220, GridUnitType.Pixel);
                                splitterColumn.Width = new GridLength(10, GridUnitType.Pixel);
                                if (toolbar != null) toolbar.Visibility = Visibility.Visible;
                                if (blockListContainer != null) blockListContainer.Visibility = Visibility.Visible;
                                if (gridSplitter != null) gridSplitter.Visibility = Visibility.Visible;
                                if (blockInspector != null)
                                {
                                    Grid.SetColumn(blockInspector, 2);
                                    Grid.SetColumnSpan(blockInspector, 1);
                                }
                                _areStackerControlsVisible = true;
                            }
                            else
                            {
                                // Hide stacker - hide block list and expand block info to use full space
                                blockListColumn.Width = new GridLength(0);
                                splitterColumn.Width = new GridLength(0);
                                if (toolbar != null) toolbar.Visibility = Visibility.Collapsed;
                                if (blockListContainer != null) blockListContainer.Visibility = Visibility.Collapsed;
                                if (gridSplitter != null) gridSplitter.Visibility = Visibility.Collapsed;
                                if (blockInspector != null)
                                {
                                    Grid.SetColumn(blockInspector, 0);
                                    Grid.SetColumnSpan(blockInspector, 3);
                                }
                                _areStackerControlsVisible = false;
                            }
                        }
                    }
                }
            }

            // Update the toggle button appearance for better UX
            if (_areStackerControlsVisible)
            {
                StackerToggleIcon.Kind = MahApps.Metro.IconPacks.PackIconUniconsKind.EyeSlash;
                ((TextBlock)((StackPanel)StackerToggleButton.Content).Children[1]).Text = "Hide Stacker";
                StackerToggleButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); // Red when visible
            }
            else
            {
                StackerToggleIcon.Kind = MahApps.Metro.IconPacks.PackIconUniconsKind.Eye;
                ((TextBlock)((StackPanel)StackerToggleButton.Content).Children[1]).Text = "Show Stacker";
                StackerToggleButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)); // Green when hidden
            }
        }

        /// <summary>
        /// Toggles the visibility of the tab buttons and search controls.
        /// </summary>
        private void ToggleTabButtons(object sender, RoutedEventArgs e)
        {
            _areTabButtonsVisible = !_areTabButtonsVisible;

            // Toggle visibility of the three tab buttons
            LogTabButton.Visibility = _areTabButtonsVisible ? Visibility.Visible : Visibility.Collapsed;
            VariablesTabButton.Visibility = _areTabButtonsVisible ? Visibility.Visible : Visibility.Collapsed;
            HtmlTabButton.Visibility = _areTabButtonsVisible ? Visibility.Visible : Visibility.Collapsed;

            // Toggle visibility of the search controls area
            SearchControlsArea.Visibility = _areTabButtonsVisible ? Visibility.Visible : Visibility.Collapsed;

            // Update the toggle button appearance for better UX
            if (_areTabButtonsVisible)
            {
                TabToggleIcon.Kind = MahApps.Metro.IconPacks.PackIconUniconsKind.EyeSlash;
                ((TextBlock)((StackPanel)TabToggleButton.Content).Children[1]).Text = "Hide UI";
                TabToggleButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); // Red when visible
            }
            else
            {
                TabToggleIcon.Kind = MahApps.Metro.IconPacks.PackIconUniconsKind.Eye;
                ((TextBlock)((StackPanel)TabToggleButton.Content).Children[1]).Text = "Show UI";
                TabToggleButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)); // Green when hidden
            }
        }
        #endregion

        /// <summary>
        /// Sets the window minimized state to optimize performance.
        /// </summary>
        /// <param name="isMinimized">True if the window is minimized, false otherwise.</param>
        public void SetWindowMinimized(bool isMinimized)
        {
            _isWindowMinimized = isMinimized;
        }

        /// <summary>
        /// Sets the resizing state to control debugger updates during external resize operations.
        /// </summary>
        /// <param name="isResizing">True if resizing is in progress, false otherwise.</param>
        public void SetResizing(bool isResizing)
        {
            if (isResizing && !_isResizing)
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

                // Start or restart the resize timer
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
            else if (!isResizing && _isResizing)
            {
                // Force immediate completion of resize
                _resizeTimer.Stop();
                OnResizeTimerTick(null, null);
            }
        }

        #region Windows API
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        #endregion

        private void OnDebuggerLoaded(object sender, RoutedEventArgs e)
        {
            // Ensure the Debugger page has keyboard focus on load so Alt shortcuts work without clicking
            try
            {
                Focusable = true;
                // Defer focus to after layout to avoid being overridden by other controls
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Prefer focusing the input box, then fallback to StartButton, then the Page
                    if (inputDataTextBox != null && inputDataTextBox.IsVisible && inputDataTextBox.IsEnabled)
                    {
                        inputDataTextBox.Focus();
                    }
                    else if (StartButton != null && StartButton.IsVisible && StartButton.IsEnabled)
                    {
                        StartButton.Focus();
                    }
                    else
                    {
                        Keyboard.Focus(this);
                        Focus();
                    }

                    var window = Window.GetWindow(this);
                    if (window != null && !_windowKeyHandlersAttached)
                    {
                        window.PreviewKeyDown += OnDebuggerKeyDown;
                        window.KeyDown += OnDebuggerKeyDown;
                        _windowKeyHandlersAttached = true;
                        System.Diagnostics.Debug.WriteLine("[Debugger] Loaded: Attached window-level key handlers.");
                    }

                    System.Diagnostics.Debug.WriteLine("[Debugger] Loaded: Focus prepared for shortcut handling.");
                }), DispatcherPriority.Background);
            }
            catch { }
        }
        private void OnDebuggerUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = Window.GetWindow(this);
                if (window != null && _windowKeyHandlersAttached)
                {
                    window.PreviewKeyDown -= OnDebuggerKeyDown;
                    window.KeyDown -= OnDebuggerKeyDown;
                    _windowKeyHandlersAttached = false;
                    System.Diagnostics.Debug.WriteLine("[Debugger] Unloaded: Detached window-level key handlers.");
                }
            }
            catch { }
        }
    }
}
