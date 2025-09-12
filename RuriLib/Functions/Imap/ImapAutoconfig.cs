using RuriLib.Functions.Networking;
using System.Collections.Generic;

namespace RuriLib.Functions.Imap
{
    /// <summary>
    /// IMAP autoconfig parser using shared base functionality
    /// </summary>
    public static class ImapAutoconfig
    {
        /// <summary>
        /// Parses IMAP autoconfig XML to extract IMAP server entries
        /// </summary>
        public static List<HostEntry> Parse(string xml)
        {
            return EmailAutoconfigBase.Parse(xml, "imap", true);
        }
    }
}
