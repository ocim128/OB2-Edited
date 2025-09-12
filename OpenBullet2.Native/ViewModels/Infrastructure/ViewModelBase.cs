using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using OpenBullet2.Native.Infrastructure.DependencyInjection;
using OpenBullet2.Native.Helpers;

namespace OpenBullet2.Native.ViewModels.Infrastructure
{
    /// <summary>
    /// Enhanced base class for ViewModels with improved property change notification
    /// and backward compatibility with existing ViewModels.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// The event that lets the GUI know a property was changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises a PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property. If null, the name of the calling property will be used.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (string.IsNullOrEmpty(propertyName))
                return;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Calls OnPropertyChanged on all public properties of this class.
        /// </summary>
        public virtual void UpdateViewModel()
        {
            foreach (var property in GetType().GetProperties())
            {
                OnPropertyChanged(property.Name);
            }
        }
    }

    /// <summary>
    /// Base class for Pages that provides common initialization patterns
    /// and service injection functionality.
    /// Call InitializePage() after InitializeComponent() in derived classes.
    /// </summary>
    public abstract class PageBase : Page
    {
        /// <summary>
        /// Initializes the page with common setup pattern.
        /// Call this after InitializeComponent() in derived constructors.
        /// </summary>
        protected void InitializePage()
        {
            SetupViewModel();
            SetupEventHandlers();
        }

        /// <summary>
        /// Override to provide ViewModel setup logic.
        /// </summary>
        protected virtual void SetupViewModel() { }

        /// <summary>
        /// Override to provide additional event handler setup.
        /// </summary>
        protected virtual void SetupEventHandlers() { }

        /// <summary>
        /// Centralized navigation helper that reduces duplication.
        /// </summary>
        protected void NavigateToPage(MainWindowPage page)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateTo(page);
        }

        /// <summary>
        /// Helper method for safe service retrieval with error handling.
        /// </summary>
        protected T GetService<T>() where T : class
        {
            try
            {
                return ServiceLocator.GetService<T>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Service retrieval failed for {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Helper method for safe service retrieval with exception throwing.
        /// </summary>
        protected T GetRequiredService<T>() where T : class
        {
            return ServiceLocator.GetService<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} service is null");
        }
    }

    /// <summary>
    /// Base class for ViewModel-backed Pages that provides common patterns.
    /// Call InitializePage() after InitializeComponent() in derived classes.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type</typeparam>
    public abstract class ViewModelPageBase<TViewModel> : PageBase 
        where TViewModel : ViewModelBase, new()
    {
        protected TViewModel vm;

        protected override void SetupViewModel()
        {
            vm = CreateViewModel();
            DataContext = vm;
            
            // Setup disposal if ViewModel implements IDisposable
            if (vm is IDisposable disposableVm)
            {
                Unloaded += (s, e) => disposableVm?.Dispose();
            }
        }

        /// <summary>
        /// Override to provide custom ViewModel creation logic.
        /// Default implementation creates a new instance.
        /// </summary>
        protected virtual TViewModel CreateViewModel() => new TViewModel();
    }

    /// <summary>
    /// Helper class that provides centralized UI functionality via composition.
    /// Used when inheritance from PageBase is not possible due to XAML constraints.
    /// </summary>
    public class PageHelper
    {
        private readonly Page page;

        public PageHelper(Page page)
        {
            this.page = page ?? throw new ArgumentNullException(nameof(page));
        }

        /// <summary>
        /// Centralized navigation helper that reduces duplication.
        /// </summary>
        public void NavigateToPage(MainWindowPage targetPage)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateTo(targetPage);
        }

        /// <summary>
        /// Helper method for safe service retrieval with error handling.
        /// </summary>
        public T GetService<T>() where T : class
        {
            try
            {
                return ServiceLocator.GetService<T>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Service retrieval failed for {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Helper method for safe service retrieval with exception throwing.
        /// </summary>
        public T GetRequiredService<T>() where T : class
        {
            return ServiceLocator.GetService<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} service is null");
        }
    }

    /// <summary>
    /// Centralized UI helpers that can be used across all UI components.
    /// Eliminates duplicate patterns in dialogs, pages, and controls.
    /// </summary>
    public static class UIHelpers
    {
        /// <summary>
        /// Centralized dialog close helper - eliminates 19+ duplicate patterns.
        /// Safely closes the parent MainDialog from any page/dialog.
        /// </summary>
        public static void CloseParentDialog(FrameworkElement element)
        {
            try
            {
                if (element?.Parent is MainDialog dialog)
                {
                    dialog.Close();
                }
                else
                {
                    // Fallback: search up the visual tree
                    var current = element?.Parent as FrameworkElement;
                    while (current != null && current is not MainDialog)
                    {
                        current = current.Parent as FrameworkElement;
                    }
                    (current as MainDialog)?.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to close parent dialog: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper for common tagged button operations - reduces duplicate casting.
        /// </summary>
        public static T GetButtonTag<T>(object sender) where T : class
        {
            return (sender as FrameworkElement)?.Tag as T;
        }

        /// <summary>
        /// Centralized exception handling for UI operations.
        /// </summary>
        public static void HandleUIException(Exception ex, string operation = "UI operation")
        {
            System.Diagnostics.Debug.WriteLine($"UI Exception in {operation}: {ex}");
            try
            {
                Alert.HandleException(ex, operation);
            }
            catch
            {
                // Fallback if Alert.HandleException fails
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Centralized exception handling with action execution.
        /// Eliminates duplicate try-catch patterns across UI components.
        /// </summary>
        public static void HandleUIException(Action action, string operation = "UI operation")
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                HandleUIException(ex, operation);
            }
        }
    }
}
