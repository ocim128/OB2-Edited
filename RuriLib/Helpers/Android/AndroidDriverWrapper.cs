using OpenQA.Selenium.Appium.Android;
using System;

namespace RuriLib.Helpers.Android
{
    /// <summary>
    /// Wrapper for AndroidDriver that ensures proper cleanup when disposed.
    /// This allows automatic cleanup when the bot ends.
    /// </summary>
    public class AndroidDriverWrapper : IDisposable
    {
        public AndroidDriver Driver { get; }
        private bool _disposed = false;

        public AndroidDriverWrapper(AndroidDriver driver)
        {
            Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    Driver?.Quit();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            _disposed = true;
        }

        ~AndroidDriverWrapper()
        {
            Dispose(false);
        }
    }
}
