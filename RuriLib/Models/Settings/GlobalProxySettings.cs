using System.Collections.Generic;

namespace RuriLib.Models.Settings
{
    public class GlobalProxySettings
    {
        public int ProxyConnectTimeoutMilliseconds { get; set; } = 5000;
        public int ProxyReadWriteTimeoutMilliseconds { get; set; } = 60000;
        public List<string> GlobalBanKeys { get; set; } = new List<string>();
        public List<string> GlobalRetryKeys { get; set; } = new List<string>();
    }
}
