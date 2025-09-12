using OpenBullet2.Core.Extensions;
using OpenBullet2.Native.Views.Dialogs;
using OpenBullet2.Native.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.Helpers
{
    public static class Alert
    {
        public static void Info(string title, string message) => ShowAlert(AlertType.Info, title, message);
        public static void Success(string title, string message) => ShowModernNotification(title, message);
        public static void Warning(string title, string message) => ShowAlert(AlertType.Warning, title, message);
        public static void Error(string title, string message) => ShowAlert(AlertType.Error, title, message);
        
        public static bool Choice(string title, string message, string yesText = "Yes", string noText = "No")
        {
            var choice = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                new MainDialog(new ChoiceDialog(title, message, b => choice = b, yesText, noText), title).ShowDialog();
            });

            return choice;
        }

        public static string CustomInput(string question, string defaultAnswer)
        {
            var answer = string.Empty;

            Application.Current.Dispatcher.Invoke(() =>
            {
                new MainDialog(new CustomInputDialog(question, defaultAnswer, a => answer = a), "Custom input").ShowDialog();
            });

            return answer;
        }

        /// <summary>
        /// Centralized helper for showing dialogs with consistent patterns.
        /// Reduces MainDialog creation duplication across the codebase.
        /// </summary>
        public static void ShowDialog(Page content, string title, bool canResize = false, Action onClosed = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new MainDialog(content, title, canResize);
                if (onClosed != null)
                {
                    dialog.Closed += (s, e) => onClosed();
                }
                dialog.ShowDialog();
            });
        }

        /// <summary>
        /// Centralized helper for showing dialogs with custom dimensions.
        /// </summary>
        public static void ShowDialog(Page content, string title, int width, int height, Action onClosed = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new MainDialog(content, title, width, height);
                if (onClosed != null)
                {
                    dialog.Closed += (s, e) => onClosed();
                }
                dialog.ShowDialog();
            });
        }

        /// <summary>
        /// Helper for safe exception handling with UI feedback.
        /// Consolidates exception handling patterns across UI components.
        /// </summary>
        public static void HandleException(Exception ex, string context = "operation")
        {
            var message = $"An error occurred during {context}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Exception in {context}: {ex}");
            Error("Error", message);
        }

        /// <summary>
        /// Helper for safe async operation execution with exception handling.
        /// </summary>
        public static async System.Threading.Tasks.Task SafeExecuteAsync(Func<System.Threading.Tasks.Task> operation, string context = "operation")
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                HandleException(ex, context);
            }
        }

        /// <summary>
        /// Helper for safe async operation execution with return value and exception handling.
        /// </summary>
        public static async System.Threading.Tasks.Task<T> SafeExecuteAsync<T>(Func<System.Threading.Tasks.Task<T>> operation, T defaultValue = default, string context = "operation")
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                HandleException(ex, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Helper for safe synchronous operation execution with exception handling.
        /// </summary>
        public static void SafeExecute(Action operation, string context = "operation")
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                HandleException(ex, context);
            }
        }

        /// <summary>
        /// Helper for safe synchronous operation execution with return value and exception handling.
        /// </summary>
        public static T SafeExecute<T>(Func<T> operation, T defaultValue = default, string context = "operation")
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                HandleException(ex, context);
                return defaultValue;
            }
        }

        private static void ShowAlert(AlertType type, string title, string message)
            => Application.Current.Dispatcher.Invoke(() => new MainDialog(new AlertDialog(type, title, message), title).ShowDialog());

        private static void ShowModernNotification(string title, string message)
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Use the shared notification window with App.xaml styles
                    var notification = new SharedNotificationWindow(title, message);
                    notification.Show();
                }));
            }
            catch (Exception ex)
            {
                // Fallback to traditional dialog if notification fails
                ShowAlert(AlertType.Success, title, message);
            }
        }

        public static void Exception(Exception ex)  => Error("Error", "An unexpected error occurred: " + ex.Message);

        public static bool Confirm(string title, string message, string settingName)
        {
            var obSettingsService = ServiceLocator.GetService<OpenBullet2.Core.Services.OpenBulletSettingsService>();

            // If the user checked 'don't ask again' for this specific setting
            if (obSettingsService.Settings.GeneralSettings.GetProperty(settingName) is bool b && !b)
            {
                return true;
            }

            var result = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new ConfirmationDialog(title, message);
                dialog.ShowDialog(Application.Current.MainWindow);
                result = dialog.Result;
            });

            return result;
        }
    }
}
