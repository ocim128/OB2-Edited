using OpenBullet2.Core.Services;
using System;
using System.Threading.Tasks;

namespace OpenBullet2.Native.Services;

/// <summary>
/// Event args for UI state changes
/// </summary>
public class UIStateChangedEventArgs : EventArgs
{
    public string PropertyName { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }

    public UIStateChangedEventArgs(string propertyName, object? oldValue, object? newValue)
    {
        PropertyName = propertyName;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// Interface for centralized UI state management
/// </summary>
public interface IUIStateService
{
    /// <summary>
    /// Whether the sidebar is currently expanded
    /// </summary>
    bool IsSidebarExpanded { get; set; }

    /// <summary>
    /// Whether focus mode is enabled (hides non-essential UI elements)
    /// </summary>
    bool IsFocusModeEnabled { get; set; }

    /// <summary>
    /// Current global search query
    /// </summary>
    string CurrentSearchQuery { get; set; }

    /// <summary>
    /// Whether a job is currently running
    /// </summary>
    bool IsJobRunning { get; set; }

    /// <summary>
    /// The ID of the currently active job, if any
    /// </summary>
    int? ActiveJobId { get; set; }

    /// <summary>
    /// Event fired when any UI state property changes
    /// </summary>
    event EventHandler<UIStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Save current state to persistent storage
    /// </summary>
    Task SaveStateAsync();

    /// <summary>
    /// Restore state from persistent storage
    /// </summary>
    void RestoreState();

    /// <summary>
    /// Reset all state to defaults
    /// </summary>
    void ResetState();
}

/// <summary>
/// Centralized service for managing UI state across the application.
/// Provides a single source of truth for UI-related state that needs
/// to be shared between components.
/// </summary>
public class UIStateService : IUIStateService
{
    private readonly OpenBulletSettingsService _settingsService;
    
    private bool _isSidebarExpanded;
    private bool _isFocusModeEnabled;
    private string _currentSearchQuery = string.Empty;
    private bool _isJobRunning;
    private int? _activeJobId;

    public event EventHandler<UIStateChangedEventArgs>? StateChanged;

    public UIStateService(OpenBulletSettingsService settingsService)
    {
        _settingsService = settingsService;
        RestoreState();
    }

    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set => SetProperty(ref _isSidebarExpanded, value, nameof(IsSidebarExpanded));
    }

    public bool IsFocusModeEnabled
    {
        get => _isFocusModeEnabled;
        set => SetProperty(ref _isFocusModeEnabled, value, nameof(IsFocusModeEnabled));
    }

    public string CurrentSearchQuery
    {
        get => _currentSearchQuery;
        set => SetProperty(ref _currentSearchQuery, value ?? string.Empty, nameof(CurrentSearchQuery));
    }

    public bool IsJobRunning
    {
        get => _isJobRunning;
        set => SetProperty(ref _isJobRunning, value, nameof(IsJobRunning));
    }

    public int? ActiveJobId
    {
        get => _activeJobId;
        set => SetProperty(ref _activeJobId, value, nameof(ActiveJobId));
    }

    public void RestoreState()
    {
        try
        {
            var customization = _settingsService.Settings.CustomizationSettings;
            
            // Restore sidebar state - default to collapsed for cleaner look
            _isSidebarExpanded = customization.SidebarExpanded;
            _isFocusModeEnabled = false;
            _currentSearchQuery = string.Empty;
            _isJobRunning = false;
            _activeJobId = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error restoring UI state: {ex.Message}");
            ResetState();
        }
    }

    public async Task SaveStateAsync()
    {
        try
        {
            var customization = _settingsService.Settings.CustomizationSettings;
            customization.SidebarExpanded = _isSidebarExpanded;
            
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving UI state: {ex.Message}");
        }
    }

    public void ResetState()
    {
        _isSidebarExpanded = false;
        _isFocusModeEnabled = false;
        _currentSearchQuery = string.Empty;
        _isJobRunning = false;
        _activeJobId = null;
    }

    private void SetProperty<T>(ref T field, T value, string propertyName)
    {
        if (Equals(field, value)) return;
        
        var oldValue = field;
        field = value;
        
        StateChanged?.Invoke(this, new UIStateChangedEventArgs(propertyName, oldValue, value));
    }
}
