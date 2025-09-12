using RuriLib.Functions.Networking;
using System.Collections.Generic;

namespace RuriLib.Functions.Smtp
{
    /// <summary>
    /// SMTP autoconfig parser using shared base functionality
    /// </summary>
    public static class SmtpAutoconfig
    {
        /// <summary>
        /// Parses SMTP autoconfig XML to extract SMTP server entries
        /// </summary>
        public static List<HostEntry> Parse(string xml)
        {
            return EmailAutoconfigBase.Parse(xml, "smtp", false);
        }
    }
}
