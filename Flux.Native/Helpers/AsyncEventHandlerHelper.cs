using System;
using System.Threading.Tasks;
using System.Windows;

namespace Flux.Native.Helpers;

/// <summary>
/// Helper class for safely handling async void event handlers.
/// Prevents unhandled exceptions from crashing the application.
/// </summary>
public static class AsyncEventHandlerHelper
{
    /// <summary>
    /// Safely executes an async action with error handling.
    /// </summary>
    /// <param name="asyncAction">The async action to execute.</param>
    /// <param name="errorHandler">Optional custom error handler. If null, displays a default error dialog.</param>
    public static async void SafeFireAndForget(Func<Task> asyncAction, Action<Exception>? errorHandler = null)
    {
        try
        {
            await asyncAction();
        }
        catch (Exception ex)
        {
            if (errorHandler != null)
            {
                errorHandler(ex);
            }
            else
            {
                HandleDefaultError(ex);
            }
        }
    }

    /// <summary>
    /// Safely executes an async action with error handling and a specific error message.
    /// </summary>
    /// <param name="asyncAction">The async action to execute.</param>
    /// <param name="errorMessage">The error message to display if an exception occurs.</param>
    public static async void SafeFireAndForget(Func<Task> asyncAction, string errorMessage)
    {
        try
        {
            await asyncAction();
        }
        catch (Exception ex)
        {
            HandleDefaultError(ex, errorMessage);
        }
    }

    /// <summary>
    /// Default error handler that shows a message box with the error details.
    /// </summary>
    private static void HandleDefaultError(Exception ex, string? customMessage = null)
    {
        var message = customMessage ?? "An unexpected error occurred";
        var detailedMessage = $"{message}:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
        
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            MessageBox.Show(
                detailedMessage,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        });

        // Also log to console for debugging
        Console.WriteLine($"[AsyncEventHandlerHelper] {message}");
        Console.WriteLine($"Exception: {ex}");
    }
}
