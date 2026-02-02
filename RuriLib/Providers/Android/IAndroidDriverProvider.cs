namespace RuriLib.Providers.Android
{
    /// <summary>
    /// Provider interface for Android Appium driver configuration.
    /// </summary>
    public interface IAndroidDriverProvider
    {
        /// <summary>
        /// The Appium server URL (e.g., "http://127.0.0.1:4723").
        /// </summary>
        string AppiumServerUrl { get; }

        /// <summary>
        /// The device/emulator ID (e.g., "emulator-5554").
        /// </summary>
        string DeviceId { get; }

        /// <summary>
        /// Android platform version (e.g., "11.0").
        /// </summary>
        string PlatformVersion { get; }

        /// <summary>
        /// Command timeout in seconds.
        /// </summary>
        int CommandTimeoutSeconds { get; }

        /// <summary>
        /// Whether to perform a full reset (uninstall app before session).
        /// </summary>
        bool FullReset { get; }

        /// <summary>
        /// Whether to skip resetting app state before session.
        /// </summary>
        bool NoReset { get; }

        /// <summary>
        /// Default app package to automate.
        /// </summary>
        string DefaultAppPackage { get; }

        /// <summary>
        /// Default app activity to launch.
        /// </summary>
        string DefaultAppActivity { get; }
    }
}
