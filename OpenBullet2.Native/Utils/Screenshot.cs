using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OpenBullet2.Native.Utils
{
    public static class Screenshot
    {
        public static void Take(Window window = null)
        {
            try
            {
                BitmapSource bitmap;
                
                if (window != null)
                {
                    // Capture only the window client area (excluding title bar and borders)
                    var handle = new WindowInteropHelper(window).Handle;
                    
                    // Get window rectangle
                    GetWindowRect(handle, out var rect);
                    
                    // Get client area to exclude title bar and borders
                    GetClientRect(handle, out var clientRect);
                    
                    // Calculate title bar and border sizes
                    var titleBarHeight = GetSystemMetrics(SM_CYCAPTION) + GetSystemMetrics(SM_CXBORDER);
                    var borderWidth = GetSystemMetrics(SM_CXBORDER);
                    
                    // Adjust coordinates to capture only client area
                    var windowLeft = rect.Left + borderWidth;
                    var windowTop = rect.Top + titleBarHeight;
                    var windowWidth = clientRect.Right;
                    var windowHeight = clientRect.Bottom;
                    
                    bitmap = CopyScreen(windowWidth, windowHeight, windowLeft, windowTop);
                }
                else
                {
                    // Fallback to full screen
                    var bounds = Screen.PrimaryScreen.Bounds;
                    bitmap = CopyScreen(bounds.Width, bounds.Height, bounds.X, bounds.Y);
                }

                // Copy it to the clipboard
                System.Windows.Clipboard.SetImage(bitmap);

                // Create Screenshots directory if it doesn't exist
                var screenshotsDir = "Screenshots";
                if (!Directory.Exists(screenshotsDir))
                {
                    Directory.CreateDirectory(screenshotsDir);
                }

                // Save with timestamp for better file management
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var filename = Path.Combine(screenshotsDir, $"screenshot_{timestamp}.png");
                
                GetBitmap(bitmap).Save(filename, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to take screenshot: {ex.Message}", 
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void Take(int width, int height, int top, int left)
        {
            // Legacy method for backward compatibility - now calls the improved version
            Take();
        }

        private static BitmapSource CopyScreen(int width, int height, int top, int left)
        {
            using var screenBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var bmpGraphics = Graphics.FromImage(screenBmp);
            bmpGraphics.CopyFromScreen(left, top, 0, 0, screenBmp.Size);

            return Imaging.CreateBitmapSourceFromHBitmap(
                screenBmp.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }

        private static Bitmap GetBitmap(BitmapSource source)
        {
            var bmp = new Bitmap(
              source.PixelWidth,
              source.PixelHeight,
              PixelFormat.Format32bppPArgb);

            var data = bmp.LockBits(
              new Rectangle(System.Drawing.Point.Empty, bmp.Size),
              ImageLockMode.WriteOnly,
              PixelFormat.Format32bppPArgb);

            source.CopyPixels(
              Int32Rect.Empty,
              data.Scan0,
              data.Height * data.Stride,
              data.Stride);

            bmp.UnlockBits(data);
            return bmp;
        }

        // Windows API declarations for precise window capture
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CYCAPTION = 4;
        private const int SM_CXBORDER = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
