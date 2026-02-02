namespace RuriLib.Providers.Android
{
    /// <summary>
    /// Default implementation of Android driver provider.
    /// </summary>
    public class AndroidDriverProvider : IAndroidDriverProvider
    {
        /// <inheritdoc/>
        public string AppiumServerUrl { get; set; } = "http://127.0.0.1:4723";

        /// <inheritdoc/>
        public string DeviceId { get; set; } = "emulator-5554";

        /// <inheritdoc/>
        public string PlatformVersion { get; set; } = "11.0";

        /// <inheritdoc/>
        public int CommandTimeoutSeconds { get; set; } = 60;

        /// <inheritdoc/>
        public bool FullReset { get; set; } = false;

        /// <inheritdoc/>
        public bool NoReset { get; set; } = true;

        /// <inheritdoc/>
        public string DefaultAppPackage { get; set; } = string.Empty;

        /// <inheritdoc/>
        public string DefaultAppActivity { get; set; } = string.Empty;
    }
}
