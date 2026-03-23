using RuriLib.Models.Settings;
using RuriLib.Services;
using System;
using System.Linq;
using System.Threading;

namespace RuriLib.Providers.Proxies
{
    public class DefaultProxySettingsProvider : IProxySettingsProvider
    {
        private readonly ProxySettings settings;

        public DefaultProxySettingsProvider(RuriLibSettingsService settings)
        {
            this.settings = settings.RuriLibSettings.ProxySettings;
        }

        public TimeSpan ConnectTimeout => NormalizeTimeout(settings.ProxyConnectTimeoutMilliseconds, nameof(settings.ProxyConnectTimeoutMilliseconds));

        public TimeSpan ReadWriteTimeout => NormalizeTimeout(settings.ProxyReadWriteTimeoutMilliseconds, nameof(settings.ProxyReadWriteTimeoutMilliseconds));

        public bool ContainsBanKey(string text, out string matchedKey, bool caseSensitive = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                matchedKey = null;
                return false;
            }

            matchedKey = settings.GlobalBanKeys.Where(k => !string.IsNullOrEmpty(k)).FirstOrDefault(k => text.Contains(k,
                caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

            return matchedKey != null;
        }

        public bool ContainsRetryKey(string text, out string matchedKey, bool caseSensitive = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                matchedKey = null;
                return false;
            }

            matchedKey = settings.GlobalRetryKeys.Where(k => !string.IsNullOrEmpty(k)).FirstOrDefault(k => text.Contains(k,
                caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

            return matchedKey != null;
        }

        private static TimeSpan NormalizeTimeout(int milliseconds, string parameterName)
        {
            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    milliseconds,
                    "Timeout must be zero or greater.");
            }

            return milliseconds == 0
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromMilliseconds(milliseconds);
        }
    }
}
