using System;
using System.Windows;
using System.Windows.Threading;
using OpenBullet2.Native.ViewModels.Base;
using RuriLib.Blocks.Utility;

namespace OpenBullet2.Native.ViewModels.Tools
{
    /// <summary>
    /// MVVM wrapper for the OTP generator tool.
    /// </summary>
    public sealed class OtpToolViewModel : ViewModelBase, IDisposable
    {
        private readonly DispatcherTimer timer;
        private readonly RelayCommand pasteSecretCommand;
        private readonly RelayCommand clearSecretCommand;
        private readonly RelayCommand copyOtpCommand;

        private string secret = string.Empty;
        private string normalizedSecret = string.Empty;
        private string currentOtp = "------";
        private string statusMessage = "Enter a secret key to generate codes.";
        private string errorMessage = string.Empty;
        private bool hasError;
        private bool canCopy;
        private double progressValue;

        public OtpToolViewModel()
        {
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += OnTimerTick;

            pasteSecretCommand = new RelayCommand(PasteSecret);
            clearSecretCommand = new RelayCommand(ClearSecret);
            copyOtpCommand = new RelayCommand(CopyOtp, () => CanCopy);
        }

        public RelayCommand PasteSecretCommand => pasteSecretCommand;

        public RelayCommand ClearSecretCommand => clearSecretCommand;

        public RelayCommand CopyOtpCommand => copyOtpCommand;

        public string Secret
        {
            get => secret;
            set
            {
                var sanitized = (value ?? string.Empty).ToUpperInvariant();
                if (SetProperty(ref secret, sanitized))
                {
                    normalizedSecret = TwoFactorUtility.NormalizeSecret(secret);
                    ValidateAndStart();
                }
            }
        }

        public string CurrentOtp
        {
            get => currentOtp;
            private set => SetProperty(ref currentOtp, value);
        }

        public string StatusMessage
        {
            get => statusMessage;
            private set => SetProperty(ref statusMessage, value);
        }

        public double ProgressValue
        {
            get => progressValue;
            set => SetProperty(ref progressValue, value);
        }

        public bool CanCopy
        {
            get => canCopy;
            private set
            {
                if (SetProperty(ref canCopy, value))
                {
                    copyOtpCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasError
        {
            get => hasError;
            private set => SetProperty(ref hasError, value);
        }

        public string ErrorMessage
        {
            get => errorMessage;
            private set => SetProperty(ref errorMessage, value);
        }

        public void Dispose()
        {
            timer.Tick -= OnTimerTick;
            timer.Stop();
        }

        private void OnTimerTick(object sender, EventArgs e) => UpdateOtp();

        private void ValidateAndStart()
        {
            if (string.IsNullOrWhiteSpace(normalizedSecret))
            {
                timer.Stop();
                ClearError();
                SetDisplay("------", "Enter a secret key to generate codes.", 0);
                CanCopy = false;
                return;
            }

            if (TwoFactorUtility.TryGenerateOtp(normalizedSecret, DateTime.UtcNow, out var otp, out var secondsRemaining, out var error))
            {
                ClearError();
                SetDisplay(otp, BuildExpiryMessage(secondsRemaining), TwoFactorUtility.TotpPeriodSeconds - secondsRemaining);
                CanCopy = true;
                timer.Start();
            }
            else
            {
                timer.Stop();
                SetError(string.IsNullOrWhiteSpace(error) ? "Invalid secret." : error);
                SetDisplay("------", "Invalid secret.", 0);
                CanCopy = false;
            }
        }

        private void UpdateOtp()
        {
            if (string.IsNullOrEmpty(normalizedSecret))
            {
                timer.Stop();
                return;
            }

            if (TwoFactorUtility.TryGenerateOtp(normalizedSecret, DateTime.UtcNow, out var otp, out var secondsRemaining, out var error))
            {
                ClearError();
                SetDisplay(otp, BuildExpiryMessage(secondsRemaining), TwoFactorUtility.TotpPeriodSeconds - secondsRemaining);
                CanCopy = true;
            }
            else
            {
                timer.Stop();
                SetError(string.IsNullOrWhiteSpace(error) ? "Invalid secret." : error);
                SetDisplay("------", "Invalid secret.", 0);
                CanCopy = false;
            }
        }

        private void PasteSecret()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    Secret = Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                SetError($"Clipboard unavailable: {ex.Message}");
            }
        }

        private void ClearSecret()
        {
            Secret = string.Empty;
        }

        private void CopyOtp()
        {
            if (!CanCopy || string.IsNullOrEmpty(CurrentOtp) || CurrentOtp.Contains('-'))
            {
                return;
            }

            try
            {
                Clipboard.SetText(CurrentOtp);
            }
            catch (Exception ex)
            {
                SetError($"Unable to copy OTP: {ex.Message}");
            }
        }

        private void SetDisplay(string otp, string status, int elapsedSeconds)
        {
            CurrentOtp = otp;
            StatusMessage = status;
            ProgressValue = Math.Max(0, Math.Min(TwoFactorUtility.TotpPeriodSeconds, elapsedSeconds));
        }

        private static string BuildExpiryMessage(int secondsRemaining)
            => secondsRemaining <= 1
                ? "Expires in 1 second"
                : $"Expires in {secondsRemaining} seconds";

        private void SetError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }

        private void ClearError()
        {
            if (!HasError)
            {
                return;
            }

            HasError = false;
            ErrorMessage = string.Empty;
        }
    }
}


