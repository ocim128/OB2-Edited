using OpenBullet2.Core.Extensions;
using OpenBullet2.Native.Views.Dialogs;
using System;
using System.Windows;

namespace OpenBullet2.Native.Helpers
{
    public static class Alert
    {
        public static void Info(string title, string message) => ShowAlert(AlertType.Info, title, message);
        public static void Success(string title, string message) => ShowAlert(AlertType.Success, title, message);
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

        private static void ShowAlert(AlertType type, string title, string message)
            => Application.Current.Dispatcher.Invoke(() => new MainDialog(new AlertDialog(type, title, message), title).ShowDialog());

        public static void Exception(Exception ex)  => Error("Error", "An unexpected error occurred: " + ex.Message);

        public static bool Confirm(string title, string message, string settingName)
        {
            var obSettingsService = SP.GetService<OpenBullet2.Core.Services.OpenBulletSettingsService>();

            // If the user checked 'don't ask again' for this specific setting
            if (obSettingsService.Settings.GeneralSettings.GetProperty(settingName) is bool b && !b)
            {
                return true;
            }

            var result = false;
            var dontAskAgain = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                new MainDialog(new ConfirmationDialog(title, message, (r, d) => { result = r; dontAskAgain = d; }), title).ShowDialog();
            });

            if (dontAskAgain)
            {
                // If the user checked 'don't ask again', save the preference
                obSettingsService.Settings.GeneralSettings.SetProperty(settingName, false);
                obSettingsService.SaveAsync();
            }

            return result;
        }
    }
}
