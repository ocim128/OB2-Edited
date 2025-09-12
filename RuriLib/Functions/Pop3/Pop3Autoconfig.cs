using RuriLib.Functions.Networking;
using System.Collections.Generic;

namespace RuriLib.Functions.Pop3
{
    /// <summary>
    /// POP3 autoconfig parser using shared base functionality
    /// </summary>
    public static class Pop3Autoconfig
    {
        /// <summary>
        /// Parses POP3 autoconfig XML to extract POP3 server entries
        /// </summary>
        public static List<HostEntry> Parse(string xml)
        {
            return EmailAutoconfigBase.Parse(xml, "pop3", true);
        }
    }
}
