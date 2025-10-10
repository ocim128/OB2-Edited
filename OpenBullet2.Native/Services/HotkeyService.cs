using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Collections.Generic;

namespace OpenBullet2.Native.Services
{
    public class HotkeyService : IDisposable
    {
        // Windows API imports for global hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // Constants for hotkey registration
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        // Virtual key codes
        private const uint VK_Q = 0x51;

        // Hotkey IDs
        private const int HOTKEY_CTRL_ALT_Q = 1;

        private IntPtr windowHandle;
        private HwndSource hwndSource;
        private bool isEnabled = true; // Default to enabled
        private bool disposed = false;

        // Debouncing and execution control
        private DateTime lastHotkeyTime = DateTime.MinValue;
        private int lastHotkeyId = -1;
        private readonly object executionLock = new object();
        private bool isExecutingHotkey = false;
        private const int DEBOUNCE_INTERVAL_MS = 500; // 500ms between hotkey executions
        private const int EXECUTION_TIMEOUT_MS = 2000; // 2 second timeout for stuck executions

        // OTP validation for "disavow" or 6-digit numbers only
        private const int MAX_CLIPBOARD_LENGTH = 1000;

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (isEnabled != value)
                {
                    isEnabled = value;
                    if (isEnabled)
                    {
                        RegisterHotkeys();
                    }
                    else
                    {
                        UnregisterHotkeys();
                    }

                    EnabledChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler EnabledChanged;

        public HotkeyService()
        {
            // Plugin is enabled by default, no need to load settings
        }

        public void Initialize(Window window)
        {
            if (window == null)
            {
                return;
            }

            var helper = new WindowInteropHelper(window);
            windowHandle = helper.Handle;

            if (windowHandle == IntPtr.Zero)
            {
                // Window not yet loaded, wait for it
                window.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(window);
                    windowHandle = h.Handle;
                    SetupMessageHook();

                    // Register hotkeys since plugin is enabled by default
                    if (isEnabled)
                    {
                        RegisterHotkeys();
                    }
                };
            }
            else
            {
                SetupMessageHook();

                // Register hotkeys since plugin is enabled by default
                if (isEnabled)
                {
                    RegisterHotkeys();
                }
            }
        }

        private void SetupMessageHook()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            hwndSource = HwndSource.FromHwnd(windowHandle);
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && isEnabled)
            {
                var id = wParam.ToInt32();
                HandleHotkey(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void RegisterHotkeys()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                // Ctrl+Alt+Q for OTP file functionality
                RegisterHotKey(windowHandle, HOTKEY_CTRL_ALT_Q, MOD_CONTROL | MOD_ALT, VK_Q);

                ShowTrayNotification("OTP Hotkey Enabled", "Ctrl+Alt+Q is now active for OTP file writing");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register hotkeys: {ex.Message}");
            }
        }

        private void UnregisterHotkeys()
        {
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                UnregisterHotKey(windowHandle, HOTKEY_CTRL_ALT_Q);

                ShowTrayNotification("OTP Hotkey Disabled", "Ctrl+Alt+Q is now inactive");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to unregister hotkeys: {ex.Message}");
            }
        }

        private void HandleHotkey(int hotkeyId)
        {
            lock (executionLock)
            {
                try
                {
                    // Check if already executing a hotkey
                    if (isExecutingHotkey)
                    {
                        Debug.WriteLine($"Hotkey {hotkeyId} ignored - another hotkey is executing");
                        return;
                    }

                    // Debouncing: prevent rapid successive executions
                    var now = DateTime.Now;
                    var timeSinceLastHotkey = (now - lastHotkeyTime).TotalMilliseconds;

                    if (timeSinceLastHotkey < DEBOUNCE_INTERVAL_MS && lastHotkeyId == hotkeyId)
                    {
                        Debug.WriteLine($"Hotkey {hotkeyId} ignored - debounce interval not met ({timeSinceLastHotkey}ms < {DEBOUNCE_INTERVAL_MS}ms)");
                        return;
                    }

                    // Update execution state
                    isExecutingHotkey = true;
                    lastHotkeyTime = now;
                    lastHotkeyId = hotkeyId;

                    Debug.WriteLine($"Executing hotkey ID: {hotkeyId} at {now:HH:mm:ss.fff}");

                    switch (hotkeyId)
                    {
                        case HOTKEY_CTRL_ALT_Q:
                            Debug.WriteLine("Executing Ctrl+Alt+Q");
                            HandleCtrlAltQ();
                            break;
                        default:
                            Debug.WriteLine($"Unknown hotkey ID: {hotkeyId}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling hotkey {hotkeyId}: {ex.Message}");
                    ShowTrayNotification("Hotkey Error", $"Error in hotkey {hotkeyId}: {ex.Message}");
                }
                finally
                {
                    // Always reset execution state
                    isExecutingHotkey = false;
                    Debug.WriteLine($"Hotkey {hotkeyId} execution completed");
                }
            }
        }

        private void HandleCtrlAltQ()
        {
            try
            {
                // Wait up to 1 second for clipboard content
                var clipboardContent = GetClipboardText();

                if (string.IsNullOrEmpty(clipboardContent))
                {
                    ShowTrayNotification("OTP", "Clipboard is empty");
                    return;
                }

                // Check if clipboard contains "disavow" or a 6-digit number
                var hasDisavow = clipboardContent.Contains("disavow", StringComparison.OrdinalIgnoreCase);
                var hasSixDigitNumber = Regex.IsMatch(clipboardContent, @"\d{6}");

                if (hasDisavow || hasSixDigitNumber)
                {
                    // Write the otp.txt file into every running OpenBullet2.Native directory
                    var targetDirs = GetOpenBullet2Directories();
                    var writtenCount = 0;
                    foreach (var dir in targetDirs)
                    {
                        try
                        {
                            var filePath = Path.Combine(dir, "otp.txt");
                            File.WriteAllText(filePath, clipboardContent);
                            writtenCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to write OTP file in {dir}: {ex.Message}");
                        }
                    }

                    // Play success sound
                    PlayPopSound();

                    ShowTrayNotification("OTP", $"OTP file updated in {writtenCount} location{(writtenCount == 1 ? string.Empty : "s")}.");
                }
                else
                {
                    ShowTrayNotification("OTP", "Clipboard did not match either condition.");
                }
            }
            catch (Exception ex)
            {
                ShowTrayNotification("OTP Error", $"Failed to process clipboard: {ex.Message}");
            }
        }

        // Returns directories of all running OpenBullet2.Native processes (including current)
        private IEnumerable<string> GetOpenBullet2Directories()
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Always include current directory
            try { dirs.Add(Directory.GetCurrentDirectory()); } catch { }

            try
            {
                foreach (var proc in Process.GetProcessesByName("OpenBullet2.Native"))
                {
                    try
                    {
                        var exePath = proc.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            var dir = Path.GetDirectoryName(exePath);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                dirs.Add(dir);
                            }
                        }
                    }
                    catch { /* Access denied for some processes, ignore */ }
                }
            }
            catch { }

            return dirs;
        }

        private void PlayPopSound()
        {
            try
            {
                // Use the ui-sound.mp3 file from the Sounds directory
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var soundPath = Path.Combine(baseDir, "Sounds", "ui-sound.mp3");

                if (!File.Exists(soundPath))
                {
                    soundPath = Path.Combine(baseDir, "ui-sound.mp3");
                }

                if (File.Exists(soundPath))
                {
                    // Use MediaPlayer in a separate thread to avoid blocking
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var player = new System.Windows.Media.MediaPlayer();
                                player.Open(new Uri(soundPath));
                                player.Volume = 0.7; // Set reasonable volume
                                player.Play();

                                // Clean up after a reasonable time
                                var timer = new System.Windows.Threading.DispatcherTimer
                                {
                                    Interval = TimeSpan.FromSeconds(3)
                                };
                                timer.Tick += (s, e) =>
                                {
                                    timer.Stop();
                                    player.Close();
                                };
                                timer.Start();
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"MediaPlayer failed: {ex.Message}");
                            // Fallback to system sound
                            SystemSounds.Asterisk.Play();
                        }
                    });
                }
                else
                {
                    Debug.WriteLine($"Sound file not found: {soundPath}");
                    // Fallback to system sound
                    SystemSounds.Asterisk.Play();
                }
            }
            catch (Exception ex)
            {
                // Sound failure shouldn't break the functionality
                Debug.WriteLine($"Failed to play sound: {ex.Message}");
                // Try fallback system sound
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch
                {
                    // Silent if even system sound fails
                }
            }
        }

        private string GetClipboardText()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    return Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get clipboard text: {ex.Message}");
            }
            return string.Empty;
        }

        private void ShowTrayNotification(string title, string message)
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
                Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                IsEnabled = false;
                hwndSource?.RemoveHook(WndProc);
                disposed = true;
            }
        }
    }

    // Shared notification window using App.xaml styles
    public partial class SharedNotificationWindow : Window
    {
        public SharedNotificationWindow(string title, string message)
        {
            InitializeComponent(title, message);
        }

        private void InitializeComponent(string title, string message)
        {
            Width = 320;
            Height = 90;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            // Position in bottom-right corner
            Left = SystemParameters.PrimaryScreenWidth - Width - 30;
            Top = SystemParameters.PrimaryScreenHeight - Height - 50;

            // Use the shared ModernNotificationWindow style from App.xaml
            var mainBorder = new Border();
            mainBorder.SetResourceReference(StyleProperty, "ModernNotificationWindow");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Icon using shared style
            var iconPath = new System.Windows.Shapes.Path();
            iconPath.SetResourceReference(StyleProperty, "NotificationIconStyle");
            iconPath.Data = System.Windows.Media.Geometry.Parse(GetIconPath(title));
            iconPath.Fill = GetIconColor(title);
            Grid.SetColumn(iconPath, 0);
            grid.Children.Add(iconPath);

            var textPanel = new StackPanel
            {
                Margin = new Thickness(5, 10, 15, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textPanel, 1);

            // Title using shared style
            var titleBlock = new TextBlock { Text = title };
            titleBlock.SetResourceReference(StyleProperty, "NotificationTitleStyle");

            // Message using shared style
            var messageBlock = new TextBlock { Text = message };
            messageBlock.SetResourceReference(StyleProperty, "NotificationMessageStyle");

            textPanel.Children.Add(titleBlock);
            textPanel.Children.Add(messageBlock);
            grid.Children.Add(textPanel);

            mainBorder.Child = grid;
            Content = mainBorder;

            // Smooth fade-in animation
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
            };
            BeginAnimation(OpacityProperty, fadeIn);

            // Auto-close after 4 seconds with fade-out
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
                };
                fadeOut.Completed += (s2, e2) => Close();
                BeginAnimation(OpacityProperty, fadeOut);
            };
            timer.Start();

            // Click to close
            MouseDown += (s, e) =>
            {
                timer.Stop();
                Close();
            };
        }

        private string GetIconPath(string title)
        {
            return title.ToLower() switch
            {
                var t when t.Contains("error") => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z",
                var t when t.Contains("enabled") || t.Contains("success") => "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z",
                var t when t.Contains("disabled") => "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
                var t when t.Contains("text sent") => "M2.01 21L23 12 2.01 3 2 10l15 2-15 2z",
                _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"
            };
        }

        private System.Windows.Media.Brush GetIconColor(string title)
        {
            return title.ToLower() switch
            {
                var t when t.Contains("error") => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
                var t when t.Contains("enabled") || t.Contains("success") => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)),
                var t when t.Contains("disabled") => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175)),
                var t when t.Contains("text sent") => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
                _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(129, 140, 248))
            };
        }
    }
}

