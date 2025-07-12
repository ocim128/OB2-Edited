using OpenBullet2.Native.Extensions;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace OpenBullet2.Native.Views.Pages.Shared
{
    /// <summary>
    /// Interaction logic for Debugger.xaml
    /// </summary>
    public partial class Debugger : Page
    {
        private readonly DebuggerViewModel vm;
        private const int MAX_LOG_LINES = 50000; // Increased limit to keep all blocks 
        private const int TRIM_TO_LINES = 40000;  // Trim to this number when max is reached
        private int logLineCount;
        private bool isResizing;
        private bool updatesPaused;
        private System.Windows.Threading.DispatcherTimer resizeTimer;
        private Queue<BotLoggerEntry> pendingEntries;
        private System.Windows.Threading.DispatcherTimer updateTimer;

        public Debugger()
        {
            vm = SP.GetService<ViewModelsService>().Debugger;
            DataContext = vm;

            vm.NewLogEntry += NewLogEntry;
            vm.LogCleared += ClearLog;

            InitializeComponent();
            
            // Add keyboard shortcuts for better UX
            this.KeyDown += Debugger_KeyDown;
            tabControl.SelectedIndex = 0;

            // Enhanced font settings for better readability while keeping original colors
            logRTB.Font = new System.Drawing.Font("Consolas", 11f);
            logRTB.BackColor = System.Drawing.Color.FromArgb(22, 22, 22);
            logRTB.HandleCreated += (_, _) => FixAutoWordSelection(logRTB);
            
            variablesRTB.Font = new System.Drawing.Font("Consolas", 11f);
            variablesRTB.BackColor = System.Drawing.Color.FromArgb(22, 22, 22);
            variablesRTB.HandleCreated += (_, _) => FixAutoWordSelection(variablesRTB);

            // Initialize pending entries queue for batching
            pendingEntries = new Queue<BotLoggerEntry>();

            // Initialize resize timer for performance optimization
            resizeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            resizeTimer.Tick += ResizeTimer_Tick;

            // Initialize update timer for batching log entries
            updateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();

            // Performance optimization: suspend layout during resize
            this.SizeChanged += Debugger_SizeChanged;
        }

        private void FixAutoWordSelection(System.Windows.Forms.RichTextBox rtb)
        {
            // Stupid ass workaround because WinForms RichTextBox is broken
            // https://stackoverflow.com/questions/3678620/c-sharp-richtextbox-selection-problem
            rtb.AutoWordSelection = true;
            rtb.AutoWordSelection = false;
            
            // Enhanced comfort settings for better readability
            rtb.WordWrap = true;
            rtb.Multiline = true;
            rtb.SelectionIndent = 8;
            rtb.SelectionHangingIndent = 0;
            rtb.ZoomFactor = 1.0f;
        }

        private void Debugger_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Completely pause all updates during resize operations
            if (!isResizing)
            {
                isResizing = true;
                updatesPaused = true;
                
                // Aggressive performance: completely disable RichTextBox updates
                try
                {
                    // Stop all timers during resize
                    updateTimer.Stop();
                    
                    // Use Windows API to suspend drawing
                    SendMessage(logRTB.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(variablesRTB.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                    
                    logRTB.SuspendLayout();
                    variablesRTB.SuspendLayout();
                }
                catch { }
            }

            // Reset timer with each resize event
            resizeTimer.Stop();
            resizeTimer.Start();
        }

        private void ResizeTimer_Tick(object sender, EventArgs e)
        {
            // Resume all operations after resize is complete
            resizeTimer.Stop();
            isResizing = false;
            updatesPaused = false;

            try
            {
                // Resume drawing and layout
                logRTB.ResumeLayout(false);
                variablesRTB.ResumeLayout(false);
                
                // Re-enable drawing
                SendMessage(logRTB.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                SendMessage(variablesRTB.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                
                // Force redraw
                logRTB.Invalidate();
                variablesRTB.Invalidate();
                
                // Restart update timer
                updateTimer.Start();
                
                // Process any pending entries
                ProcessPendingEntries();
            }
            catch { }
        }

        // Windows API for better performance control
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WM_SETREDRAW = 0x0B;

        private void TrimLogIfNeeded()
        {
            // Performance optimization: limit log size to prevent UI freezing
            if (logLineCount > MAX_LOG_LINES)
            {
                try
                {
                    // Instead of trimming lines (which loses colors), trim by removing characters from start
                    // This preserves the RTF formatting and colors for recent entries
                    var text = logRTB.Text;
                    var lines = logRTB.Lines;
                    
                    if (lines.Length > TRIM_TO_LINES)
                    {
                        // Calculate how many characters to remove from the beginning
                        var linesToRemove = lines.Length - TRIM_TO_LINES;
                        var charsToRemove = 0;
                        
                        for (int i = 0; i < linesToRemove && i < lines.Length; i++)
                        {
                            charsToRemove += lines[i].Length + Environment.NewLine.Length;
                        }
                        
                        // Remove text from beginning while preserving formatting
                        if (charsToRemove > 0 && charsToRemove < text.Length)
                        {
                            logRTB.Select(0, charsToRemove);
                            logRTB.SelectedText = "";
                            logLineCount = TRIM_TO_LINES;
                        }
                        
                        // Update ViewModel with current line count for UI binding
                        vm.LogLineCount = logLineCount;
                        
                        // Move cursor to end
                        logRTB.SelectionStart = logRTB.TextLength;
                        logRTB.ScrollToCaret();
                    }
                }
                catch
                {
                    // Fallback: just clear if trimming fails
                    logRTB.Clear();
                    logLineCount = 0;
                    vm.LogLineCount = 0;
                }
            }
        }

        private void ShowLog(object sender, RoutedEventArgs e) 
        {
            tabControl.SelectedIndex = 0;
        }

        private void ShowVariables(object sender, RoutedEventArgs e) 
        {
            tabControl.SelectedIndex = 1;
            // Update variables list when switching to variables tab
            UpdateVariablesList();
        }

        private void ShowHTML(object sender, RoutedEventArgs e) 
        {
            tabControl.SelectedIndex = 2;
        }

        private async void Start(object sender, RoutedEventArgs e)
        {   
            if (!vm.PersistLog)
            {
                logRTB.Clear();
            }

            try
            {
                await vm.RunAsync();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void TakeStep(object sender, RoutedEventArgs e) => vm.TakeStep();

        private void Stop(object sender, RoutedEventArgs e) => vm.Stop();

        private void NewLogEntry(object sender, BotLoggerEntry entry)
        {
            try
            {
                // If updates are paused or resizing, queue the entry for later processing
                if (updatesPaused || isResizing)
                {
                    lock (pendingEntries)
                    {
                        pendingEntries.Enqueue(entry);
                        // Limit queue size for memory management
                        if (pendingEntries.Count > 500)
                        {
                            pendingEntries.Dequeue();
                        }
                    }
                    return;
                }

                ProcessLogEntry(entry);
            }
            catch { }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Process batched entries for better performance
            if (!updatesPaused && !isResizing)
            {
                ProcessPendingEntries();
            }
        }

        private void ProcessPendingEntries()
        {
            try
            {
                var entriesToProcess = new List<BotLoggerEntry>();
                
                lock (pendingEntries)
                {
                    // Process up to 10 entries at a time for smooth UI
                    for (int i = 0; i < 10 && pendingEntries.Count > 0; i++)
                    {
                        entriesToProcess.Add(pendingEntries.Dequeue());
                    }
                }

                foreach (var entry in entriesToProcess)
                {
                    ProcessLogEntry(entry);
                }
            }
            catch { }
        }

        private void ProcessLogEntry(BotLoggerEntry entry)
        {
            // Use BeginInvoke for better performance with high-frequency updates
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Skip if paused (double check)
                    if (updatesPaused || isResizing)
                        return;

                    // Suspend layout for batch operations
                    logRTB.SuspendLayout();

                    // Append the log message with improved formatting
                                                logRTB.AppendText(entry.Message + Environment.NewLine, entry.Color);
                            logLineCount++;

                            // Update ViewModel with current line count for UI binding
                            vm.LogLineCount = logLineCount;

                            // Trim log if it gets too large (performance optimization)
                            TrimLogIfNeeded();

                    // Scroll to the bottom of the log (only if not searching)
                    if (vm.Indices.Length == 0)
                    {
                        logRTB.SelectionStart = logRTB.TextLength;
                        logRTB.ScrollToCaret();
                    }

                    // Clear undo history for memory efficiency (less frequently to preserve colors)
                    if (logLineCount % 100 == 0) // Only clear every 100 lines to preserve formatting
                    {
                        logRTB.ClearUndoHistory();
                    }

                    // Resume layout
                    logRTB.ResumeLayout(false);

                    // Update variables list (less frequently for performance)
                    UpdateVariablesList();

                    // Update the HTML view only if it can be viewed as HTML
                    if (entry.CanViewAsHtml && tabControl.SelectedIndex == 2)
                    {
                        htmlViewer.HTML = entry.Message;
                    }
                }
                catch
                {
                    // Ensure layout is resumed even if there's an error
                    try { logRTB.ResumeLayout(false); } catch { }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateVariablesList()
        {
            // Update variables only if the Variables tab is selected (performance optimization)
            if (tabControl.SelectedIndex != 1)
                return;

            try
            {
                variablesRTB.SuspendLayout();
                variablesRTB.Clear();

                foreach (var variable in vm.Variables)
                {
                    var color = variable.MarkedForCapture ? LogColors.Tomato : LogColors.Yellow;
                    variablesRTB.AppendText($"{variable.Name} ({variable.Type}) = {variable.AsString()}" + Environment.NewLine, color);
                }

                variablesRTB.ResumeLayout(false);
                variablesRTB.ClearUndoHistory();
            }
            catch
            {
                try { variablesRTB.ResumeLayout(false); } catch { }
            }
        }

        private void ClearLog(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Suspend layout for better performance
                    logRTB.SuspendLayout();
                    variablesRTB.SuspendLayout();

                    // Clear all content
                    logRTB.Clear();
                    variablesRTB.Clear();
                    htmlViewer.HTML = string.Empty;

                    // Reset all search state to prevent color issues
                    originalTextColors.Clear();
                    isSearching = false;
                    vm.SearchString = string.Empty;

                    // Reset counters
                    logLineCount = 0;
                    vm.Indices = new int[0];
                    vm.CurrentMatchIndex = 0;

                    // Clear any pending entries to avoid stale data
                    lock (pendingEntries)
                    {
                        pendingEntries.Clear();
                    }

                    // Resume layout
                    logRTB.ResumeLayout(true);
                    variablesRTB.ResumeLayout(true);
                }
                catch
                {
                    // Ensure layout is resumed even if there's an error
                    try 
                    { 
                        logRTB.ResumeLayout(true); 
                        variablesRTB.ResumeLayout(true); 
                    } 
                    catch { }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        #region Search
        private readonly Dictionary<int, System.Drawing.Color> originalTextColors = new Dictionary<int, System.Drawing.Color>();
        private bool isSearching;

        private void Search(object sender, RoutedEventArgs e)
        {
            // Clear previous search state properly
            ClearPreviousSearch();

            // Check for empty search
            if (string.IsNullOrWhiteSpace(vm.SearchString))
            {
                vm.Indices = new int[0];
                isSearching = false;
                return;
            }

            isSearching = true;
            var selectionStart = logRTB.SelectionStart;
            var startIndex = 0;
            var indices = new List<int>();
            int index;

            while ((index = logRTB.Text.IndexOf(vm.SearchString, startIndex, StringComparison.InvariantCultureIgnoreCase)) != -1)
            {
                logRTB.Select(index, vm.SearchString.Length);
                
                // Store original text color before changing it
                var originalColor = logRTB.SelectionColor;
                originalTextColors[index] = originalColor;
                
                // Apply search highlighting - only change background, not text color
                logRTB.SelectionBackColor = System.Drawing.Color.FromArgb(0, 123, 255); // Blue highlight
                // Keep original text color for better visibility

                startIndex = index + vm.SearchString.Length;
                indices.Add(index);

                // If it's the first match, immediately scroll to it
                if (indices.Count == 1)
                {
                    logRTB.ScrollToCaret();
                }
            }

            vm.Indices = indices.ToArray();

            // Reset the selection
            logRTB.SelectionStart = selectionStart;
            logRTB.SelectionLength = 0;
        }

        private void PreviousMatch(object sender, RoutedEventArgs e)
        {
            // If no matches, do nothing
            if (vm.Indices.Length == 0 || string.IsNullOrEmpty(vm.SearchString))
            {
                return;
            }

            // If we need to loop around
            if (vm.CurrentMatchIndex == 0)
            {
                vm.CurrentMatchIndex = vm.Indices.Length - 1;
            }
            else
            {
                vm.CurrentMatchIndex--;
            }

            // Navigate to the match and highlight it
            logRTB.DeselectAll();
            logRTB.Select(vm.Indices[vm.CurrentMatchIndex], vm.SearchString.Length);
            logRTB.ScrollToCaret();
            logRTB.Focus(); // Ensure focus for better visibility
        }

        private void NextMatch(object sender, RoutedEventArgs e)
        {
            // If no matches, do nothing
            if (vm.Indices.Length == 0 || string.IsNullOrEmpty(vm.SearchString))
            {
                return;
            }

            // If we need to loop around
            if (vm.CurrentMatchIndex == vm.Indices.Length - 1)
            {
                vm.CurrentMatchIndex = 0;
            }
            else
            {
                vm.CurrentMatchIndex++;
            }

            // Navigate to the match and highlight it
            logRTB.DeselectAll();
            logRTB.Select(vm.Indices[vm.CurrentMatchIndex], vm.SearchString.Length);
            logRTB.ScrollToCaret();
            logRTB.Focus(); // Ensure focus for better visibility
        }

        private void ClearPreviousSearch()
        {
            // Remove previous search highlights and restore original colors
            if (vm.Indices.Length > 0)
            {
                var searchLength = vm.SearchString?.Length ?? 0;
                foreach (var index in vm.Indices)
                {
                    if (index >= 0 && index < logRTB.Text.Length)
                    {
                        logRTB.Select(index, Math.Min(searchLength, logRTB.Text.Length - index));
                        
                        // Reset background color
                        logRTB.SelectionBackColor = System.Drawing.Color.FromArgb(22, 22, 22);
                        
                        // Restore original text color if we stored it
                        if (originalTextColors.ContainsKey(index))
                        {
                            logRTB.SelectionColor = originalTextColors[index];
                        }
                    }
                }
                logRTB.DeselectAll();
            }
            
            // Clear stored colors and reset state
            originalTextColors.Clear();
            vm.Indices = new int[0];
            vm.CurrentMatchIndex = 0;
        }

        private void ClearSearch(object sender, RoutedEventArgs e)
        {
            // Clear previous search properly
            ClearPreviousSearch();
            
            // Clear search string
            vm.SearchString = string.Empty;
            isSearching = false;
        }

        private void Debugger_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Handle keyboard shortcuts for better UX
            if (e.Key == System.Windows.Input.Key.F3)
            {
                if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift)
                {
                    // Shift+F3: Previous match
                    PreviousMatch(null, null);
                    e.Handled = true;
                }
                else
                {
                    // F3: Next match
                    NextMatch(null, null);
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Ctrl+F: Focus search box
                searchTextBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.L && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Ctrl+L: Clear logs
                ClearLog(null, null);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                // Esc: Clear search
                if (!string.IsNullOrEmpty(vm.SearchString))
                {
                    ClearSearch(null, null);
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Enter && searchTextBox.IsFocused)
            {
                // Enter in search box: Start search
                Search(null, null);
                e.Handled = true;
            }
        }
        #endregion
    }
}
