using ICSharpCode.AvalonEdit;
using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using RuriLib.Logging;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenBullet2.Native.Views.Pages.Shared;

/// <summary>
/// Interaction logic for Debugger.xaml
/// Refactored to use DebuggerLogService and DebuggerUIManager for separation of concerns.
/// Now using AvalonEdit for high-performance logging.
/// </summary>
public partial class Debugger : Page
{
    #region Constants
    private const int RESIZE_DELAY_MS = 300;
    private const int UPDATE_INTERVAL_MS = 100;
    #endregion

    #region Private Fields
    private readonly DebuggerViewModel _viewModel;
    private readonly AccessibilitySettings _accessibility;
    private readonly DebuggerLogService _logService;
    private readonly DebuggerUIManager _uiManager;
    
    private DispatcherTimer? _resizeTimer;
    private DispatcherTimer? _updateTimer;
    private bool _isResizing;
    private bool _windowKeyHandlersAttached;
    #endregion

    #region Constructor
    public Debugger()
    {
        var settingsService = ServiceLocator.GetService<OpenBulletSettingsService>();
        _accessibility = settingsService.Settings.AccessibilitySettings ?? new AccessibilitySettings();

        _viewModel = ServiceLocator.GetService<ViewModelsService>().Debugger;
        DataContext = _viewModel;

        _uiManager = new DebuggerUIManager(() => Application.Current.MainWindow as MainWindow);

        InitializeComponent();
        
        // Initialize log service after InitializeComponent (controls now exist)
        _logService = new DebuggerLogService(logRTB, _viewModel);
        
        // Wire up events
        _viewModel.NewLogEntry += OnNewLogEntry;
        _viewModel.LogCleared += OnLogCleared;

        InitializeComponents();
        SetupEventHandlers();
        ApplyAccessibilityPreferences();
    }
    #endregion

    #region Initialization
    private void InitializeComponents()
    {
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnDebuggerKeyDown), true);
        AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnDebuggerKeyDown), true);
        tabControl.SelectedIndex = 0;

        // Initialize timers
        var resizeDelay = _accessibility.ReduceAnimations ? RESIZE_DELAY_MS * 2 : RESIZE_DELAY_MS;
        var updateInterval = _accessibility.ReduceAnimations ? UPDATE_INTERVAL_MS * 2 : UPDATE_INTERVAL_MS;
        
        _resizeTimer = CreateTimer(TimeSpan.FromMilliseconds(resizeDelay), OnResizeTimerTick);
        _updateTimer = CreateTimer(TimeSpan.FromMilliseconds(updateInterval), OnUpdateTimerTick);
        _updateTimer.Start();

        // Initialize UI manager with control references
        _uiManager.Initialize(this);
        
        _uiManager.UpdateAllToggleAppearances();
    }

    private static DispatcherTimer CreateTimer(TimeSpan interval, EventHandler tickHandler)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += tickHandler;
        return timer;
    }

    private void SetupEventHandlers()
    {
        SizeChanged += OnDebuggerSizeChanged;
        Loaded += OnDebuggerLoaded;
        Unloaded += OnDebuggerUnloaded;
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
            ConfigureTooltip(StepButton, "Run a single step (Ctrl+Alt+Right)");
            ConfigureTooltip(StopButton, "Stop execution (Alt+X)");
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
        if (StopButton != null) yield return StopButton;
        if (StepButton != null) yield return StepButton;
        yield return searchTextBox;
        yield return inputDataTextBox;
    }

    private static void ConfigureTooltip(DependencyObject? target, string text)
    {
        if (target == null) return;
        ToolTipService.SetToolTip(target, text);
        ToolTipService.SetInitialShowDelay(target, 150);
        ToolTipService.SetShowDuration(target, 12000);
        ToolTipService.SetBetweenShowDelay(target, 300);
    }
    #endregion

    #region Log Event Handlers
    private void OnNewLogEntry(object? sender, BotLoggerEntry entry)
    {
        _logService.HandleNewLogEntry(entry);
    }

    private void OnLogCleared(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() => _logService.ClearLog(html => htmlViewer.HTML = html));
    }
    #endregion

    #region Resize Handling
    private void OnDebuggerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isResizing)
        {
            _isResizing = true;
            _logService.UpdatesPaused = true;
            _logService.IsResizing = true;
            _updateTimer?.Stop();
        }

        _resizeTimer?.Stop();
        _resizeTimer?.Start();
    }

    private void OnResizeTimerTick(object? sender, EventArgs e)
    {
        _resizeTimer?.Stop();
        _isResizing = false;
        _logService.UpdatesPaused = false;
        _logService.IsResizing = false;
        _updateTimer?.Start();
        _logService.ProcessPendingEntries();
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        if (!_logService.UpdatesPaused && !_isResizing && !_logService.IsWindowMinimized)
        {
            _logService.ProcessPendingEntries();
        }
    }
    #endregion

    #region Keyboard Shortcuts
    private void OnDebuggerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.RoutedEvent != Keyboard.PreviewKeyDownEvent) return;

        var focusedElement = Keyboard.FocusedElement as UIElement;
        var isEditorFocused = focusedElement != null && (
            focusedElement.GetType().Name.Contains("TextEditor") ||
            focusedElement.ToString().Contains("ICSharpCode.AvalonEdit"));

        // Alt-based shortcuts
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            var key = e.SystemKey != Key.None ? e.SystemKey : e.Key;
            switch (key)
            {
                case Key.A when inputDataTextBox is { IsEnabled: true }:
                    inputDataTextBox.Focus();
                    inputDataTextBox.SelectAll();
                    e.Handled = true;
                    return;
                case Key.S when StartButton is { IsEnabled: true, IsVisible: true }:
                    Start(StartButton, new RoutedEventArgs(Button.ClickEvent, StartButton));
                    e.Handled = true;
                    return;
                case Key.S when StopButton is { IsEnabled: true, IsVisible: true }: // Typo in original? No, Alt+S for Start.
                     // Logic for STOP is usually Alt+X in other handlers or logic
                     break;
            }
        }

        switch (e.Key)
        {
            case Key.F3 when Keyboard.Modifiers == ModifierKeys.Shift:
                _logService.PreviousMatch();
                e.Handled = true;
                break;
            case Key.F3:
                _logService.NextMatch();
                e.Handled = true;
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control && !isEditorFocused:
                searchTextBox.Focus();
                e.Handled = true;
                break;
            case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.ClearLog();
                e.Handled = true;
                break;
            case Key.Escape when !string.IsNullOrEmpty(_viewModel.SearchString):
                _logService.ClearSearch();
                e.Handled = true;
                break;
            case Key.Enter when searchTextBox.IsFocused:
                _logService.Search();
                e.Handled = true;
                break;
        }
    }
    #endregion

    #region Tab Navigation
    private void ShowLog(object sender, RoutedEventArgs e) => tabControl.SelectedIndex = 0;
    
    private void ShowVariables(object sender, RoutedEventArgs e)
    {
        tabControl.SelectedIndex = 1;
        _logService.UpdateVariablesList();
    }
    
    private void ShowHTML(object sender, RoutedEventArgs e) => tabControl.SelectedIndex = 2;
    #endregion

    #region Search Actions
    private void Search(object sender, RoutedEventArgs e) => _logService.Search();
    private void PreviousMatch(object sender, RoutedEventArgs e) => _logService.PreviousMatch();
    private void NextMatch(object sender, RoutedEventArgs e) => _logService.NextMatch();
    private void ClearSearch(object sender, RoutedEventArgs e) => _logService.ClearSearch();
    private void ClearLog(object sender, RoutedEventArgs e) => _viewModel.ClearLog();
    private void PreviousBlock(object sender, RoutedEventArgs e) => _logService.NavigateToBlock(-1);
    private void NextBlock(object sender, RoutedEventArgs e) => _logService.NavigateToBlock(1);
    #endregion

    #region Debugger Control Actions
    private async void Start(object sender, RoutedEventArgs e)
    {
        try
        {
            _logService.ClearLog(); // Use service helper
            await _viewModel.RunAsync();
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private void TakeStep(object sender, RoutedEventArgs e) => _viewModel.TakeStep();
    private void Stop(object sender, RoutedEventArgs e) => _viewModel.Stop();
    #endregion

    #region UI Toggle Actions
    private void ToggleAutoScroll(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleAutoScroll();
        _logService.ScrollingDisabled = !_viewModel.IsAutoScrollEnabled;
    }

    private void ToggleOptions(object sender, RoutedEventArgs e) => _uiManager.ToggleOptions();
    private void ToggleStacker(object sender, RoutedEventArgs e) => _uiManager.ToggleStacker();
    private void ToggleTabButtons(object sender, RoutedEventArgs e) => _uiManager.ToggleTabButtons();
    private void ToggleFocusMode(object sender, RoutedEventArgs e) => _uiManager.ToggleFocusMode();
    #endregion

    #region Window State
    public void SetWindowMinimized(bool isMinimized)
    {
        _logService.IsWindowMinimized = isMinimized;
    }

    public void SetResizing(bool isResizing)
    {
        if (isResizing && !_isResizing)
        {
            _isResizing = true;
            _logService.UpdatesPaused = true;
            _logService.IsResizing = true;
            _updateTimer?.Stop();

            _resizeTimer?.Stop();
            _resizeTimer?.Start();
        }
        else if (!isResizing && _isResizing)
        {
            _resizeTimer?.Stop();
            OnResizeTimerTick(null, EventArgs.Empty);
        }
    }
    #endregion

    #region Page Lifecycle
    private void OnDebuggerLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Focusable = true;
            Dispatcher.BeginInvoke(() =>
            {
                if (inputDataTextBox is { IsVisible: true, IsEnabled: true })
                {
                    inputDataTextBox.Focus();
                }
                else if (StartButton is { IsVisible: true, IsEnabled: true })
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
                }
            }, DispatcherPriority.Background);
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
            }
            
            // _logService.Dispose(); // Removed to prevent detaching colorizers when page is cached/reused
        }
        catch { }
    }
    #endregion
}
